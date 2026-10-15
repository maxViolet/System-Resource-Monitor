using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using SystemResourceMonitor.Desktop.Lifecycle;
using SystemResourceMonitor.Desktop.Settings;
using static SystemResourceMonitor.Desktop.Bar.NativeMethods;

namespace SystemResourceMonitor.Desktop.Bar;

/// <summary>
/// The empty, non-activating AppBar. All HWND, tray, placement, and reconciliation members run on its WPF thread.
/// </summary>
public sealed class BarWindow : Window, IShutdownBar
{
    public const int ThicknessDips = 24;
    private const string CallbackMessageName = "SystemResourceMonitor.AppBarCallback";
    private const string TaskbarCreatedMessageName = "TaskbarCreated";
    private static readonly TimeSpan FullscreenPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly BarEdge _initialEdge;
    private readonly Action<BarEdge> _edgeApplied;
    private readonly Action _closeRequested;
    private readonly Action _shellIntegrationFailed;
    private readonly Action<string>? _record;
    private HwndSourceHook? _hook;
    private AppBarSession? _appBar;
    private ITrayIcon? _tray;
    private ReconciliationCoordinator? _reconciliation;
    private BarReconciliationController? _barReconciliation;
    private FullscreenReconciler? _fullscreen;
    private DispatcherTimer? _fullscreenTimer;
    private nint _hwnd;
    private uint _callbackMessage;
    private uint _taskbarCreatedMessage;
    private bool _finalClose;
    private bool _shuttingDown;
    private MenuReconciliationDeferral? _menuDeferral;

    public BarWindow(
        BarEdge initialEdge,
        Action<BarEdge> edgeApplied,
        Action closeRequested,
        Action shellIntegrationFailed,
        Action<string>? record = null)
    {
        _initialEdge = initialEdge;
        _edgeApplied = edgeApplied;
        _closeRequested = closeRequested;
        _shellIntegrationFailed = shellIntegrationFailed;
        _record = record;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        AllowsTransparency = false;
        Topmost = true;
        Focusable = false;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

        // Create the handle on the primary monitor, which always contains (0, 0).
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
    }

    public int ThicknessPixels => _appBar?.ThicknessPixels ?? 0;

    public PixelRect ReservedBounds => _appBar?.ReservedBounds ?? default;

    public BarEdge AppliedEdge => _appBar?.AppliedEdge ?? _initialEdge;

    public nint Handle => _hwnd;

    /// <summary>Registers and positions the AppBar, then adds and versions the tray icon.</summary>
    public void Start()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        AddExtendedStyle(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        _callbackMessage = RegisterRequiredMessage(CallbackMessageName);
        _taskbarCreatedMessage = RegisterRequiredMessage(TaskbarCreatedMessageName);
        _hook = OnWindowMessage;
        var source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("The bar has no HwndSource.");
        source.AddHook(_hook);

        _appBar = new AppBarSession(_hwnd, _callbackMessage, new Win32AppBarNativeApi());
        _fullscreen = new FullscreenReconciler(
            _hwnd,
            new Win32FullscreenStateReader(),
            new Win32BarZOrderNativeApi());
        _reconciliation = new ReconciliationCoordinator(
            action => Dispatcher.BeginInvoke(action),
            Reconcile);
        _menuDeferral = new MenuReconciliationDeferral(() => _tray?.IsMenuOpen == true, _reconciliation.Request);
        _appBar.Register();
        _appBar.Place(_initialEdge);
        Show();

        _tray = new NativeTrayIcon(
            _hwnd,
            edge => SelectEdge(edge),
            _closeRequested,
            () => _menuDeferral!.MenuClosed(),
            new TrayMenuOwner(new Win32TrayMenuNativeApi()));
        _tray.SetAppliedEdge(_initialEdge);
        _tray.Start();
        _barReconciliation = new BarReconciliationController(
            _appBar,
            _tray,
            _fullscreen,
            _shellIntegrationFailed,
            ReportStatus,
            _record);
        _fullscreenTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = FullscreenPollInterval,
        };
        _fullscreenTimer.Tick += OnFullscreenTimerTick;
        _fullscreenTimer.Start();
        _reconciliation.Request(ReconciliationReason.Fullscreen);
    }

    /// <summary>Applies an edge transaction and persists only a successfully applied edge.</summary>
    public bool SelectEdge(BarEdge edge)
    {
        Dispatcher.VerifyAccess();
        if (_appBar is null || _tray is null)
        {
            throw new InvalidOperationException("The bar has not started.");
        }

        if (_shuttingDown)
        {
            // A menu command can return after shutdown ran inside the menu's modal loop.
            return false;
        }

        if (_appBar.AppliedEdge == edge && _barReconciliation!.IsAppBarReady)
        {
            return true;
        }

        try
        {
            _appBar.Place(edge);
            if (_barReconciliation!.PlacementSucceeded())
            {
                _reconciliation!.Request(ReconciliationReason.Fullscreen);
            }
        }
        catch (Exception exception)
        {
            _record?.Invoke($"placement-failed:{exception.GetType().Name}");
            ReportStatus("The bar could not change position.");
            return false;
        }

        try
        {
            _tray.SetAppliedEdge(edge);
        }
        catch (Exception exception)
        {
            _record?.Invoke($"tray-check-failed:{exception.GetType().Name}");
            ReportStatus("The tray could not update its position check.");
        }

        _record?.Invoke($"edge-applied:{edge}");
        _edgeApplied(edge);
        return true;
    }

    public void ReportStatus(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => ReportStatus(message));
            return;
        }

        try
        {
            if (_tray?.ShowStatus(message) != true)
            {
                _record?.Invoke("status-unavailable");
            }
        }
        catch (Exception exception)
        {
            _record?.Invoke($"status-failed:{exception.GetType().Name}");
        }
    }

    public void RemoveAppBar()
    {
        _shuttingDown = true;
        _fullscreenTimer?.Stop();
        _appBar?.Remove();
    }

    public void RemoveTrayIcon() => _tray?.Remove();

    public void CloseFinal()
    {
        _finalClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_finalClose)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(_closeRequested);
        }

        base.OnClosing(e);
    }

    private void Reconcile(ReconciliationReason reason)
    {
        if (_shuttingDown || _barReconciliation is null)
        {
            return;
        }

        if (_menuDeferral!.TryDefer(reason))
        {
            return;
        }

        _barReconciliation.Reconcile(reason, AppliedEdge);
    }

    private void OnFullscreenTimerTick(object? sender, EventArgs e)
    {
        if (!_shuttingDown)
        {
            _reconciliation?.Request(ReconciliationReason.Fullscreen);
        }
    }

    private void AddExtendedStyle(nint flags)
    {
        // SetLastError stubs clear the error before each call, so zero with no error is a valid style.
        var style = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        if (style == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetWindowLongPtr failed.");
        }

        if (SetWindowLongPtr(_hwnd, GWL_EXSTYLE, style | flags) == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowLongPtr failed.");
        }
    }

    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return MA_NOACTIVATE;
        }

        try
        {
            if (_tray?.HandleWindowMessage(msg, wParam, lParam) == true)
            {
                handled = true;
                return 0;
            }
        }
        catch (Exception exception)
        {
            _record?.Invoke($"tray-action-failed:{exception.GetType().Name}");
            ReportStatus("The tray action failed.");
            handled = true;
            return 0;
        }

        var reason = _shuttingDown
            ? ReconciliationReason.None
            : ShellMessageClassifier.Classify(msg, wParam, _callbackMessage, _taskbarCreatedMessage);
        _reconciliation?.Request(reason);
        if ((uint)msg == _taskbarCreatedMessage || (uint)msg == _callbackMessage)
        {
            handled = true;
        }

        return 0;
    }

    private static uint RegisterRequiredMessage(string name)
    {
        var message = RegisterWindowMessage(name);
        return message == 0
            ? throw new Win32Exception(Marshal.GetLastPInvokeError(), $"RegisterWindowMessage failed for {name}.")
            : message;
    }
}
