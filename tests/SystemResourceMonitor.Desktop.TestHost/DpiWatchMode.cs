using System.Runtime.InteropServices;
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
    /// Holds the bar for <paramref name="seconds"/> while a person changes the display scale, and logs every change in
    /// the monitor DPI, the HWND DPI and awareness, the WPF scale, the AppBar thickness, the window rectangle, and the
    /// work area. Each scale the bar settles at must fit every section; the run fails unless the bar settled at two or
    /// more scales, so it cannot pass without a live change, and at the end the bar must match the current scale.
    /// </summary>
    private static int DpiWatch(int seconds)
    {
        var completed = false;
        var run = new LifecycleRun(
            codex: new CodexLimitMonitor(new MissingCodexClient(), TimeProvider.System),
            claude: new ClaudeLimitMonitor(new MissingClaudeClient(), TimeProvider.System));
        run.Run((bar, shutdown) =>
        {
            run.Record("ready");
            string? last = null;
            var settled = new List<uint>();
            var deadline = run.Now + TimeSpan.FromSeconds(seconds);
            var timer = new DispatcherTimer(DispatcherPriority.Background, bar.Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) =>
            {
                var state = DpiState(bar);
                if (state != last)
                {
                    LifecycleRun.Info($"dpi-state t={run.Now.TotalSeconds:F1}s {state}");
                    last = state;
                }

                var dpi = DpiProbe.WindowDpi(bar.Handle);
                // A scale counts once Windows, WPF, and the AppBar all report it; then the whole geometry is checked.
                if (!settled.Contains(dpi) && dpi == DpiProbe.MonitorDpi(bar.Handle)
                    && bar.ThicknessPixels == AppBarPlacement.PhysicalThickness(BarWindow.ThicknessDips, dpi)
                    && Math.Abs(VisualTreeHelper.GetDpi(bar).PixelsPerDip - dpi / 96.0) < 0.001)
                {
                    settled.Add(dpi);
                    bar.UpdateLayout();
                    CheckGeometry(run, bar, $"DPI {dpi}");
                }

                if (run.Now < deadline)
                {
                    return;
                }

                timer.Stop();
                LifecycleRun.Info($"settled-dpis [{string.Join(", ", settled)}]");
                run.Check(settled.Count >= 2, $"the bar settled at DPI [{string.Join(", ", settled)}]; no live scale change was observed");
                CheckGeometry(run, bar, "final");
                run.RebaseAfterDisplayChange(bar);
                run.CheckPositionedBar(bar);
                completed = true;
                run.Check(DesktopProbe.RequestClose(bar.Handle), "the DPI watch could not close the bar");
            };
            timer.Start();
        });

        run.Check(completed, "the DPI watch ended before its deadline and final geometry checks");
        run.Check(run.ExitCode == SystemResourceMonitor.Desktop.Lifecycle.ExitCodes.Success, $"the bar exited with {run.ExitCode?.ToString() ?? "no exit code"}");
        return run.Finish();
    }

    /// <summary>
    /// The HWND, WPF, and AppBar match the monitor's current DPI, the window fills its reservation next to the work
    /// area, and every shown section keeps its fixed width, fits its text, and ends inside the bar.
    /// </summary>
    private static void CheckGeometry(LifecycleRun run, BarWindow bar, string state)
    {
        var monitorDpi = DpiProbe.MonitorDpi(bar.Handle);
        run.Check(DpiProbe.WindowDpi(bar.Handle) == monitorDpi,
            $"{state}: the bar HWND has DPI {DpiProbe.WindowDpi(bar.Handle)}, the monitor {monitorDpi}");
        run.Check(bar.ThicknessPixels == AppBarPlacement.PhysicalThickness(BarWindow.ThicknessDips, monitorDpi),
            $"{state}: the bar is {bar.ThicknessPixels} px thick, not {BarWindow.ThicknessDips} DIP at DPI {monitorDpi}");
        run.Check(Math.Abs(VisualTreeHelper.GetDpi(bar).PixelsPerDip - monitorDpi / 96.0) < 0.001,
            $"{state}: WPF renders at {VisualTreeHelper.GetDpi(bar).PixelsPerDip}x, the monitor at {monitorDpi / 96.0}x");
        run.Check(bar.ActualHeight == BarWindow.ThicknessDips, $"{state}: the bar is {bar.ActualHeight} DIP high");
        run.Check(bar.ReservedBounds == DesktopProbe.WindowRect(bar.Handle), $"{state}: the bar window does not match its reservation");
        var monitor = DesktopProbe.MonitorBounds(bar.Handle);
        run.Check(bar.ReservedBounds.Height == bar.ThicknessPixels,
            $"{state}: the reservation {bar.ReservedBounds} is {bar.ReservedBounds.Height} px high, not {bar.ThicknessPixels}");
        run.Check(bar.ReservedBounds.Left == monitor.Left && bar.ReservedBounds.Right == monitor.Right,
            $"{state}: the reservation {bar.ReservedBounds} does not span the monitor {monitor}");
        var workArea = DesktopProbe.WorkArea();
        run.Check(bar.AppliedEdge == BarEdge.Top ? workArea.Top == bar.ReservedBounds.Bottom : workArea.Bottom == bar.ReservedBounds.Top,
            $"{state}: work area {workArea} does not border the reservation {bar.ReservedBounds}");

        var pixelsPerDip = VisualTreeHelper.GetDpi(bar).PixelsPerDip;
        foreach (var section in bar.LeftRegion.Children.OfType<System.Windows.Controls.TextBlock>())
        {
            var natural = BarWindow.MeasureWidth(section.Text, pixelsPerDip);
            var right = section.TranslatePoint(new System.Windows.Point(section.ActualWidth, 0), bar).X;
            LifecycleRun.Info($"section {state} '{section.Text}' natural {natural:F2} width {section.ActualWidth:F2} right {right:F2} of {bar.ActualWidth:F2}");
            run.Check(section.ActualWidth == section.Width, $"{state}: '{section.Text}' was arranged at {section.ActualWidth} DIP, not its fixed {section.Width}");
            run.Check(natural <= section.Width, $"{state}: '{section.Text}' needs {natural:F2} DIP in a {section.Width:F2} DIP section");
            run.Check(right <= bar.ActualWidth, $"{state}: '{section.Text}' ends at {right:F2}, outside the {bar.ActualWidth:F2} DIP bar");
        }
    }

    private static string DpiState(BarWindow bar) =>
        $"monitor-dpi={DpiProbe.MonitorDpi(bar.Handle)} hwnd-dpi={DpiProbe.WindowDpi(bar.Handle)} awareness={DpiProbe.Awareness(bar.Handle)} " +
        $"wpf-scale={VisualTreeHelper.GetDpi(bar).PixelsPerDip} thickness={bar.ThicknessPixels}px " +
        $"window={DesktopProbe.WindowRect(bar.Handle)} reserved={bar.ReservedBounds} work-area={DesktopProbe.WorkArea()} " +
        $"bar-dip={bar.ActualWidth:F1}x{bar.ActualHeight:F1}";
}

/// <summary>Reads the DPI of the bar's monitor and HWND as Windows reports them, independently of WPF.</summary>
internal static partial class DpiProbe
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    public static uint WindowDpi(nint hwnd) => GetDpiForWindow(hwnd);

    public static uint MonitorDpi(nint hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        return GetDpiForMonitor(monitor, MdtEffectiveDpi, out var x, out _) == 0 ? x : 0;
    }

    public static int Awareness(nint hwnd) => GetAwarenessFromDpiAwarenessContext(GetWindowDpiAwarenessContext(hwnd));

    private const int GwlExStyle = -20;

    public static void AddExtendedStyle(nint hwnd, nint flags) =>
        SetWindowLongPtr(hwnd, GwlExStyle, DesktopProbe.ExtendedStyle(hwnd) | flags);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowDpiAwarenessContext(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial int GetAwarenessFromDpiAwarenessContext(nint context);
}

public static partial class Program
{
    /// <summary>
    /// Reproduces ADR 0027's evidence: shows an ordinary window, a WS_EX_NOACTIVATE window, and a WS_EX_TOOLWINDOW
    /// window for <paramref name="seconds"/> while a person changes the display scale, and logs each window's DPI. It
    /// asserts nothing about Windows; it fails only when no live scale change was observed.
    /// </summary>
    private static int DpiCompare(int seconds)
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        var failed = false;
        app.Startup += (_, _) =>
        {
            var windows = new (string Name, nint Handle)[]
            {
                ("plain", ComparisonWindow("plain", 0, 100)),
                ("noactivate", ComparisonWindow("noactivate", DesktopProbe.WS_EX_NOACTIVATE, 220)),
                ("toolwindow", ComparisonWindow("toolwindow", DesktopProbe.WS_EX_TOOLWINDOW, 340)),
            };
            var monitorDpis = new HashSet<uint>();
            string? last = null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) =>
            {
                var monitorDpi = DpiProbe.MonitorDpi(windows[0].Handle);
                monitorDpis.Add(monitorDpi);
                var state = $"monitor-dpi={monitorDpi} " + string.Join(" ", windows.Select(w => $"{w.Name}-dpi={DpiProbe.WindowDpi(w.Handle)}"));
                if (state != last)
                {
                    LifecycleRun.Info($"dpi-compare t={clock.Elapsed.TotalSeconds:F1}s {state}");
                    last = state;
                }

                if (clock.Elapsed < TimeSpan.FromSeconds(seconds))
                {
                    return;
                }

                timer.Stop();
                failed = monitorDpis.Count < 2;
                if (failed)
                {
                    Console.WriteLine($"FAIL: the monitor stayed at DPI [{string.Join(", ", monitorDpis)}]; no live scale change was observed");
                }

                app.Shutdown();
            };
            timer.Start();
        };
        app.Run();
        Console.WriteLine(failed ? "FAILED" : "PASS");
        return failed ? AssertionFailed : Success;
    }

    private static nint ComparisonWindow(string title, nint extendedStyle, double top)
    {
        var window = new System.Windows.Window
        {
            Title = $"SRM DPI comparison ({title})",
            Width = 260,
            Height = 80,
            Left = 300,
            Top = top,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
        };
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        if (extendedStyle != 0)
        {
            DpiProbe.AddExtendedStyle(hwnd, extendedStyle);
        }

        window.Show();
        return hwnd;
    }
}
