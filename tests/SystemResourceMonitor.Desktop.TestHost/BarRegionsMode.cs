using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Desktop.Bar;
using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.TestHost;

public static partial class Program
{
    /// <summary>Wider than any supported monitor, so a placeholder always asks for more than the free space.</summary>
    private const double PlaceholderWidthDips = 5_000;

    /// <summary>
    /// Checks the three-region layout on a real HWND/AppBar at both edges. The published centre and right hosts start
    /// empty, and test-host-only placeholders wider than the free space neither move, resize, nor cover the fixed left
    /// sections.
    /// </summary>
    private static int BarRegions()
    {
        var run = new LifecycleRun(
            codex: new CodexLimitMonitor(new MissingCodexClient(), TimeProvider.System),
            claude: new ClaudeLimitMonitor(new MissingClaudeClient(), TimeProvider.System));
        run.Run((bar, shutdown) =>
        {
            bar.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    CheckRegionStructure(run, bar);
                    CheckRegionsAtEdge(run, bar);
                    var other = bar.AppliedEdge == BarEdge.Top ? BarEdge.Bottom : BarEdge.Top;
                    run.Check(bar.SelectEdge(other), $"the bar could not move to {other}");
                    CheckRegionsAtEdge(run, bar);
                }
                catch (Exception exception)
                {
                    run.Check(false, $"the region check failed with {exception.GetType().Name}: {exception.Message}");
                }
                finally
                {
                    run.Check(DesktopProbe.RequestClose(bar.Handle), "the region check could not close the bar");
                }
            }));
        });

        return run.Finish();
    }

    private static void CheckRegionStructure(LifecycleRun run, BarWindow bar)
    {
        var grid = bar.Content as Grid;
        run.Check(grid is { ColumnDefinitions.Count: 3 }
                && grid.ColumnDefinitions[0].Width.IsAuto
                && grid.ColumnDefinitions[1].Width.IsStar
                && grid.ColumnDefinitions[2].Width.IsAuto,
            "the bar content is not an Auto / * / Auto grid");
        run.Check(grid is not null
                && bar.LeftRegion.Parent == grid && Grid.GetColumn(bar.LeftRegion) == 0
                && bar.CenterRegion.Parent == grid && Grid.GetColumn(bar.CenterRegion) == 1
                && bar.RightRegion.Parent == grid && Grid.GetColumn(bar.RightRegion) == 2,
            "the left, centre, and right regions are not the grid's three columns");
        run.Check(bar.LeftRegion.Children.Cast<UIElement>().SequenceEqual(AllSections(bar)),
            "the left region does not hold exactly the CPU, RAM, GPU, Codex, and Claude sections in order");
        foreach (var host in new Panel[] { bar.CenterRegion, bar.RightRegion })
        {
            run.Check(host.Children.Count == 0, "a published centre or right host is not empty");
            run.Check(!host.Focusable && host.ToolTip is null, "an empty host is a focus or tooltip surface");
        }
    }

    private static void CheckRegionsAtEdge(LifecycleRun run, BarWindow bar)
    {
        run.CheckPositionedBar(bar);
        bar.UpdateLayout();
        var before = LeftBounds(bar);
        bar.CenterRegion.Children.Add(Placeholder(Brushes.DarkRed));
        bar.RightRegion.Children.Add(Placeholder(Brushes.DarkBlue));
        bar.UpdateLayout();
        var after = LeftBounds(bar);
        LifecycleRun.Info($"left-sections {bar.AppliedEdge} dpi={VisualTreeHelper.GetDpi(bar).PixelsPerDip} [{Format(after)}]");

        run.Check(before.SequenceEqual(after),
            $"left section bounds at {bar.AppliedEdge} changed from [{Format(before)}] to [{Format(after)}]");
        run.Check(bar.ActualHeight == BarWindow.ThicknessDips,
            $"the bar is {bar.ActualHeight} DIP high, not {BarWindow.ThicknessDips}");
        var leftEnd = bar.LeftRegion.TranslatePoint(new Point(bar.LeftRegion.ActualWidth, 0), bar).X;
        foreach (var host in new Panel[] { bar.CenterRegion, bar.RightRegion })
        {
            var start = host.TranslatePoint(new Point(0, 0), bar).X;
            run.Check(start >= leftEnd, $"a placeholder host starts at {start} DIP, inside the left region ending at {leftEnd} DIP");
            run.Check(host.ClipToBounds, "a placeholder host does not clip its content to its region");
            var end = host.TranslatePoint(new Point(host.ActualWidth, 0), bar).X;
            run.Check(end <= bar.ActualWidth + 0.5,
                $"a placeholder host at {bar.AppliedEdge} ends at {end} DIP, beyond the {bar.ActualWidth} DIP bar");
        }

        foreach (var section in AllSections(bar))
        {
            var right = section.TranslatePoint(new Point(section.ActualWidth, 0), bar).X;
            run.Check(right <= bar.ActualWidth && section.ActualWidth == section.Width,
                $"a left section at {bar.AppliedEdge} is clipped: right edge {right} DIP, bar width {bar.ActualWidth} DIP");
            var center = section.TranslatePoint(new Point(section.ActualWidth / 2, section.ActualHeight / 2), bar);
            run.Check(bar.InputHitTest(center) == section,
                $"a left section at {bar.AppliedEdge} does not receive the pointer at its centre, so its tooltip is unreachable");
        }

        bar.CenterRegion.Children.Clear();
        bar.RightRegion.Children.Clear();
    }

    private static TextBlock[] AllSections(BarWindow bar) =>
        [bar.CpuSection, bar.RamSection, bar.GpuSection, bar.CodexSection, bar.ClaudeSection];

    private static Border Placeholder(Brush fill) => new()
    {
        Width = PlaceholderWidthDips,
        Height = BarWindow.ThicknessDips,
        Background = fill,
        Focusable = false,
    };

    private static SectionBounds[] LeftBounds(BarWindow bar)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(bar).PixelsPerDip;
        return AllSections(bar)
            .Select(section =>
            {
                var point = section.TranslatePoint(new Point(0, 0), bar);
                return new SectionBounds(Math.Round(point.X * pixelsPerDip), Math.Round(section.ActualWidth * pixelsPerDip));
            })
            .ToArray();
    }

    private static string Format(IEnumerable<SectionBounds> bounds) =>
        string.Join(", ", bounds.Select(bound => $"{bound.X}px/{bound.Width}px"));

    private readonly record struct SectionBounds(double X, double Width);
}
