using System.Windows.Threading;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Bar;
using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.Lifecycle;

/// <summary>
/// Startup order: acquire the per-session guard synchronously, load settings off the WPF thread, create the bar and
/// tray on the owning dispatcher, then start metric monitoring and, when composed, Codex limit monitoring until
/// shutdown cancels their lifetime. Any owner failure enters the shared shutdown coordinator.
/// </summary>
public sealed class DesktopStartup(
    string guardName,
    ShutdownCoordinator shutdown,
    ISettingsStore settingsStore,
    Dispatcher dispatcher,
    MetricMonitor monitor,
    Action<string>? record = null,
    Action<BarWindow>? afterBarPositioned = null,
    Action? afterMetricRun = null,
    CodexLimitMonitor? codex = null,
    ClaudeLimitMonitor? claude = null) : IDisposable
{
    /// <summary>Per-session name: AppBar space and the work area belong to one session.</summary>
    public const string ProductionGuardName = @"Local\SystemResourceMonitor-1178D3BA-F66E-486A-8896-F6EBAF20FF05";

    private SingleInstanceGuard? _guard;

    public async Task RunAsync()
    {
        _guard = SingleInstanceGuard.TryAcquire(guardName);
        if (_guard is null)
        {
            record?.Invoke("non-owner");
            await shutdown.ShutdownAsync(ExitCodes.Success);
            return;
        }

        record?.Invoke("guard-acquired");
        try
        {
            record?.Invoke("settings-start");
            var loaded = await settingsStore.LoadAsync().ConfigureAwait(false);
            await dispatcher.InvokeAsync(() =>
            {
                if (!shutdown.TryPublish(() => CompleteStartup(loaded)))
                {
                    record?.Invoke("startup-skipped");
                }
            });
        }
        catch (Exception exception)
        {
            await dispatcher.InvokeAsync(() =>
            {
                record?.Invoke($"startup-failed:{exception.GetType().Name}");
                _ = shutdown.ShutdownAsync(ExitCodes.Failure);
            });
        }
    }

    public void Dispose() => _guard?.Dispose();

    private void CompleteStartup(SettingsLoadResult loaded)
    {
        record?.Invoke(loaded.Issue is null ? $"settings-loaded:{loaded.Settings.Edge}" : $"settings-load-issue:{loaded.Issue}");

        SettingsPersistenceQueue? persistence = null;
        var bar = new BarWindow(
            loaded.Settings.Edge,
            edge => persistence!.Enqueue(edge),
            () => shutdown.ShutdownAsync(ExitCodes.Success),
            () => shutdown.ShutdownAsync(ExitCodes.Failure),
            record);
        persistence = new SettingsPersistenceQueue(
            settingsStore,
            bar.ReportStatus,
            record,
            shutdown.Track,
            action => bar.Dispatcher.BeginInvoke(action));

        shutdown.Attach(bar);
        bar.Start();
        record?.Invoke("bar-positioned");
        if (loaded.Issue is { } issue)
        {
            bar.ReportStatus(LoadIssueMessage(issue));
        }

        StartMetrics(bar);
        StartCodex(bar);
        StartClaude(bar);
        afterBarPositioned?.Invoke(bar);
    }

    /// <summary>
    /// Starts collectors after the guard, settings, and AppBar. Every state reaches the bar through the dispatcher and
    /// <see cref="ShutdownCoordinator.TryPublish"/>, so nothing is shown once shutdown begins. A faulted monitor run
    /// enters failure shutdown, because stale readings must not stay on the bar as fresh.
    /// </summary>
    private void StartMetrics(BarWindow bar)
    {
        var relay = new MetricRelay<DashboardState>(
            action => bar.Dispatcher.BeginInvoke(action),
            state => shutdown.TryPublish(() => bar.ShowMetrics(state)));
        monitor.Changed += (_, state) => relay.OnChanged(state);
        bar.ShowMetrics(monitor.Current);
        var run = monitor.RunAsync(shutdown.Lifetime);
        shutdown.TrackOwner("metric-monitor", run);
        if (afterMetricRun is not null)
        {
            _ = run.ContinueWith(_ => afterMetricRun(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        record?.Invoke("metrics-started");
    }

    /// <summary>
    /// Starts Codex polling after system metrics, on its own monitor, relay, and owner task, so a Codex failure or a
    /// pending client never delays CPU, RAM, or GPU. Shutdown waits for the run, which ends only after the pending
    /// client tree has exited or reached its termination deadline.
    /// </summary>
    private void StartCodex(BarWindow bar)
    {
        if (codex is null)
        {
            return;
        }

        var relay = new MetricRelay<CodexLimitState>(
            action => bar.Dispatcher.BeginInvoke(action),
            state => shutdown.TryPublish(() => bar.ShowCodex(state)));
        codex.Changed += (_, state) => relay.OnChanged(state);
        bar.ShowCodex(codex.Current);
        shutdown.TrackOwner("codex-limits", codex.RunAsync(shutdown.Lifetime));
        record?.Invoke("codex-started");
    }

    private void StartClaude(BarWindow bar)
    {
        if (claude is null) return;
        var relay = new MetricRelay<ClaudeLimitState>(
            action => bar.Dispatcher.BeginInvoke(action),
            state => shutdown.TryPublish(() => bar.ShowClaude(state)));
        claude.Changed += (_, state) => relay.OnChanged(state);
        bar.ShowClaude(claude.Current);
        shutdown.TrackOwner("claude-limits", claude.RunAsync(shutdown.Lifetime));
        record?.Invoke("claude-started");
    }

    private static string LoadIssueMessage(SettingsLoadIssue issue) => issue switch
    {
        SettingsLoadIssue.ReadFailed => "Settings could not be read. Top is in use.",
        SettingsLoadIssue.Malformed => "Invalid settings were preserved. Top is in use.",
        SettingsLoadIssue.BackupFailed => "Invalid settings could not be preserved. Top is in use.",
        _ => throw new ArgumentOutOfRangeException(nameof(issue)),
    };
}
