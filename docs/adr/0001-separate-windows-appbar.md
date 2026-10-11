# Separate AppBar instead of embedding into the Windows taskbar

Status: accepted, 2026-10-11.

The user needs an always-visible strip across the full width of the primary monitor that reserves space at the top of the screen or at the bottom above the Windows taskbar. An arbitrary block inside the Windows 11 taskbar is not a suitable supported extension point, and third-party Windows Widgets are shown on a separate board. The application therefore creates its own bar through the documented [Windows AppBar API](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars). This choice determines the window model and the interaction with the work area: the application must negotiate its position with Windows and release the space on exit.

Rejected: taskbar deskband (not supported on Windows 11), Windows Widgets (the board opens on demand and is not always visible), an always-on-top window without reserved space (covers maximized windows).
