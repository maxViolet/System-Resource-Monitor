using System.IO;

using System.Windows;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Adapters.Metrics;
using SystemResourceMonitor.Desktop.Adapters.Settings;
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
    private GpuLoadSource? _gpu;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var shutdown = new ShutdownCoordinator(_lifetime, TimeProvider.System, Shutdown);
        SessionEnding += (_, _) => shutdown.ShutdownAsync(ExitCodes.Success);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            shutdown.ShutdownAsync(ExitCodes.Failure);
        };

        try
        {
            var settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SystemResourceMonitor");
            var settings = new JsonSettingsStore(settingsDirectory);
            _gpu = new GpuLoadSource(new DxCoreAdapterApi(), new PdhGpuCounterApi());
            var monitor = new MetricMonitor(new SystemTimesCpuSource(), new GlobalMemoryRamSource(), _gpu, TimeProvider.System);
            _startup = new DesktopStartup(DesktopStartup.ProductionGuardName, shutdown, settings, Dispatcher, monitor,
                afterMetricRun: _gpu.Dispose);
            await _startup.RunAsync();
        }
        catch (Exception)
        {
            await shutdown.ShutdownAsync(ExitCodes.Failure);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _startup?.Dispose();
        base.OnExit(e);
    }
}
