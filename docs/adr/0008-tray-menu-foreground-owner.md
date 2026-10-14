# Let a hidden owner take foreground for the tray menu

Status: accepted, 2026-10-14. Narrows the "never steal focus" rule of the [implementation plan](../implementation-plan.md#appbar-and-shell-behaviour) and the B03 focus gate for the tray menu only. [ADR 0001](0001-separate-windows-appbar.md) and [ADR 0003](0003-keep-appbar-registered-during-fullscreen.md) remain in force.

## Decision

The tray context menu is owned by a hidden, never-shown native window that belongs to the Desktop process. That owner, not the visible bar, is passed to `TrackPopupMenuEx`. Before tracking, the owner calls `SetForegroundWindow` on itself; after tracking, it posts `WM_NULL` to itself. These steps follow the notification-icon pattern in the [`TrackPopupMenu` documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackpopupmenu#remarks).

After the menu closes, the owner returns foreground to the window that was foreground when the menu opened. After a mouse click on the tray icon, that window is the taskbar, because the click activates the taskbar first. The owner restores foreground only when it still holds it, so a window the user activated while the menu was open is never overridden. If Windows refuses to make the owner foreground, the menu still opens: Position and Exit stay usable, and only dismissal by clicking outside the menu degrades.

The menu's modal loop also pumps WPF dispatcher work. Shell recovery is deferred until the menu closes, so it cannot destroy the menu or owner that Windows is still tracking. Shutdown that runs inside the loop calls `EndMenu` before it destroys them.

The visible bar keeps `WS_EX_NOACTIVATE`, its `WM_MOUSEACTIVATE` handling, and `SWP_NOACTIVATE`. It never becomes the foreground window and never receives keyboard focus.

## Reason and alternatives

Windows dismisses a notification-icon menu on an outside click, and routes keyboard navigation to it, only when the menu's owner is the foreground window. B03 also required that no foreground or focus event target the Desktop process. Those two requirements cannot both hold for a native menu.

- **Keep a zero-foreground rule.** The menu can stay open after an outside click, and the keyboard cannot reach it. Rejected as a defect in the only control surface.
- **Build a custom nonactivating popup.** This avoids any foreground change, but it re-implements input, dismissal, accessibility, keyboard, and DPI behavior that native menus already provide. Rejected as disproportionate for two actions.

The hidden owner confines the foreground change to an invisible window, for as long as the menu is open, after an explicit user gesture on the tray icon.

## Acceptance

With `SetWinEventHook` running, a real tray click must produce foreground and focus events only for the taskbar, the hidden owner, and the menu. No event may target the visible bar. After a menu closes without a selection, the window that was foreground when the menu opened must regain foreground. An outside click and Escape must both dismiss the menu. Every other B03 scenario keeps the rule that no event targets the Desktop process.
