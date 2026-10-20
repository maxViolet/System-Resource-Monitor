using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;

using SystemResourceMonitor.Desktop.Bar;

using SystemResourceMonitor.Desktop.Adapters.Settings;
using SystemResourceMonitor.Desktop.Lifecycle;
using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.TestHost;

/// <summary>
/// Entry point of the test host. Fault modes compose the production lifecycle with a real AppBar; the published
/// Desktop app has none of them.
/// </summary>
public static partial class Program
{
    public const int Success = 0;
    public const int InvalidInvocation = 2;
    public const int AssertionFailed = 3;

    private const string Usage = "Usage: SystemResourceMonitor.Desktop.TestHost --smoke | --fail-after-setpos | --bounded-shutdown | --faulted-metric-run | --hold-bar | --edge-switches | --fullscreen-reconciliation | --settings-read-failure | --settings-backup-failure | --settings-write-failure | --shutdown-during-settings-load | --metric-freshness [cpu|ram|gpu] | --metric-display [seconds] | --native-reads <seconds> | --gpu-probe | --gpu-correlation <seconds> | --codex-probe <polls> <interval-seconds>";

    [STAThread]
    public static int Main(string[] args)
    {
        switch (args)
        {
            case ["--smoke"]:
                Console.WriteLine("SystemResourceMonitor.Desktop.TestHost: smoke OK");
                return Success;
            case ["--fail-after-setpos"]:
                return FailAfterSetPos();
            case ["--bounded-shutdown"]:
                return BoundedShutdown();
            case ["--faulted-metric-run"]:
                return FaultedMetricRun();
            case ["--hold-bar"]:
                return HoldBar();
            case ["--edge-switches"]:
                return EdgeSwitches();
            case ["--fullscreen-reconciliation"]:
                return FullscreenReconciliation();
            case ["--settings-read-failure"]:
                return SettingsFailure(SettingsFault.Read);
            case ["--settings-backup-failure"]:
                return SettingsFailure(SettingsFault.Backup);
            case ["--settings-write-failure"]:
                return SettingsFailure(SettingsFault.Write);
            case ["--shutdown-during-settings-load"]:
                return ShutdownDuringSettingsLoad();
            case ["--metric-freshness"] or ["--metric-freshness", "cpu"]:
                return MetricFreshness(Core.Metrics.MetricKind.Cpu);
            case ["--metric-freshness", "ram"]:
                return MetricFreshness(Core.Metrics.MetricKind.Ram);
            case ["--metric-freshness", "gpu"]:
                return MetricFreshness(Core.Metrics.MetricKind.Gpu);
            case ["--metric-display"]:
                return MetricDisplay(TimeSpan.Zero);
            case ["--metric-display", var seconds] when TryParseSeconds(seconds, out var hold):
                return MetricDisplay(hold);
            case ["--native-reads", var seconds] when TryParseSeconds(seconds, out var duration):
                return NativeReads(duration);
            case ["--gpu-probe"]:
                return GpuProbe();
            case ["--gpu-correlation", var seconds] when TryParseSeconds(seconds, out var gpuDuration):
                return GpuCorrelation(gpuDuration);
            case ["--codex-probe", var count, var seconds] when int.TryParse(count, out var polls) && polls > 0
                && TryParseSeconds(seconds, out var interval):
                return CodexProbe(polls, interval);
            case ["app-server", "--listen", "stdio://"]:
                return FakeCodexAppServer();
            case [FakeGrandchildArgument]:
                Thread.Sleep(Timeout.Infinite);
                return Success;
            default:
                Console.Error.WriteLine(Usage);
                return InvalidInvocation;
        }
    }

    /// <summary>
    /// A handled startup failure after <c>ABM_SETPOS</c> restores the work area before the bounded wait.
    /// </summary>
    private static int FailAfterSetPos()
    {
        var run = new LifecycleRun();
        var injected = false;
        run.Run((bar, _) =>
        {
            run.CheckPositionedBar(bar);
            injected = true;
            run.Record("inject-failure");
            throw new InjectedStartupFailureException();
        });

        run.Check(injected, "the failure was not injected");
        run.Check(run.ExitCode == ExitCodes.Failure, $"coordinator exit code {run.ExitCode}, expected {ExitCodes.Failure}");
        run.CheckOrder(
            "guard-acquired",
            "settings-start",
            "bar-positioned",
            "inject-failure",
            $"startup-failed:{nameof(InjectedStartupFailureException)}",
            "remove",
            "restored",
            "wait-start",
            "exit");
        return run.Finish();
    }

    /// <summary>
    /// A defect faults the running metric monitor after startup (ADR 0011): a <c>Changed</c> handler throws, which stops
    /// both polling loops. The coordinator records the owner failure, removes the AppBar, restores the work area before
    /// the bounded wait, and exits with the failure exit code.
    /// </summary>
    private static int FaultedMetricRun()
    {
        var run = new LifecycleRun();
        run.Run((bar, _) =>
        {
            run.CheckPositionedBar(bar);
            run.Monitor.Changed += (_, _) => throw new InjectedCollectorFaultException();
            run.Record("fault-armed");
        });

        var waited = run.Between("wait-start", "exit");
        LifecycleRun.Info($"wait-ms {waited.TotalMilliseconds:F0}");
        run.Check(waited <= ShutdownCoordinator.WaitDeadline, $"waited {waited.TotalMilliseconds:F0} ms after the faulted run");
        run.Check(run.MarkerCount("pending:metric-monitor") == 0, "the faulted metric monitor was still pending at the deadline");
        run.Check(run.ExitCode == ExitCodes.Failure, $"coordinator exit code {run.ExitCode}, expected {ExitCodes.Failure}");
        run.CheckOrder(
            "guard-acquired",
            "bar-positioned",
            "metrics-started",
            "fault-armed",
            $"owner-failed:metric-monitor:{nameof(InjectedCollectorFaultException)}",
            "remove",
            "restored",
            "wait-start",
            "exit");
        return run.Finish();
    }

    /// <summary>
    /// Normal close with handle-free pending work: the work area is restored before the wait, the wait ends at the
    /// deadline with the pending operations recorded, and a result that arrives afterwards cannot publish.
    /// </summary>
    private static int BoundedShutdown()
    {
        var run = new LifecycleRun();
        var neverCompleting = new TaskCompletionSource();
        var late = new TaskCompletionSource();
        run.Run((bar, shutdown) =>
        {
            run.CheckPositionedBar(bar);
            shutdown.Track("never-completing-source", neverCompleting.Task);
            shutdown.Track("late-source", late.Task);
            bar.Dispatcher.BeginInvoke(() => run.Check(DesktopProbe.RequestClose(bar.Handle), "WM_CLOSE was not processed"));
        });

        late.SetResult();
        var published = false;
        run.Check(run.Shutdown?.TryPublish(() => published = true) == false && !published, "a late result published after shutdown");

        var waited = run.Between("wait-start", "exit");
        LifecycleRun.Info($"wait-ms {waited.TotalMilliseconds:F0}");
        run.Check(
            waited >= ShutdownCoordinator.WaitDeadline - TimeSpan.FromMilliseconds(20) && waited <= ShutdownCoordinator.WaitDeadline + TimeSpan.FromMilliseconds(500),
            $"waited {waited.TotalMilliseconds:F0} ms, expected the {ShutdownCoordinator.WaitDeadline.TotalMilliseconds:F0} ms deadline");
        run.Check(run.ExitCode == ExitCodes.Success, $"coordinator exit code {run.ExitCode}, expected {ExitCodes.Success}");
        run.CheckOrder(
            "bar-positioned",
            "remove",
            "restored",
            "wait-start",
            "pending:never-completing-source",
            "pending:late-source",
            "exit");
        return run.Finish();
    }

    private static int FullscreenReconciliation()
    {
        var run = new LifecycleRun();
        run.Run((bar, shutdown) =>
        {
            run.CheckPositionedBar(bar);
            _ = ExerciseAsync();

            async Task ExerciseAsync()
            {
                Window? controlled = null;
                Window? normal = null;
                Window? maximized = null;
                try
                {
                    var reservedWorkArea = DesktopProbe.WorkArea();
                    var reservedBounds = bar.ReservedBounds;
                    var monitorBounds = DesktopProbe.MonitorBounds(bar.Handle);
                    controlled = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        ShowInTaskbar = false,
                        ShowActivated = true,
                    };
                    var controlledHandle = new WindowInteropHelper(controlled).EnsureHandle();
                    controlled.Show();

                    var lowering = Stopwatch.StartNew();
                    run.Check(
                        DesktopProbe.SetForegroundBounds(controlledHandle, monitorBounds),
                        "the controlled fullscreen window could not become foreground");
                    run.Check(
                        await WaitForMarkerAsync(run, "zorder:Lowered", expectedCount: 1, TimeSpan.FromSeconds(1)),
                        "the bar did not lower within one second");
                    LifecycleRun.Info($"lower-ms {lowering.Elapsed.TotalMilliseconds:F0}");
                    run.Check(DesktopProbe.WorkArea() == reservedWorkArea, "fullscreen changed the reserved work area");
                    run.Check(DesktopProbe.WindowRect(bar.Handle) == reservedBounds, "lowering moved or resized the bar");
                    run.Check(DesktopProbe.ForegroundWindow() == controlledHandle, "lowering stole foreground focus");
                    run.Check(
                        (DesktopProbe.ExtendedStyle(bar.Handle) & DesktopProbe.WS_EX_TOPMOST) == 0,
                        "the lowered bar remained topmost");

                    var normalBounds = new PixelRect(
                        monitorBounds.Left + 200,
                        monitorBounds.Top + 150,
                        monitorBounds.Right - 200,
                        monitorBounds.Bottom - 150);
                    normal = new Window
                    {
                        WindowStyle = WindowStyle.SingleBorderWindow,
                        ResizeMode = ResizeMode.CanResize,
                        ShowInTaskbar = false,
                        ShowActivated = true,
                    };
                    var normalHandle = new WindowInteropHelper(normal).EnsureHandle();
                    normal.Show();
                    var restoring = Stopwatch.StartNew();
                    run.Check(
                        DesktopProbe.SetForegroundBounds(normalHandle, normalBounds),
                        "the separate normal window could not become foreground");
                    run.Check(
                        await WaitForMarkerAsync(run, "zorder:Normal", expectedCount: 1, TimeSpan.FromSeconds(1)),
                        "the bar did not restore within one second after switching to a normal window");
                    LifecycleRun.Info($"restore-ms {restoring.Elapsed.TotalMilliseconds:F0}");
                    run.Check(DesktopProbe.WindowRect(controlledHandle) == monitorBounds, "the fullscreen window changed bounds during the switch");
                    run.Check(DesktopProbe.ForegroundWindow() == normalHandle, "restoration stole foreground focus");
                    run.Check(
                        (DesktopProbe.ExtendedStyle(bar.Handle) & DesktopProbe.WS_EX_TOPMOST) != 0,
                        "the restored bar did not regain topmost z-order");

                    run.Check(
                        DesktopProbe.SetForegroundBounds(controlledHandle, monitorBounds),
                        "the still-alive fullscreen window could not become foreground again");
                    run.Check(
                        await WaitForMarkerAsync(run, "zorder:Lowered", expectedCount: 2, TimeSpan.FromSeconds(1)),
                        "the bar did not lower again when returning to fullscreen");
                    run.Check(DesktopProbe.ForegroundWindow() == controlledHandle, "returning to fullscreen lost foreground focus");

                    run.Check(
                        DesktopProbe.SetForegroundBounds(normalHandle, normalBounds),
                        "the normal window could not become foreground after returning from fullscreen");
                    run.Check(
                        await WaitForMarkerAsync(run, "zorder:Normal", expectedCount: 2, TimeSpan.FromSeconds(1)),
                        "the bar did not restore after the second fullscreen departure");
                    run.Check(DesktopProbe.WorkArea() == reservedWorkArea, "fullscreen cycles changed the reserved work area");
                    run.Check(DesktopProbe.WindowRect(bar.Handle) == reservedBounds, "fullscreen cycles moved or resized the bar");

                    controlled.Close();
                    controlled = null;
                    normal.Close();
                    normal = null;
                    var loweredCount = run.MarkerCount("zorder:Lowered");
                    var indeterminateCount = run.MarkerCount("fullscreen-indeterminate");
                    maximized = new Window
                    {
                        WindowStyle = WindowStyle.SingleBorderWindow,
                        ResizeMode = ResizeMode.CanResize,
                        ShowInTaskbar = false,
                        ShowActivated = true,
                    };
                    var maximizedHandle = new WindowInteropHelper(maximized).EnsureHandle();
                    maximized.Show();
                    maximized.WindowState = WindowState.Maximized;
                    await Task.Delay(TimeSpan.FromMilliseconds(50));
                    run.Check(
                        DesktopProbe.SetForegroundBounds(maximizedHandle, DesktopProbe.WindowRect(maximizedHandle)),
                        "the maximized window could not become foreground");
                    await Task.Delay(TimeSpan.FromMilliseconds(750));
                    run.Check(run.MarkerCount("zorder:Lowered") == loweredCount, "an ordinary maximized window lowered the bar");
                    run.Check(
                        run.MarkerCount("fullscreen-indeterminate") == indeterminateCount,
                        "the maximized negative case produced only indeterminate observations");
                    run.Check(DesktopProbe.WorkArea() == reservedWorkArea, "the maximized negative case changed the reservation");
                }
                catch (Exception exception)
                {
                    run.Check(false, $"fullscreen reconciliation probe failed with {exception.GetType().Name}");
                }
                finally
                {
                    maximized?.Close();
                    normal?.Close();
                    controlled?.Close();
                    run.Check(DesktopProbe.RequestClose(bar.Handle), "WM_CLOSE was not processed after fullscreen reconciliation");
                }
            }
        });

        run.CheckOrder("bar-positioned", "zorder:Lowered", "zorder:Normal", "remove", "restored");
        return run.Finish();
    }

    private static async Task<bool> WaitForMarkerAsync(
        LifecycleRun run,
        string marker,
        int expectedCount,
        TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        while (run.MarkerCount(marker) < expectedCount && elapsed.Elapsed < timeout)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return run.MarkerCount(marker) >= expectedCount;
    }
    private static int EdgeSwitches()
    {
        ObservedSettingsStore? observed = null;
        var run = new LifecycleRun(directory =>
            observed = new ObservedSettingsStore(new JsonSettingsStore(directory), expectedSaves: 10));
        run.Run((bar, _) =>
        {
            for (var index = 0; index < 10; index++)
            {
                var edge = index % 2 == 0 ? BarEdge.Bottom : BarEdge.Top;
                run.Check(bar.SelectEdge(edge), $"edge switch {index + 1} to {edge} failed");
            }

            new Thread(() =>
            {
                var save = observed!.SaveCompleted.GetAwaiter().GetResult();
                bar.Dispatcher.BeginInvoke(() =>
                {
                    run.Check(save.Succeeded, $"final edge save failed with {save.Issue}");
                    run.Check(bar.AppliedEdge == BarEdge.Top, $"final live edge {bar.AppliedEdge}, expected Top");
                    run.Check(File.ReadAllText(Path.Combine(run.SettingsDirectory, "settings.json")) == "{\"edge\":\"Top\"}", "final persisted edge is not Top");
                    run.CheckPositionedBar(bar);
                    run.Check(DesktopProbe.RequestClose(bar.Handle), "WM_CLOSE was not processed after edge switches");
                });
            })
            {
                IsBackground = true,
                Name = "edge-switch-observer",
            }.Start();
        });

        run.CheckOrder("bar-positioned", "edge-applied:Bottom", "edge-applied:Top", "settings-saved:Top", "remove", "restored");
        return run.Finish();
    }

    private static int SettingsFailure(SettingsFault fault)
    {
        ObservedSettingsStore? observed = null;
        var run = new LifecycleRun(directory =>
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(
                path,
                fault == SettingsFault.Backup ? "{ malformed" : "{\"edge\":\"Top\"}");
            var production = new JsonSettingsStore(directory, TimeProvider.System, new FaultingSettingsFileOperations(fault));
            return observed = new ObservedSettingsStore(production);
        });

        run.Run((bar, _) =>
        {
            run.Check(bar.AppliedEdge == BarEdge.Top, $"startup edge {bar.AppliedEdge}, expected Top");
            run.CheckPositionedBar(bar);
            run.Check(bar.SelectEdge(BarEdge.Bottom), "Bottom selection failed");

            new Thread(() =>
            {
                var save = observed!.SaveCompleted.GetAwaiter().GetResult();
                bar.Dispatcher.BeginInvoke(() =>
                {
                    run.Check(bar.AppliedEdge == BarEdge.Bottom, $"live edge {bar.AppliedEdge}, expected Bottom");
                    run.CheckPositionedBar(bar);
                    var saved = File.ReadAllText(Path.Combine(run.SettingsDirectory, "settings.json"));
                    if (fault == SettingsFault.Write)
                    {
                        run.Check(!save.Succeeded, "the injected write unexpectedly succeeded");
                        run.Check(saved == "{\"edge\":\"Top\"}", $"write failure changed the previous settings to {saved}");
                    }
                    else
                    {
                        run.Check(save.Succeeded, $"saving after the {fault} fault failed with {save.Issue}");
                        run.Check(saved == "{\"edge\":\"Bottom\"}", $"the controllable bar did not persist Bottom: {saved}");
                    }

                    run.Check(DesktopProbe.RequestClose(bar.Handle), "WM_CLOSE was not processed after the settings fault");
                });
            })
            {
                IsBackground = true,
                Name = $"settings-{fault}-observer",
            }.Start();
        });

        var expectedLoad = fault switch
        {
            SettingsFault.Read => "settings-load-issue:ReadFailed",
            SettingsFault.Backup => "settings-load-issue:BackupFailed",
            SettingsFault.Write => "settings-loaded:Top",
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
        run.CheckOrder("guard-acquired", "settings-start", expectedLoad, "bar-positioned", "edge-applied:Bottom");
        if (fault == SettingsFault.Write)
        {
            run.CheckOrder("edge-applied:Bottom", "settings-save-failed:WriteFailed", "settings-save-restart-only", "remove");
        }
        else
        {
            run.CheckOrder("edge-applied:Bottom", "settings-saved:Bottom", "remove");
        }

        return run.Finish();
    }

    /// <summary>
    /// Shutdown starts while settings load. Tracked work keeps the dispatcher alive through the bounded wait, so a
    /// load completing inside that wait must not create, register, or position the bar after shutdown began.
    /// </summary>
    private static int ShutdownDuringSettingsLoad()
    {
        LifecycleRun? run = null;
        run = new LifecycleRun(_ => new ShutdownDuringLoadStore(() => run!));
        var positioned = false;
        run.Run((_, _) => positioned = true);

        run.Check(!positioned, "the bar was created and positioned after shutdown began");
        run.Check(run.ExitCode == ExitCodes.Success, $"coordinator exit code {run.ExitCode}, expected {ExitCodes.Success}");
        run.CheckOrder("guard-acquired", "settings-start", "shutdown-requested", "wait-start", "settings-load-completed", "startup-skipped", "exit");
        return run.Finish();
    }

    private sealed class ShutdownDuringLoadStore(Func<LifecycleRun> run) : ISettingsStore
    {
        private static readonly TaskCompletionSource HeldOpen = new();

        public async Task<SettingsLoadResult> LoadAsync(CancellationToken cancellationToken = default)
        {
            // Called on the dispatcher thread before startup's first await.
            var lifecycle = run();
            _ = System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(() =>
            {
                lifecycle.Shutdown!.Track("held-open", HeldOpen.Task);
                lifecycle.Record("shutdown-requested");
                _ = lifecycle.Shutdown.ShutdownAsync(ExitCodes.Success);
            });

            await Task.Delay(TimeSpan.FromMilliseconds(500), CancellationToken.None).ConfigureAwait(false);
            System.Windows.Application.Current.Dispatcher.Invoke(() => lifecycle.Record("settings-load-completed"));
            return new SettingsLoadResult(BarSettings.Default);
        }

        public Task<SettingsSaveResult> SaveAsync(BarSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SettingsSaveResult());
    }

    /// <summary>
    /// Holds the bar until a line or end of input arrives on stdin, then closes it with <c>WM_CLOSE</c>.
    /// A second process exits as a non-owner. Returns the coordinator's exit code.
    /// </summary>
    private static int HoldBar()
    {
        var run = new LifecycleRun();
        run.Run((bar, _) =>
        {
            run.Record("ready");
            new Thread(() =>
            {
                Console.In.ReadLine();
                bar.Dispatcher.BeginInvoke(() => DesktopProbe.RequestClose(bar.Handle));
            }) { IsBackground = true }.Start();
        });

        return run.ExitCode ?? AssertionFailed;
    }

    private sealed class InjectedStartupFailureException() : Exception("Injected failure after ABM_SETPOS.");

    private sealed class InjectedCollectorFaultException() : Exception("Injected fault in a metric monitor Changed handler.");
}
