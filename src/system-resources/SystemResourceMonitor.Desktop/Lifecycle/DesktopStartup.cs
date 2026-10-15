using System.Windows.Threading;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Bar;
using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.Lifecycle;

/// <summary>
/// Startup order: acquire the per-session guard synchronously, load settings off the WPF thread, create the bar and
/// tray on the owning dispatcher, then start metric monitoring until <paramref name="lifetime"/> is cancelled. Any
/// owner failure enters the shared shutdown coordinator.
/// </summary>
public sealed class DesktopStartup(
    string guardName,
    ShutdownCoordinator shutdown,
    ISettingsStore settingsStore,
    Dispatcher dispatcher,
    MetricMonitor monitor,
    CancellationToken lifetime,
    Action<string>? record = null,
    Action<BarWindow>? afterBarPositioned = null) : IDisposable
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
        afterBarPositioned?.Invoke(bar);
    }

    /// <summary>
    /// Starts collectors after the guard, settings, and AppBar. Every state reaches the bar through the dispatcher and
    /// <see cref="ShutdownCoordinator.TryPublish"/>, so nothing is shown once shutdown begins. A faulted monitor run
    /// enters failure shutdown, because stale readings must not stay on the bar as fresh.
    /// </summary>
    private void StartMetrics(BarWindow bar)
    {
        var relay = new MetricRelay(
            action => bar.Dispatcher.BeginInvoke(action),
            state => shutdown.TryPublish(() => bar.ShowMetrics(state)));
        monitor.Changed += (_, state) => relay.OnChanged(state);
        bar.ShowMetrics(monitor.Current);
        var run = monitor.RunAsync(lifetime);
        shutdown.Track("metric-monitor", run);
        MetricRunFailure.Observe(
            run,
            action => bar.Dispatcher.BeginInvoke(action),
            exception =>
            {
                record?.Invoke($"metrics-failed:{exception.GetType().Name}");
                _ = shutdown.ShutdownAsync(ExitCodes.Failure);
            });
        record?.Invoke("metrics-started");
    }

    private static string LoadIssueMessage(SettingsLoadIssue issue) => issue switch
    {
        SettingsLoadIssue.ReadFailed => "Settings could not be read. Top is in use.",
        SettingsLoadIssue.Malformed => "Invalid settings were preserved. Top is in use.",
        SettingsLoadIssue.BackupFailed => "Invalid settings could not be preserved. Top is in use.",
        _ => throw new ArgumentOutOfRangeException(nameof(issue)),
    };
}
