# Replace the Windows taskbar in phases while Explorer remains the shell

Status: accepted, 2026-10-15. Extends [ADR 0001](0001-separate-windows-appbar.md); the bar remains a separate AppBar, and embedding into the Windows taskbar remains rejected.

## Decision

After the first personal release, the bar takes over the role of the Windows taskbar in two phases. Explorer remains the Windows shell throughout: the desktop, Start, File Explorer, and notification center keep working.

- **Phase A, task strip ([B017](../bricks/B17-task-strip.md)):** the bar shows a task button for each open task on every monitor, between the left metric and limit sections and a clock on the right. The application does not hide the Windows taskbar and does not change its settings; the user may set it to auto-hide, so the Windows tray remains reachable.
- **Phase B, taskbar replacement ([B018](../bricks/B18-taskbar-replacement.md)):** a Windows 11 feasibility gate first proves that the bar's tray receives the icons of applications already running; if it fails, phase B is rejected and the Windows taskbar stays. Otherwise the bar hosts its own tray and the shell indicators of [ADR 0010](0010-own-shell-indicators.md), then hides the Windows taskbar. It shows the Windows taskbar again when its tray fails and restores it on every handled exit. Abrupt termination skips that cleanup, so B018 records and proves a recovery for it. Hiding the Windows taskbar is not allowed before the bar's tray works, because hiding it also hides every tray icon, including the bar's own position and exit actions.

Neither phase may block or retroactively change the accepted first release.

## Reason and alternatives

The user wants one strip that shows resources, limits, open windows, and time. Explorer owns the taskbar, the tray, and the shell surfaces the user still needs.

- **Replace Explorer as the shell (`Winlogon\Shell`):** gives full control of tasks and tray icons, but removes Start, the desktop, and the notification center, and makes a broken bar leave the session without a shell.
- **Hide the Windows taskbar in phase A, without a tray:** delivers the full replacement first, but makes every tray icon, including the bar's own tray actions, unreachable until phase B.
- **Show tasks while the Windows taskbar stays reachable, then replace it (chosen):** delivers the task strip without removing any existing function, and hides the Windows taskbar only after its tray has a working replacement.

The cost is that Windows 11 may show a hidden taskbar again after Explorer restart, display, or DPI changes, so phase B repeats the shell-recovery checks from [B003](../bricks/B03-shell-position-recovery.md) for the hidden state.
