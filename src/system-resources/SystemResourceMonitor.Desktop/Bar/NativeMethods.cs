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
    internal const uint ABE_BOTTOM = 3;
    internal const uint ABE_TOP = 1;
    internal const uint ABN_STATECHANGE = 0x0;
    internal const uint ABN_POSCHANGED = 0x1;
    internal const uint ABN_FULLSCREENAPP = 0x2;

    internal const int WM_NULL = 0x0000;
    internal const int WM_MOUSEACTIVATE = 0x0021;
    internal const int WM_CONTEXTMENU = 0x007B;
    internal const int WM_DISPLAYCHANGE = 0x007E;
    internal const int WM_DPICHANGED = 0x02E0;
    internal const int WM_USER = 0x0400;
    internal const int WM_APP = 0x8000;
    internal const nint MA_NOACTIVATE = 3;
    internal const uint GA_ROOT = 2;

    internal const int GWL_EXSTYLE = -20;
    internal const nint WS_EX_TOOLWINDOW = 0x00000080;
    internal const nint WS_EX_NOACTIVATE = 0x08000000;
    internal const uint WS_POPUP = 0x80000000;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const nint HWND_TOPMOST = -1;
    internal const nint HWND_BOTTOM = 1;

    internal const uint MONITOR_DEFAULTTONULL = 0x0;
    internal const uint MONITOR_DEFAULTTOPRIMARY = 0x1;
    internal const int UserDefaultDpi = 96;

    internal const uint NIM_ADD = 0x00000000;
    internal const uint NIM_MODIFY = 0x00000001;
    internal const uint NIM_DELETE = 0x00000002;
    internal const uint NIM_SETVERSION = 0x00000004;
    internal const uint NIF_MESSAGE = 0x00000001;
    internal const uint NIF_ICON = 0x00000002;
    internal const uint NIF_TIP = 0x00000004;
    internal const uint NIF_INFO = 0x00000010;
    internal const uint NIIF_WARNING = 0x00000002;
    internal const uint NOTIFYICON_VERSION_4 = 4;
    internal const int NIN_SELECT = WM_USER;
    internal const int NIN_KEYSELECT = WM_USER + 1;
    internal const int IDI_APPLICATION = 32512;
    internal const uint IMAGE_ICON = 1;
    internal const uint LR_SHARED = 0x00008000;
    internal const int SM_CXSMICON = 49;
    internal const int SM_CYSMICON = 50;

    internal const uint MF_STRING = 0x00000000;
    internal const uint MF_CHECKED = 0x00000008;
    internal const uint MF_POPUP = 0x00000010;
    internal const uint MF_SEPARATOR = 0x00000800;
    internal const uint MF_BYCOMMAND = 0x00000000;
    internal const uint MF_UNCHECKED = 0x00000000;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_RETURNCMD = 0x0100;
    internal const uint TPM_NONOTIFY = 0x0080;

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

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal unsafe struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [LibraryImport("shell32.dll")]
    internal static partial nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShellNotifyIcon(uint dwMessage, ref NOTIFYICONDATAW lpData);

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


    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", SetLastError = true)]
    internal static partial nint LoadImage(nint hInst, nint name, uint type, int cx, int cy, uint fuLoad);

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true)]
    internal static partial nint GetModuleHandle(nint lpModuleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AppendMenu(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint CheckMenuItem(nint hMenu, uint uIDCheckItem, uint uCheck);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EndMenu();

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    internal static partial nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial nint GetAncestor(nint hwnd, uint gaFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll")]
    internal static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(nint hwnd, out PixelRect lpRect);

    [LibraryImport("user32.dll")]
    internal static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    internal static partial nint GetDesktopWindow();

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", SetLastError = true)]
    internal static unsafe partial int GetClassName(nint hwnd, char* className, int maxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct PixelPoint(int X, int Y);
}
