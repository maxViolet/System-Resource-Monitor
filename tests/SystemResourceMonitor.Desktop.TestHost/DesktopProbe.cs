using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SystemResourceMonitor.Desktop.Bar;

// Lets source-generated interop pass Desktop's PixelRect directly; every signature here is blittable.
[assembly: DisableRuntimeMarshalling]

namespace SystemResourceMonitor.Desktop.TestHost;

/// <summary>
/// Reads desktop state the way an outside observer sees it, for the test host and the Desktop tests.
/// </summary>
public static partial class DesktopProbe
{
    private const uint SPI_GETWORKAREA = 0x0030;
    private const int GWL_EXSTYLE = -20;
    private const uint WM_CLOSE = 0x0010;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const nint HWND_TOP = 0;

    public const nint WS_EX_TOPMOST = 0x00000008;
    public const nint WS_EX_TOOLWINDOW = 0x00000080;
    public const nint WS_EX_NOACTIVATE = 0x08000000;

    public static PixelRect WorkArea()
    {
        var rect = default(PixelRect);
        if (!SystemParametersInfo(SPI_GETWORKAREA, 0, ref rect, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SPI_GETWORKAREA failed.");
        }

        return rect;
    }

    public static PixelRect WindowRect(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetWindowRect failed.");
        }

        return rect;
    }

    public static nint ExtendedStyle(nint hwnd) => GetWindowLongPtr(hwnd, GWL_EXSTYLE);

    public static PixelRect MonitorBounds(nint hwnd)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The monitor bounds could not be read.");
        }

        return info.rcMonitor;
    }

    public static nint ForegroundWindow() => GetForegroundWindow();

    public static bool SetForegroundBounds(nint hwnd, PixelRect bounds)
    {
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var attached = foregroundThread != 0 && targetThread != 0 && foregroundThread != targetThread &&
            AttachThreadInput(foregroundThread, targetThread, true);
        try
        {
            if (!SetWindowPos(hwnd, HWND_TOP, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowPos failed for the controlled window.");
            }

            _ = SetForegroundWindow(hwnd);
            return GetForegroundWindow() == hwnd;
        }
        finally
        {
            if (attached)
            {
                _ = AttachThreadInput(foregroundThread, targetThread, false);
            }
        }
    }

    /// <summary>Visible top-level windows owned by the process.</summary>
    public static IReadOnlyList<nint> VisibleWindows(int processId)
    {
        var windows = new List<nint>();
        for (var hwnd = FindWindowEx(0, 0, null, null); hwnd != 0; hwnd = FindWindowEx(0, hwnd, null, null))
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner == processId && IsWindowVisible(hwnd))
            {
                windows.Add(hwnd);
            }
        }

        return windows;
    }

    /// <summary>Sends <c>WM_CLOSE</c>, the normal-exit request, and reports whether the window processed it.</summary>
    public static bool RequestClose(nint hwnd) =>
        SendMessageTimeout(hwnd, WM_CLOSE, 0, 0, SMTO_ABORTIFHUNG, 5_000, out _) != 0;

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint uiAction, uint uiParam, ref PixelRect pvParam, uint fWinIni);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out PixelRect lpRect);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindowEx(nint hWndParent, nint hWndChildAfter, string? lpszClass, string? lpszWindow);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public PixelRect rcMonitor;
        public PixelRect rcWork;
        public uint dwFlags;
    }

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static partial nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, nint lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);
}
