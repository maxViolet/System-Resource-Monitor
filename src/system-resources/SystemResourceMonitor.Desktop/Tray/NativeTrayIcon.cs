using System.ComponentModel;
using System.Runtime.InteropServices;
using SystemResourceMonitor.Desktop.Settings;
using static SystemResourceMonitor.Desktop.Bar.NativeMethods;

namespace SystemResourceMonitor.Desktop.Bar;

internal interface ITrayIcon
{
    void Start();

    void Recreate();

    void Remove();

    void SetAppliedEdge(BarEdge edge);

    bool ShowStatus(string message);

    bool HandleWindowMessage(int message, nint wParam, nint lParam);
}

internal sealed class TrayIconRegistration(Action add, Action setVersion, Action delete)
{
    public bool IsAdded { get; private set; }

    public void Add()
    {
        add();
        IsAdded = true;
        try
        {
            setVersion();
        }
        catch
        {
            try
            {
                delete();
            }
            finally
            {
                IsAdded = false;
            }

            throw;
        }
    }


    public void Remove()
    {
        if (!IsAdded)
        {
            return;
        }

        try
        {
            delete();
        }
        finally
        {
            IsAdded = false;
        }
    }
}

/// <summary>Owns the native notification icon, its callback, command IDs, and popup-menu handles.</summary>
internal sealed class NativeTrayIcon : ITrayIcon
{
    internal const uint CallbackMessage = WM_APP + 0x31;
    private const uint IconId = 1;
    private const uint TopCommand = 1001;
    private const uint BottomCommand = 1002;
    private const uint ExitCommand = 1003;

    private readonly nint _hwnd;
    private readonly Action<BarEdge> _selectEdge;
    private readonly Action _exitRequested;
    private readonly TrayIconRegistration _registration;
    private readonly TrayMenuOwner _menuOwner;
    private nint _menu;
    private nint _positionMenu;
    private nint _icon;
    private BarEdge _appliedEdge;
    private bool _positionMenuAttached;

    public NativeTrayIcon(nint hwnd, Action<BarEdge> selectEdge, Action exitRequested, TrayMenuOwner menuOwner)
    {
        _hwnd = hwnd;
        _selectEdge = selectEdge;
        _exitRequested = exitRequested;
        _menuOwner = menuOwner;
        _registration = new TrayIconRegistration(AddNativeIcon, SetNativeVersion, DeleteNativeIcon);
    }

    public void Start()
    {
        try
        {
            _menuOwner.Start();
            CreateResources();
            _registration.Add();
        }
        catch
        {
            DestroyResources();
            throw;
        }
    }

    public void Recreate()
    {
        // A spurious or partial shell restart can leave the old identifier alive.
        // Best-effort deletion prevents an existing icon from blocking NIM_ADD.
        _registration.Remove();
        DestroyResources();
        Start();
    }

    public void Remove()
    {
        try
        {
            _registration.Remove();
        }
        finally
        {
            DestroyResources();
            _menuOwner.Remove();
        }
    }

    public void SetAppliedEdge(BarEdge edge)
    {
        _appliedEdge = edge;
        if (_positionMenu == 0)
        {
            return;
        }

        SetChecked(TopCommand, edge == BarEdge.Top);
        SetChecked(BottomCommand, edge == BarEdge.Bottom);
    }

    public unsafe bool ShowStatus(string message)
    {
        if (!_registration.IsAdded)
        {
            return false;
        }

        var data = NewData();
        data.uFlags = NIF_INFO;
        data.dwInfoFlags = NIIF_WARNING;
        Copy("System Resource Monitor", data.szInfoTitle, 64);
        Copy(message, data.szInfo, 256);
        return ShellNotifyIcon(NIM_MODIFY, ref data);
    }

    public bool HandleWindowMessage(int message, nint wParam, nint lParam)
    {
        if ((uint)message != CallbackMessage)
        {
            return false;
        }

        var notification = (int)((long)lParam & 0xffff);
        if (notification is WM_CONTEXTMENU or NIN_SELECT or NIN_KEYSELECT)
        {
            ShowMenu(wParam);
        }

        return true;
    }

    private unsafe void AddNativeIcon()
    {
        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        Copy("System Resource Monitor", data.szTip, 128);
        if (!ShellNotifyIcon(NIM_ADD, ref data))
        {
            throw new InvalidOperationException("NIM_ADD failed for the tray icon.");
        }
    }

    private void SetNativeVersion()
    {
        var data = NewData();
        data.uVersion = NOTIFYICON_VERSION_4;
        if (!ShellNotifyIcon(NIM_SETVERSION, ref data))
        {
            throw new InvalidOperationException("NIM_SETVERSION failed for the tray icon.");
        }
    }

    private void DeleteNativeIcon()
    {
        var data = NewData();
        _ = ShellNotifyIcon(NIM_DELETE, ref data);
    }

    private void CreateResources()
    {
        _icon = LoadIcon(0, IDI_APPLICATION);
        if (_icon == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "LoadIcon failed for the tray icon.");
        }

        _menu = CreatePopupMenu();
        _positionMenu = CreatePopupMenu();
        if (_menu == 0 || _positionMenu == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreatePopupMenu failed for the tray menu.");
        }

        if (!AppendMenu(_positionMenu, MF_STRING, TopCommand, "Top") ||
            !AppendMenu(_positionMenu, MF_STRING, BottomCommand, "Bottom") ||
            !AppendMenu(_menu, MF_POPUP, (nuint)_positionMenu, "Position"))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "AppendMenu failed for the tray menu.");
        }

        _positionMenuAttached = true;
        if (!AppendMenu(_menu, MF_SEPARATOR, 0, null) ||
            !AppendMenu(_menu, MF_STRING, ExitCommand, "Exit"))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "AppendMenu failed for the tray menu.");
        }

        SetAppliedEdge(_appliedEdge);
    }

    private void DestroyResources()
    {
        if (_menu != 0)
        {
            _ = DestroyMenu(_menu);
        }

        if (!_positionMenuAttached && _positionMenu != 0)
        {
            _ = DestroyMenu(_positionMenu);
        }

        _menu = 0;
        _positionMenu = 0;
        _positionMenuAttached = false;
        _icon = 0; // IDI_APPLICATION is shared and must not be destroyed.
    }

    private void SetChecked(uint command, bool isChecked)
    {
        var result = CheckMenuItem(_positionMenu, command, MF_BYCOMMAND | (isChecked ? MF_CHECKED : MF_UNCHECKED));
        if (result == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CheckMenuItem failed for the tray menu.");
        }
    }

    private void ShowMenu(nint packedPoint)
    {
        var point = new POINT
        {
            X = unchecked((short)((long)packedPoint & 0xffff)),
            Y = unchecked((short)(((long)packedPoint >> 16) & 0xffff)),
        };
        if (point.X == 0 && point.Y == 0 && !GetCursorPos(out point))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetCursorPos failed for the tray menu.");
        }

        var command = _menuOwner.Show(_menu, point);
        switch (command)
        {
            case TopCommand:
                _selectEdge(BarEdge.Top);
                break;
            case BottomCommand:
                _selectEdge(BarEdge.Bottom);
                break;
            case ExitCommand:
                _exitRequested();
                break;
        }
    }

    private static unsafe void Copy(string value, char* destination, int capacity)
    {
        var length = Math.Min(value.Length, capacity - 1);
        value.AsSpan(0, length).CopyTo(new Span<char>(destination, capacity));
        destination[length] = '\0';
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = IconId,
    };
}
