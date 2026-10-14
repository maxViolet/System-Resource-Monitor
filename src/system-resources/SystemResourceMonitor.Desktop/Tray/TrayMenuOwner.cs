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

    /// <summary>True while <see cref="Show"/> is inside the menu's modal loop, which also pumps dispatcher work.</summary>
    public bool IsTracking { get; private set; }

    /// <summary>
    /// Makes the hidden owner foreground, tracks the menu, posts the documented benign task-switch message, and
    /// returns foreground to the window that was foreground when the menu opened (the taskbar after a mouse click)
    /// only when no other window was activated meanwhile.
    /// </summary>
    public uint Show(nint menu, POINT point)
    {
        // Work pumped by the modal loop may remove this owner; use the handle captured before tracking.
        var owner = _hwnd;
        if (owner == 0)
        {
            throw new InvalidOperationException("The tray menu owner has not started.");
        }

        var previousForeground = native.GetForeground();

        // Explorer grants foreground rights for a tray click. If the lock still refuses, the menu remains usable;
        // only outside-click dismissal degrades, which is preferable to losing Position and Exit.
        _ = native.SetForeground(owner);

        uint command;
        IsTracking = true;
        try
        {
            command = native.Track(menu, point, owner);
        }
        finally
        {
            IsTracking = false;
        }

        // Best-effort: failing to force the documented task switch must not discard the user's chosen command.
        _ = native.PostNull(owner);

        if (previousForeground != 0 && previousForeground != owner && native.GetForeground() == owner)
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
