using System.IO;

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Adapters.Settings;
using SystemResourceMonitor.Desktop.Bar;
using SystemResourceMonitor.Desktop.Lifecycle;
using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.TestHost;

/// <summary>Composes production lifecycle components with a real HWND/AppBar and an isolated settings root.</summary>
internal sealed class LifecycleRun
{
    private const string GuardName = @"Local\SystemResourceMonitor.TestHost-0C45C65E-BF8E-4486-91EB-AC0324790386";

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(string Marker, TimeSpan At)> _markers = [];
    private readonly List<string> _failures = [];
    private readonly ISettingsStore _settings;
    private readonly CodexLimitMonitor? _codex;
    private readonly ClaudeLimitMonitor? _claude;
    private BarWindow? _bar;
    private string? _textAtShutdown;

    /// <param name="settingsFactory">Creates the settings store in the isolated settings directory.</param>
    /// <param name="sources">Handle-free metric sources; by default, steady scripted CPU and RAM values.</param>
    /// <param name="codex">The Codex limit monitor to compose, as production does; none by default.</param>
    /// <param name="claude">The Claude limit monitor to compose, as production does; none by default.</param>
    public LifecycleRun(Func<string, ISettingsStore>? settingsFactory = null, (IMetricSource Cpu, IMetricSource Ram, IMetricSource Gpu)? sources = null,
        CodexLimitMonitor? codex = null, ClaudeLimitMonitor? claude = null)
    {
        _codex = codex;
        _claude = claude;
        Baseline = DesktopProbe.WorkArea();
        Info($"baseline-work-area {Baseline}");
        SettingsDirectory = Path.Combine(Path.GetTempPath(), "SystemResourceMonitor.TestHost", Guid.NewGuid().ToString("N"));
        _settings = settingsFactory?.Invoke(SettingsDirectory) ?? new JsonSettingsStore(SettingsDirectory);
        var (cpu, ram, gpu) = sources ?? (ScriptedSource.Steady(MetricKind.Cpu), ScriptedSource.Steady(MetricKind.Ram), ScriptedSource.Steady(MetricKind.Gpu));
        Monitor = new MetricMonitor(cpu, ram, gpu, TimeProvider.System);
    }

    /// <summary>Monotonic time of this run, on the same <see cref="Stopwatch"/> clock as <see cref="TimeProvider.System"/>.</summary>
    public TimeSpan Now => _clock.Elapsed;

    public PixelRect Baseline { get; }

    public string SettingsDirectory { get; }

    public ShutdownCoordinator? Shutdown { get; private set; }

    public int? ExitCode { get; private set; }

    /// <summary>The production metric monitor that <see cref="Run"/> starts.</summary>
    public MetricMonitor Monitor { get; }

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

        void Positioned(BarWindow bar)
        {
            _bar = bar;
            afterBarPositioned?.Invoke(bar, shutdown);
        }

        using (var startup = new DesktopStartup(GuardName, shutdown, _settings, app.Dispatcher, Monitor, Record, Positioned,
            codex: _codex, claude: _claude))
        {
            app.Startup += async (_, _) => await startup.RunAsync();
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

        // The coordinator records both markers on the bar's thread. Sources change their values on every read, so a
        // state published during the bounded wait would change the rendered text between them.
        if (marker == "wait-start" && _bar is not null)
        {
            _textAtShutdown = SectionTexts(_bar);
        }
        else if (marker == "exit" && _bar is not null)
        {
            Check(SectionTexts(_bar) == _textAtShutdown, "metrics were shown after shutdown began");
        }

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

    public int MarkerCount(string marker) => _markers.Count(recorded => recorded.Marker == marker);

    /// <summary>Finds the metric sections at the start of the bar, ahead of provider sections.</summary>
    public static (TextBlock Cpu, TextBlock Ram, TextBlock Gpu) Sections(BarWindow bar) =>
        bar.Content is StackPanel { Children: [TextBlock cpu, TextBlock ram, TextBlock gpu, ..] }
            ? (cpu, ram, gpu)
            : throw new InvalidOperationException("The bar content does not start with three metric sections.");

    /// <summary>Finds the provider sections that follow the metric sections.</summary>
    public static (TextBlock Codex, TextBlock Claude) ProviderSections(BarWindow bar) =>
        bar.Content is StackPanel { Children: [TextBlock, TextBlock, TextBlock, TextBlock codex, TextBlock claude, ..] }
            ? (codex, claude)
            : throw new InvalidOperationException("The bar content does not hold the Codex and Claude sections.");

    private static string SectionTexts(BarWindow bar)
    {
        var (cpu, ram, gpu) = Sections(bar);
        return $"{cpu.Text} | {ram.Text} | {gpu.Text}";
    }

    public static void Info(string line) => Console.WriteLine($"info: {line}");

    public void CheckPositionedBar(BarWindow bar)
    {
        var workArea = DesktopProbe.WorkArea();
        var windowRect = DesktopProbe.WindowRect(bar.Handle);
        Info($"edge {bar.AppliedEdge}");
        Info($"thickness {BarWindow.ThicknessDips} DIP = {bar.ThicknessPixels} px");
        Info($"setpos-rect {bar.ReservedBounds}");
        Info($"window-rect {windowRect}");
        Info($"work-area-with-bar {workArea}");

        var expected = bar.AppliedEdge switch
        {
            BarEdge.Top => Baseline with { Top = Baseline.Top + bar.ThicknessPixels },
            BarEdge.Bottom => Baseline with { Bottom = Baseline.Bottom - bar.ThicknessPixels },
            _ => throw new ArgumentOutOfRangeException(),
        };
        Check(workArea == expected, $"work area {workArea} differs from expected {expected} for {bar.AppliedEdge}");
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

        try
        {
            if (Directory.Exists(SettingsDirectory))
            {
                Directory.Delete(SettingsDirectory, recursive: true);
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FAIL: isolated settings cleanup failed with {exception.GetType().Name}");
            _failures.Add("isolated settings cleanup failed");
        }

        Console.WriteLine(_failures.Count == 0 ? "PASS" : "FAILED");
        return _failures.Count == 0 ? Program.Success : Program.AssertionFailed;
    }
}
