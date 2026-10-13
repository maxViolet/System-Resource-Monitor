using System.Windows;

namespace SystemResourceMonitor.Desktop;

/// <summary>
/// Composition root. Production components are created and wired here only.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // B01 composes nothing yet: the bar, tray, settings, and collectors arrive in later bricks.
        Shutdown(0);
    }
}
