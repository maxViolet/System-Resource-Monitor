using System.Runtime.InteropServices;
using static SystemResourceMonitor.Desktop.Bar.NativeMethods;

namespace SystemResourceMonitor.Desktop.Bar;

internal interface ITrayMenuNativeApi
{
    nint CreateOwner();

    bool DestroyOwner(nint hwnd);

    nint GetForeground();

    bool SetForeground(nint hwnd);

    uint Track(nint menu, POINT point, nint owner);

    bool PostNull(nint hwnd);
}

/// <summary>Owns the hidden foreground window required for correct notification-area menu dismissal.</summary>
internal sealed class TrayMenuOwner(ITrayMenuNativeApi native)
{
    private nint _hwnd;

    public void Start()
    {
        if (_hwnd != 0)
        {
            return;
        }

        _hwnd = native.CreateOwner();
        if (_hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed for the tray menu owner with error {Marshal.GetLastPInvokeError()}.");
        }
    }

    /// <summary>
    /// Makes the hidden owner foreground, tracks the menu, posts the documented benign task-switch message, and
    /// returns foreground to the previous window only when the user did not activate another window meanwhile.
    /// </summary>
    public uint Show(nint menu, POINT point)
    {
        if (_hwnd == 0)
        {
            throw new InvalidOperationException("The tray menu owner has not started.");
        }

        var previousForeground = native.GetForeground();

        // Explorer grants foreground rights for a tray click. If the lock still refuses, the menu remains usable;
        // only outside-click dismissal degrades, which is preferable to losing Position and Exit.
        _ = native.SetForeground(_hwnd);

        var command = native.Track(menu, point, _hwnd);
        if (!native.PostNull(_hwnd))
        {
            throw new InvalidOperationException($"PostMessage failed for the tray menu owner with error {Marshal.GetLastPInvokeError()}.");
        }

        if (previousForeground != 0 && previousForeground != _hwnd && native.GetForeground() == _hwnd)
        {
            // Foreground-lock rules may refuse this; the visible bar is never activated either way.
            _ = native.SetForeground(previousForeground);
        }

        return command;
    }

    public void Remove()
    {
        if (_hwnd == 0)
        {
            return;
        }

        if (!native.DestroyOwner(_hwnd))
        {
            throw new InvalidOperationException($"DestroyWindow failed for the tray menu owner with error {Marshal.GetLastPInvokeError()}.");
        }

        _hwnd = 0;
    }
}

internal sealed class Win32TrayMenuNativeApi : ITrayMenuNativeApi
{
    // Never shown. WS_EX_NOACTIVATE is deliberately absent: the menu owner must be able to become foreground.
    public nint CreateOwner() => CreateWindowEx(
        unchecked((uint)(nuint)WS_EX_TOOLWINDOW),
        "STATIC",
        "System Resource Monitor tray menu owner",
        WS_POPUP,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);

    public bool DestroyOwner(nint hwnd) => DestroyWindow(hwnd);

    public nint GetForeground() => GetForegroundWindow();

    public bool SetForeground(nint hwnd) => SetForegroundWindow(hwnd);

    public uint Track(nint menu, POINT point, nint owner) =>
        TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY, point.X, point.Y, owner, 0);

    public bool PostNull(nint hwnd) => PostMessage(hwnd, WM_NULL, 0, 0);
}
