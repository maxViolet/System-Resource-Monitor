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
    /// <summary>
    /// Checks the three-region layout on a real HWND/AppBar at both edges: the published hosts start empty, and
    /// test-host-only placeholders wider than the free space neither move nor resize the fixed left sections.
    /// </summary>
    private static int BarRegions()
    {
        var run = new LifecycleRun(
            codex: new CodexLimitMonitor(new ImmediateCodexSource(), TimeProvider.System),
            claude: new ClaudeLimitMonitor(new ImmediateClaudeSource(), TimeProvider.System));
        run.Run((bar, _) =>
        {
            bar.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                CheckRegionStructure(run, bar);
                CheckRegionsAtEdge(run, bar);
                var other = bar.AppliedEdge == BarEdge.Top ? BarEdge.Bottom : BarEdge.Top;
                run.Check(bar.SelectEdge(other), $"the bar could not move to {other}");
                CheckRegionsAtEdge(run, bar);
                run.Check(DesktopProbe.RequestClose(bar.Handle), "the region check could not close the bar");
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
        run.Check(Grid.GetColumn(bar.LeftRegion) == 0 && Grid.GetColumn(bar.CenterRegion) == 1 && Grid.GetColumn(bar.RightRegion) == 2
                && bar.LeftRegion.Parent == grid && bar.CenterRegion.Parent == grid && bar.RightRegion.Parent == grid,
            "the left, centre, and right regions are not the grid's three columns");
        var expected = Sections(bar).ToArray();
        run.Check(bar.LeftRegion.Children.Cast<UIElement>().SequenceEqual(expected),
            "the left region does not hold exactly the CPU, RAM, GPU, and qualified provider sections in order");
        foreach (var host in new Panel[] { bar.CenterRegion, bar.RightRegion })
        {
            run.Check(host.Children.Count == 0, "a published centre or right host is not empty");
            run.Check(!host.Focusable && !host.IsHitTestVisible && host.ToolTip is null,
                "an empty host is a focus, hit-test, or tooltip surface");
        }
    }

    private static void CheckRegionsAtEdge(LifecycleRun run, BarWindow bar)
    {
        run.CheckPositionedBar(bar);
        bar.CenterRegion.Children.Clear();
        bar.RightRegion.Children.Clear();
        bar.UpdateLayout();
        var before = LeftBounds(bar);
        bar.CenterRegion.Children.Add(Placeholder());
        bar.RightRegion.Children.Add(Placeholder());
        bar.UpdateLayout();
        var after = LeftBounds(bar);
        LifecycleRun.Info($"left-sections {bar.AppliedEdge} dpi={VisualTreeHelper.GetDpi(bar).PixelsPerDip} [{Format(after)}]");

        run.Check(before.SequenceEqual(after),
            $"left section bounds at {bar.AppliedEdge} changed from [{Format(before)}] to [{Format(after)}]");
        run.Check(!bar.CenterRegion.IsHitTestVisible && !bar.RightRegion.IsHitTestVisible,
            "a placeholder region became a hit-test surface");
        run.Check(bar.ActualHeight == BarWindow.ThicknessDips,
            $"the bar is {bar.ActualHeight} DIP high, not {BarWindow.ThicknessDips}");
        run.Check(bar.LeftRegion.RenderSize.Width >= bar.LeftRegion.DesiredSize.Width,
            $"the left region at {bar.AppliedEdge} was arranged narrower than its fixed sections");
        foreach (var section in Sections(bar))
        {
            var right = section.TransformToAncestor(bar).Transform(new Point(section.ActualWidth, 0)).X;
            run.Check(right <= bar.ActualWidth && section.ActualWidth >= section.Width,
                $"a left section at {bar.AppliedEdge} is clipped: right edge {right} DIP, bar width {bar.ActualWidth} DIP");
        }
        bar.CenterRegion.Children.Clear();
        bar.RightRegion.Children.Clear();
    }

    /// <summary>All five sections, for the region check that composes both providers.</summary>
    private static TextBlock[] Sections(BarWindow bar) =>
        [bar.CpuSection, bar.RamSection, bar.GpuSection, bar.CodexSection, bar.ClaudeSection];

    private static int ProviderOmission(string omitted)
    {
        var codex = omitted is "codex" or "both"
            ? null
            : new CodexLimitMonitor(new ImmediateCodexSource(), TimeProvider.System);
        var claude = omitted is "claude" or "both"
            ? null
            : new ClaudeLimitMonitor(new ImmediateClaudeSource(), TimeProvider.System);
        var run = new LifecycleRun(codex: codex, claude: claude);
        run.Run((bar, _) =>
        {
            run.CheckPositionedBar(bar);
            var providers = LifecycleRun.ProviderSections(bar);
            run.Check((codex is null) == (providers.Codex is null),
                $"Codex section presence did not match the {omitted} configuration");
            run.Check((claude is null) == (providers.Claude is null),
                $"Claude section presence did not match the {omitted} configuration");
            var expected = 3 + (codex is null ? 0 : 1) + (claude is null ? 0 : 1);
            run.Check(bar.LeftRegion.Children.Count == expected,
                $"the left region holds {bar.LeftRegion.Children.Count} sections, not {expected}");
            run.Check(DesktopProbe.RequestClose(bar.Handle), "the provider omission check could not close the bar");
        });

        return run.Finish();
    }

    private static Border Placeholder() => new()
    {
        Width = 5_000,
        Height = BarWindow.ThicknessDips,
        Focusable = false,
        IsHitTestVisible = false,
    };

    private static SectionBounds[] LeftBounds(BarWindow bar)
    {
        var pixelsPerDip = VisualTreeHelper.GetDpi(bar).PixelsPerDip;
        return Sections(bar)
            .Select(section =>
            {
                var point = section.TransformToAncestor(bar).Transform(new Point(0, 0));
                return new SectionBounds(
                    Math.Round(point.X * pixelsPerDip),
                    Math.Round(section.ActualWidth * pixelsPerDip));
            })
            .ToArray();
    }

    private static string Format(IEnumerable<SectionBounds> bounds) =>
        string.Join(", ", bounds.Select(bound => $"{bound.X}px/{bound.Width}px"));

    private readonly record struct SectionBounds(double X, double Width);

    private sealed class ImmediateCodexSource : ICodexLimitSource
    {
        public Task<CodexReadResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CodexReadResult>(new CodexReadFailed(CodexUnavailableReason.ReadFailed));
    }

    private sealed class ImmediateClaudeSource : IClaudeLimitSource
    {
        public Task<ClaudeReadResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ClaudeReadResult>(new ClaudeReadFailed(ClaudeUnavailableReason.ReadFailed));
    }
}
