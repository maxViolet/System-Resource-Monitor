using System.ComponentModel;
using System.Runtime.InteropServices;
using SystemResourceMonitor.Desktop.Settings;
using static SystemResourceMonitor.Desktop.Bar.NativeMethods;

namespace SystemResourceMonitor.Desktop.Bar;

internal interface IAppBarNativeApi
{
    void Register(nint hwnd, uint callbackMessage);

    void Remove(nint hwnd);

    uint GetWindowDpi(nint hwnd);

    PixelRect GetPrimaryMonitorBounds();

    void QueryPosition(nint hwnd, BarEdge edge, ref PixelRect bounds);

    void SetPosition(nint hwnd, BarEdge edge, ref PixelRect bounds);

    void SetWindowPosition(nint hwnd, PixelRect bounds);
}

/// <summary>One AppBar registration and all placement transactions performed through it.</summary>
internal sealed class AppBarSession(nint hwnd, uint callbackMessage, IAppBarNativeApi native)
{
    private bool _registered;
    private bool _registrationLost;
    private BarEdge? _appliedEdge;

    public BarEdge? AppliedEdge => _appliedEdge;

    public int ThicknessPixels { get; private set; }

    public PixelRect ReservedBounds { get; private set; }

    public void Register()
    {
        if (_registered)
        {
            return;
        }

        native.Register(hwnd, callbackMessage);
        _registered = true;
    }

    /// <summary>
    /// Re-establishes the registration after <c>TaskbarCreated</c>, sending <c>ABM_REMOVE</c> only when Explorer
    /// rejects <c>ABM_NEW</c>. A second rejection marks the registration lost until a later attempt succeeds.
    /// </summary>
    public void RegisterAfterShellRestart()
    {
        try
        {
            try
            {
                native.Register(hwnd, callbackMessage);
            }
            catch (Exception)
            {
                // ABM_NEW is rejected for a window Explorer still has registered (a spurious TaskbarCreated).
                // Placement cannot tell that apart from a lost registration: ABM_QUERYPOS/ABM_SETPOS return TRUE for
                // unregistered windows without reserving anything. Start over; a second rejection is a genuine failure.
                native.Remove(hwnd);
                native.Register(hwnd, callbackMessage);
            }
        }
        catch (Exception)
        {
            _registrationLost = true;
            throw;
        }

        _registrationLost = false;

        // Shutdown keeps ABM_REMOVE responsibility in every case; Windows returns TRUE even for unregistered windows.
        _registered = true;
    }

    public void Place(BarEdge edge)
    {
        if (!_registered)
        {
            throw new InvalidOperationException("The AppBar is not registered.");
        }

        if (_registrationLost)
        {
            // Unregistered placement would "succeed" without reserving anything; retry registration or fail visibly.
            RegisterAfterShellRestart();
        }

        var previous = _appliedEdge;
        try
        {
            var placement = PlaceCore(edge);
            _appliedEdge = edge;
            ThicknessPixels = placement.Thickness;
            ReservedBounds = placement.Bounds;
        }
        catch
        {
            if (previous is { } previousEdge)
            {
                try
                {
                    var rollback = PlaceCore(previousEdge);
                    ThicknessPixels = rollback.Thickness;
                    ReservedBounds = rollback.Bounds;
                }
                catch
                {
                    // The original placement error remains authoritative; recovery is reported by its caller.
                }
            }

            throw;
        }
    }

    public void Remove()
    {
        if (!_registered)
        {
            return;
        }

        native.Remove(hwnd);
        _registered = false;
    }

    private (int Thickness, PixelRect Bounds) PlaceCore(BarEdge edge)
    {
        var dpi = native.GetWindowDpi(hwnd);
        var thickness = AppBarPlacement.PhysicalThickness(BarWindow.ThicknessDips, dpi);
        var monitor = native.GetPrimaryMonitorBounds();
        if (monitor.Width <= 0 || monitor.Height <= 0)
        {
            throw new InvalidOperationException($"The primary monitor has invalid bounds {monitor}.");
        }

        var bounds = monitor;
        native.QueryPosition(hwnd, edge, ref bounds);
        bounds = AppBarPlacement.ApplyThickness(bounds, edge, thickness);
        native.SetPosition(hwnd, edge, ref bounds);
        AppBarPlacement.Validate(bounds, monitor, thickness);
        native.SetWindowPosition(hwnd, bounds);
        return (thickness, bounds);
    }
}

internal static class AppBarPlacement
{
    public static int PhysicalThickness(int dips, uint dpi)
    {
        if (dips <= 0 || dpi == 0)
        {
            throw new InvalidOperationException($"Cannot scale {dips} DIPs at {dpi} DPI.");
        }

        var pixels = checked((int)(((long)dips * dpi + UserDefaultDpi / 2) / UserDefaultDpi));
        return pixels > 0 ? pixels : throw new InvalidOperationException("The scaled AppBar thickness is empty.");
    }

    public static PixelRect ApplyThickness(PixelRect queried, BarEdge edge, int thickness) => edge switch
    {
        BarEdge.Top => queried with { Bottom = checked(queried.Top + thickness) },
        BarEdge.Bottom => queried with { Top = checked(queried.Bottom - thickness) },
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    public static void Validate(PixelRect bounds, PixelRect monitor, int thickness)
    {
        if (bounds.Width <= 0 || bounds.Height != thickness || bounds.Left < monitor.Left || bounds.Right > monitor.Right ||
            bounds.Top < monitor.Top || bounds.Bottom > monitor.Bottom)
        {
            throw new InvalidOperationException($"ABM_SETPOS returned invalid bounds {bounds} for monitor {monitor} and thickness {thickness}.");
        }
    }
}

internal sealed class Win32AppBarNativeApi : IAppBarNativeApi
{
    public void Register(nint hwnd, uint callbackMessage)
    {
        var data = NewData(hwnd);
        data.uCallbackMessage = callbackMessage;
        if (SHAppBarMessage(ABM_NEW, ref data) == 0)
        {
            throw new InvalidOperationException("ABM_NEW rejected the bar.");
        }
    }

    public void Remove(nint hwnd)
    {
        var data = NewData(hwnd);
        if (SHAppBarMessage(ABM_REMOVE, ref data) == 0)
        {
            throw new InvalidOperationException("ABM_REMOVE failed.");
        }
    }

    public uint GetWindowDpi(nint hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? throw new InvalidOperationException("GetDpiForWindow failed.") : dpi;
    }

    public PixelRect GetPrimaryMonitorBounds()
    {
        var monitor = MonitorFromPoint(new PixelPoint(0, 0), MONITOR_DEFAULTTOPRIMARY);
        if (monitor == 0)
        {
            throw new InvalidOperationException("MonitorFromPoint failed for the primary monitor.");
        }

        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetMonitorInfo failed for the primary monitor.");
        }

        return info.rcMonitor;
    }

    public void QueryPosition(nint hwnd, BarEdge edge, ref PixelRect bounds)
    {
        var data = NewData(hwnd);
        data.uEdge = EdgeValue(edge);
        data.rc = bounds;
        if (SHAppBarMessage(ABM_QUERYPOS, ref data) == 0)
        {
            throw new InvalidOperationException("ABM_QUERYPOS failed.");
        }

        bounds = data.rc;
    }

    public void SetPosition(nint hwnd, BarEdge edge, ref PixelRect bounds)
    {
        var data = NewData(hwnd);
        data.uEdge = EdgeValue(edge);
        data.rc = bounds;
        if (SHAppBarMessage(ABM_SETPOS, ref data) == 0)
        {
            throw new InvalidOperationException("ABM_SETPOS failed.");
        }

        bounds = data.rc;
    }

    public void SetWindowPosition(nint hwnd, PixelRect bounds)
    {
        if (!SetWindowPos(hwnd, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_NOACTIVATE))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowPos failed.");
        }
    }

    private static uint EdgeValue(BarEdge edge) => edge switch
    {
        BarEdge.Top => ABE_TOP,
        BarEdge.Bottom => ABE_BOTTOM,
        _ => throw new ArgumentOutOfRangeException(nameof(edge)),
    };

    private static APPBARDATA NewData(nint hwnd) => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = hwnd,
    };
}
