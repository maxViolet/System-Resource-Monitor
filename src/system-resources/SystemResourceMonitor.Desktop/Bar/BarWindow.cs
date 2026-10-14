using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SystemResourceMonitor.Desktop.Lifecycle;
using static SystemResourceMonitor.Desktop.Bar.NativeMethods;

namespace SystemResourceMonitor.Desktop.Bar;

/// <summary>
/// The empty, non-activating bar that reserves a Top strip of the primary monitor through the AppBar API.
/// All members run on the window's owning WPF thread.
/// </summary>
public sealed class BarWindow : Window, IShutdownBar
{
    public const int ThicknessDips = 24;
    private const string CallbackMessageName = "SystemResourceMonitor.AppBarCallback";

    private readonly Action _closeRequested;
    private HwndSourceHook? _hook;
    private nint _hwnd;
    private uint _callbackMessage;
    private bool _registered;
    private bool _finalClose;

    /// <param name="closeRequested">Posted when something other than shutdown closes the window.</param>
    public BarWindow(Action closeRequested)
    {
        _closeRequested = closeRequested;
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

    /// <summary>Bar thickness in physical pixels at the window's DPI, used for negotiation.</summary>
    public int ThicknessPixels { get; private set; }

    /// <summary>The rectangle returned by <c>ABM_SETPOS</c> and applied to the window.</summary>
    public PixelRect ReservedBounds { get; private set; }

    public nint Handle => _hwnd;

    /// <summary>
    /// Registers the AppBar, reserves the Top strip, and shows the window without activating it.
    /// Throws on any native failure; the caller's shutdown path removes a partial registration.
    /// </summary>
    public void Start()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        AddExtendedStyle(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        _callbackMessage = RegisterWindowMessage(CallbackMessageName);
        if (_callbackMessage == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "RegisterWindowMessage failed.");
        }

        _hook = OnWindowMessage;
        var source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("The bar has no HwndSource.");
        source.AddHook(_hook);

        var data = NewAppBarData();
        data.uCallbackMessage = _callbackMessage;
        if (SHAppBarMessage(ABM_NEW, ref data) == 0)
        {
            throw new InvalidOperationException("ABM_NEW rejected the bar.");
        }

        _registered = true;
        ReserveTopStrip();
        Show();
    }

    public void RemoveAppBar()
    {
        if (!_registered)
        {
            return;
        }

        var data = NewAppBarData();
        if (SHAppBarMessage(ABM_REMOVE, ref data) == 0)
        {
            throw new InvalidOperationException("ABM_REMOVE failed.");
        }

        _registered = false;
    }

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

    private void ReserveTopStrip()
    {
        var dpi = GetDpiForWindow(_hwnd);
        if (dpi == 0)
        {
            throw new InvalidOperationException("GetDpiForWindow failed.");
        }

        ThicknessPixels = MulDiv(ThicknessDips, (int)dpi, UserDefaultDpi);
        if (ThicknessPixels <= 0)
        {
            throw new InvalidOperationException($"MulDiv returned {ThicknessPixels} for {dpi} DPI.");
        }

        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromPoint(new PixelPoint(0, 0), MONITOR_DEFAULTTOPRIMARY), ref monitorInfo))
        {
            throw new InvalidOperationException("GetMonitorInfo failed for the primary monitor.");
        }

        var data = NewAppBarData();
        data.uEdge = ABE_TOP;
        data.rc = monitorInfo.rcMonitor;
        if (SHAppBarMessage(ABM_QUERYPOS, ref data) == 0)
        {
            throw new InvalidOperationException("ABM_QUERYPOS failed.");
        }

        // ABM_QUERYPOS adjusts the edges but does not preserve the requested size.
        data.rc = data.rc with { Bottom = data.rc.Top + ThicknessPixels };
        if (SHAppBarMessage(ABM_SETPOS, ref data) == 0)
        {
            throw new InvalidOperationException("ABM_SETPOS failed.");
        }

        var bounds = data.rc;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException($"ABM_SETPOS returned an empty rectangle {bounds}.");
        }

        if (!SetWindowPos(_hwnd, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_NOACTIVATE))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowPos failed.");
        }

        ReservedBounds = bounds;
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
        // WS_EX_NOACTIVATE alone still let a click raise foreground and focus events for the bar.
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return MA_NOACTIVATE;
        }

        if ((uint)msg == _callbackMessage)
        {
            // B02 keeps its fixed strip for every notification; later bricks fill these handlers.
            switch ((uint)wParam)
            {
                case ABN_STATECHANGE:
                case ABN_POSCHANGED:
                    // B03 renegotiates the strip.
                    break;
                case ABN_FULLSCREENAPP:
                    // B04 reconciles fullscreen state.
                    break;
            }

            handled = true;
        }

        return 0;
    }

    private APPBARDATA NewAppBarData() =>
        new() { cbSize = (uint)Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
}
