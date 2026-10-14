using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.Lifecycle;

/// <summary>
/// Startup order: single-instance guard, then settings, then the bar. A process that does not own the guard
/// exits before the settings step. Any failure after the guard is a handled startup failure that enters
/// <see cref="ShutdownCoordinator"/>. Run and dispose it on the UI thread; disposal releases the guard.
/// </summary>
/// <param name="record">Step observer; only the test host supplies one.</param>
/// <param name="afterBarPositioned">Runs after the bar is positioned; only the test host supplies one.</param>
public sealed class DesktopStartup(
    string guardName,
    ShutdownCoordinator shutdown,
    Action<string>? record = null,
    Action<BarWindow>? afterBarPositioned = null) : IDisposable
{
    /// <summary>Per-session name: AppBar space and the work area belong to one session.</summary>
    public const string ProductionGuardName = @"Local\SystemResourceMonitor-1178D3BA-F66E-486A-8896-F6EBAF20FF05";

    private SingleInstanceGuard? _guard;

    public void Run()
    {
        _guard = SingleInstanceGuard.TryAcquire(guardName);
        if (_guard is null)
        {
            record?.Invoke("non-owner");
            _ = shutdown.ShutdownAsync(ExitCodes.Success);
            return;
        }

        record?.Invoke("guard-acquired");
        try
        {
            // B03 loads settings here.
            record?.Invoke("settings-start");

            var bar = new BarWindow(() => shutdown.ShutdownAsync(ExitCodes.Success));
            shutdown.Attach(bar);
            bar.Start();
            record?.Invoke("bar-positioned");
            afterBarPositioned?.Invoke(bar);
        }
        catch (Exception exception)
        {
            record?.Invoke($"startup-failed:{exception.GetType().Name}");
            _ = shutdown.ShutdownAsync(ExitCodes.Failure);
        }
    }

    public void Dispose() => _guard?.Dispose();
}
