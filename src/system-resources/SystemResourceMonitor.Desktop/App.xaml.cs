using System.Windows;
using SystemResourceMonitor.Desktop.Lifecycle;

namespace SystemResourceMonitor.Desktop;

/// <summary>
/// Composition root. Production components are created and wired here only.
/// </summary>
public partial class App : Application
{
    // Not disposed: work still pending at the shutdown deadline may hold its token.
    private readonly CancellationTokenSource _lifetime = new();
    private DesktopStartup? _startup;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var shutdown = new ShutdownCoordinator(_lifetime, TimeProvider.System, Shutdown);
        SessionEnding += (_, _) => shutdown.ShutdownAsync(ExitCodes.Success);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            shutdown.ShutdownAsync(ExitCodes.Failure);
        };

        _startup = new DesktopStartup(DesktopStartup.ProductionGuardName, shutdown);
        _startup.Run();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _startup?.Dispose();
        base.OnExit(e);
    }
}
