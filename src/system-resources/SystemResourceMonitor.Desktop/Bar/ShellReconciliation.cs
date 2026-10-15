using static SystemResourceMonitor.Desktop.Bar.NativeMethods;

using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.Bar;

[Flags]
internal enum ReconciliationReason
{
    None = 0,
    Placement = 1,
    ExplorerRestart = 2,
    Fullscreen = 4,
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
                ABN_FULLSCREENAPP => ReconciliationReason.Fullscreen,
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

/// <summary>
/// Holds reconciliation reasons while the tray menu's modal loop runs (it pumps dispatcher work, and recovery would
/// destroy the menu being tracked), then re-requests them once the menu closes.
/// </summary>
internal sealed class MenuReconciliationDeferral(Func<bool> isMenuOpen, Action<ReconciliationReason> request)
{
    private ReconciliationReason _deferred;

    public bool TryDefer(ReconciliationReason reason)
    {
        if (!isMenuOpen())
        {
            return false;
        }

        _deferred |= reason;
        return true;
    }

    public void MenuClosed()
    {
        var deferred = _deferred;
        _deferred = ReconciliationReason.None;
        request(deferred);
    }
}

internal enum ShellRecoveryAction
{
    None,
    ReportAppBarFailure,
    FailureShutdown,
}

internal readonly record struct ShellRecoveryResult(bool AppBarRecovered, bool TrayRecovered)
{
    public bool Succeeded => AppBarRecovered && TrayRecovered;

    /// <summary>
    /// Without a tray icon there is no Position or Exit, so a reservation the user cannot release is worse than
    /// exiting (ADR 0009). An AppBar failure leaves the tray usable and is reported through it.
    /// </summary>
    public ShellRecoveryAction NextAction => !TrayRecovered
        ? ShellRecoveryAction.FailureShutdown
        : AppBarRecovered ? ShellRecoveryAction.None : ShellRecoveryAction.ReportAppBarFailure;
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

/// <summary>
/// Owns AppBar recovery readiness and the required placement/recovery-before-z-order ordering.
/// The caller serializes passes through <see cref="ReconciliationCoordinator"/>.
/// </summary>
internal sealed class BarReconciliationController(
    AppBarSession appBar,
    ITrayIcon tray,
    FullscreenReconciler fullscreen,
    Action shellIntegrationFailed,
    Action<string> reportStatus,
    Action<string>? record)
{
    private bool _appBarReady = true;
    private bool _zOrderFailureReported;

    public bool IsAppBarReady => _appBarReady;

    /// <summary>Records a successful edge-placement transaction and reports whether it restored lost registration.</summary>
    public bool PlacementSucceeded()
    {
        var recoveredRegistration = !_appBarReady;
        _appBarReady = true;
        if (recoveredRegistration)
        {
            fullscreen.InvalidateZOrder();
        }

        return recoveredRegistration;
    }

    public void Reconcile(ReconciliationReason reason, BarEdge appliedEdge)
    {
        if ((reason & ReconciliationReason.ExplorerRestart) != 0)
        {
            var result = ShellRecovery.Recover(appBar, tray, appliedEdge);
            record?.Invoke($"recovery-appbar:{(result.AppBarRecovered ? "success" : "failure")}");
            record?.Invoke($"recovery-tray:{(result.TrayRecovered ? "success" : "failure")}");
            _appBarReady = result.AppBarRecovered;
            switch (result.NextAction)
            {
                case ShellRecoveryAction.FailureShutdown:
                    shellIntegrationFailed();
                    return;
                case ShellRecoveryAction.ReportAppBarFailure:
                    reportStatus("Shell recovery failed.");
                    return;
            }

            fullscreen.InvalidateZOrder();
        }
        else if ((reason & ReconciliationReason.Placement) != 0)
        {
            try
            {
                appBar.Place(appliedEdge);
                _ = PlacementSucceeded();
                record?.Invoke("reconciled");
            }
            catch (Exception exception)
            {
                record?.Invoke($"reconciliation-failed:{exception.GetType().Name}");
                reportStatus("The bar could not reconcile its position.");
                return;
            }
        }

        if (_appBarReady)
        {
            ReconcileFullscreen();
        }
    }

    private void ReconcileFullscreen()
    {
        try
        {
            var wasKnown = fullscreen.IsZOrderKnown;
            var previous = fullscreen.AppliedState;
            var observation = fullscreen.Reconcile();
            if (observation == FullscreenObservation.Indeterminate)
            {
                record?.Invoke("fullscreen-indeterminate");
                return;
            }

            _zOrderFailureReported = false;
            if (!wasKnown || previous != fullscreen.AppliedState)
            {
                record?.Invoke($"zorder:{fullscreen.AppliedState}");
            }
        }
        catch (Exception exception)
        {
            record?.Invoke($"zorder-failed:{exception.GetType().Name}");
            if (!_zOrderFailureReported)
            {
                _zOrderFailureReported = true;
                reportStatus("The bar could not reconcile fullscreen.");
            }
        }
    }
}
