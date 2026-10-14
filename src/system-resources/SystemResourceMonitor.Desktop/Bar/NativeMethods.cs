using System.Runtime.InteropServices;

namespace SystemResourceMonitor.Desktop.Bar;

/// <summary>
/// Win32 calls used by the bar. Every struct passed by reference carries its own size.
/// </summary>
internal static partial class NativeMethods
{
    internal const uint ABM_NEW = 0x0;
    internal const uint ABM_REMOVE = 0x1;
    internal const uint ABM_QUERYPOS = 0x2;
    internal const uint ABM_SETPOS = 0x3;
    internal const uint ABE_TOP = 1;
    internal const uint ABN_STATECHANGE = 0x0;
    internal const uint ABN_POSCHANGED = 0x1;
    internal const uint ABN_FULLSCREENAPP = 0x2;

    internal const int WM_MOUSEACTIVATE = 0x0021;
    internal const nint MA_NOACTIVATE = 3;

    internal const int GWL_EXSTYLE = -20;
    internal const nint WS_EX_TOOLWINDOW = 0x00000080;
    internal const nint WS_EX_NOACTIVATE = 0x08000000;

    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;

    internal const uint MONITOR_DEFAULTTOPRIMARY = 0x1;
    internal const int UserDefaultDpi = 96;

    [StructLayout(LayoutKind.Sequential)]
    internal struct APPBARDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public PixelRect rc;
        public nint lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public uint cbSize;
        public PixelRect rcMonitor;
        public PixelRect rcWork;
        public uint dwFlags;
    }

    [LibraryImport("shell32.dll")]
    internal static partial nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial uint RegisterWindowMessage(string lpString);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromPoint(PixelPoint pt, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [LibraryImport("kernel32.dll")]
    internal static partial int MulDiv(int nNumber, int nNumerator, int nDenominator);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct PixelPoint(int X, int Y);
}
