using static SystemResourceMonitor.Desktop.Bar.NativeMethods;

using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.Bar;

[Flags]
internal enum ReconciliationReason
{
    None = 0,
    Placement = 1,
    ExplorerRestart = 2,
}

internal static class ShellMessageClassifier
{
    public static ReconciliationReason Classify(int message, nint wParam, uint appBarCallback, uint taskbarCreated)
    {
        if ((uint)message == taskbarCreated)
        {
            return ReconciliationReason.ExplorerRestart;
        }

        if ((uint)message == appBarCallback)
        {
            return (uint)wParam switch
            {
                ABN_STATECHANGE or ABN_POSCHANGED => ReconciliationReason.Placement,
                ABN_FULLSCREENAPP => ReconciliationReason.None,
                _ => ReconciliationReason.None,
            };
        }

        return message is WM_DISPLAYCHANGE or WM_DPICHANGED
            ? ReconciliationReason.Placement
            : ReconciliationReason.None;
    }
}


/// <summary>Coalesces shell/display bursts without recursively negotiating from AppBar callbacks.</summary>
internal sealed class ReconciliationCoordinator(Action<Action> schedule, Action<ReconciliationReason> reconcile)
{
    private ReconciliationReason _pending;
    private bool _scheduled;
    private bool _running;

    public void Request(ReconciliationReason reason)
    {
        if (reason == ReconciliationReason.None)
        {
            return;
        }

        _pending |= reason;
        if (_scheduled || _running)
        {
            return;
        }

        _scheduled = true;
        schedule(Drain);
    }

    private void Drain()
    {
        _scheduled = false;
        _running = true;
        try
        {
            RunOne();
            if (_pending != ReconciliationReason.None)
            {
                RunOne();
            }
        }
        finally
        {
            _running = false;
            if (_pending != ReconciliationReason.None && !_scheduled)
            {
                _scheduled = true;
                schedule(Drain);
            }
        }
    }

    private void RunOne()
    {
        var reason = _pending;
        _pending = ReconciliationReason.None;
        reconcile(reason);
    }
}

internal readonly record struct ShellRecoveryResult(bool AppBarRecovered, bool TrayRecovered)
{
    public bool Succeeded => AppBarRecovered && TrayRecovered;
}

internal static class ShellRecovery
{
    /// <summary>Restores Explorer-owned resources in AppBar-then-tray order and exposes partial failure.</summary>
    public static ShellRecoveryResult Recover(AppBarSession appBar, ITrayIcon tray, BarEdge edge)
    {
        var appBarRecovered = false;
        try
        {
            appBar.RegisterAfterShellRestart();
            appBar.Place(edge);
            appBarRecovered = true;
        }
        catch (Exception)
        {
            // Tray recovery remains independently useful and observable.
        }

        var trayRecovered = false;
        try
        {
            tray.Recreate();
            trayRecovered = true;
        }
        catch (Exception)
        {
            // The combined result reports the partial failure to the lifecycle owner.
        }

        return new ShellRecoveryResult(appBarRecovered, trayRecovered);
    }
}
