using System.Diagnostics;
using System.Windows;
using SystemResourceMonitor.Desktop.Bar;
using SystemResourceMonitor.Desktop.Lifecycle;

namespace SystemResourceMonitor.Desktop.TestHost;

/// <summary>
/// Composes the production lifecycle with a real HWND and AppBar, prints each marker, and collects assertion failures.
/// </summary>
internal sealed class LifecycleRun
{
    // Separate from the production guard so the test host never competes with a running bar.
    private const string GuardName = @"Local\SystemResourceMonitor.TestHost-0C45C65E-BF8E-4486-91EB-AC0324790386";

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(string Marker, TimeSpan At)> _markers = [];
    private readonly List<string> _failures = [];

    public LifecycleRun()
    {
        Baseline = DesktopProbe.WorkArea();
        Info($"baseline-work-area {Baseline}");
    }

    public PixelRect Baseline { get; }

    public ShutdownCoordinator? Shutdown { get; private set; }

    public int? ExitCode { get; private set; }

    /// <summary>
    /// Runs startup on this thread's dispatcher until the coordinator exits. <paramref name="afterBarPositioned"/>
    /// is the test host's injection point after <c>ABM_SETPOS</c>.
    /// </summary>
    public void Run(Action<BarWindow, ShutdownCoordinator>? afterBarPositioned = null)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var shutdown = new ShutdownCoordinator(
            new CancellationTokenSource(),
            TimeProvider.System,
            code =>
            {
                ExitCode = code;
                app.Shutdown(code);
            },
            Record);
        Shutdown = shutdown;

        using (var startup = new DesktopStartup(GuardName, shutdown, Record, bar => afterBarPositioned?.Invoke(bar, shutdown)))
        {
            app.Startup += (_, _) => startup.Run();
            app.Run();
        }

        var final = DesktopProbe.WorkArea();
        Info($"final-work-area {final}");
        Check(final == Baseline, $"work area at exit {final} differs from baseline {Baseline}");
    }

    public void Record(string marker)
    {
        _markers.Add((marker, _clock.Elapsed));
        Console.WriteLine(marker);

        if (marker == "remove")
        {
            var workArea = DesktopProbe.WorkArea();
            Info($"work-area-after-remove {workArea}");
            if (workArea == Baseline)
            {
                Record("restored");
            }
        }
    }

    public static void Info(string line) => Console.WriteLine($"info: {line}");

    /// <summary>Checks the reservation the bar made: exact Top-edge shrink, window rectangle, and non-activating style.</summary>
    public void CheckPositionedBar(BarWindow bar)
    {
        var workArea = DesktopProbe.WorkArea();
        var windowRect = DesktopProbe.WindowRect(bar.Handle);
        Info($"thickness {BarWindow.ThicknessDips} DIP = {bar.ThicknessPixels} px");
        Info($"setpos-rect {bar.ReservedBounds}");
        Info($"window-rect {windowRect}");
        Info($"work-area-with-bar {workArea}");

        Check(
            workArea == Baseline with { Top = Baseline.Top + bar.ThicknessPixels },
            $"work area {workArea} is not the baseline {Baseline} shrunk on the Top edge by {bar.ThicknessPixels} px");
        Check(windowRect == bar.ReservedBounds, $"window rectangle {windowRect} differs from ABM_SETPOS {bar.ReservedBounds}");

        const nint required = DesktopProbe.WS_EX_NOACTIVATE | DesktopProbe.WS_EX_TOOLWINDOW;
        Check((DesktopProbe.ExtendedStyle(bar.Handle) & required) == required, "the bar lacks WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW");
    }

    public void Check(bool condition, string failure)
    {
        if (!condition)
        {
            _failures.Add(failure);
        }
    }

    /// <summary>Checks that every marker occurred, in this order.</summary>
    public void CheckOrder(params string[] expected)
    {
        var positions = expected.Select(marker => _markers.FindIndex(recorded => recorded.Marker == marker)).ToList();
        Check(
            positions.All(position => position >= 0) && positions.SequenceEqual(positions.Order()),
            $"markers [{string.Join(", ", _markers.Select(recorded => recorded.Marker))}] do not contain [{string.Join(", ", expected)}] in order");
    }

    public TimeSpan Between(string first, string second) =>
        _markers.Find(recorded => recorded.Marker == second).At - _markers.Find(recorded => recorded.Marker == first).At;

    public int Finish()
    {
        foreach (var failure in _failures)
        {
            Console.WriteLine($"FAIL: {failure}");
        }

        Console.WriteLine(_failures.Count == 0 ? "PASS" : "FAILED");
        return _failures.Count == 0 ? Program.Success : Program.AssertionFailed;
    }
}
