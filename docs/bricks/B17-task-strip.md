# B017 — Task strip and clock

| | |
|---|---|
| **Area** | Desktop |
| **Depends on** | B007 |
| **Unblocks** | B018; optionally B016 |
| **Sources** | [Product plan — Task strip and taskbar replacement](../plan.md#task-strip-and-taskbar-replacement), [Implementation plan — Independent later slices](../implementation-plan.md#independent-later-slices), [ADR 0008](../adr/0008-replace-taskbar-buttons-in-phases.md), [ADR 0009](../adr/0009-managedshell-for-tasks-and-tray.md), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

The user switches windows through the Windows taskbar while the bar already occupies a full-width strip. Showing tasks and time on the bar is phase A of replacing the Windows taskbar, and it must work before phase B hides the Windows taskbar.

## What done looks like

The bar shows CPU/RAM and limit sections on the left, the task strip in the centre, and the clock on the right. The task strip has one task button for each task on every monitor. A click activates the task's window, or minimizes it when it is already active; a middle click asks the window to close. The Windows taskbar remains visible or auto-hidden as the user configured it.

## Context

Take tasks only from ManagedShell's task services in Desktop, as ADR 0009 requires; keep the bar's own AppBar, fullscreen reconciliation, and shell recovery. Verify that enabling ManagedShell registers no second AppBar and does not change the work area.

A task button shows the window icon and its title, trimmed to fit, highlights the active task, and shows attention while the task is flashing. Task buttons are centred in the space between the left sections and the clock. A task button with a title is 48–160 device-independent pixels (DIP) wide and shrinks within that range when space runs out. When every task button would be narrower than 48 DIP, all become 32 DIP icon-only buttons; tasks whose icon-only buttons still do not fit move to the overflow menu `»`. Overflow never shrinks or moves the metric and limit sections, whose fixed widths stay as in the product plan.

Activating a task must bring its window to the foreground even though the bar is a non-activating window. The bar itself never takes focus when a task button is clicked or when the task list changes.

The clock shows the time in the short time format of the current Windows regional settings, 24-hour or 12-hour, and shows the full date in its tooltip. It updates at each minute boundary rather than by polling, and follows changes of time zone, system time, and regional format. Clicking the clock does nothing.

The task list survives Explorer restart, display and DPI changes, and primary-monitor change, under the same scenarios as B003. Record ManagedShell's version and every Windows 11 scenario under `docs/acceptance/`.

## Acceptance criteria

- [ ] Only Desktop references ManagedShell, at the exact version pinned in `packages.lock.json`; the architecture test still passes for both Core projects
- [ ] With ManagedShell enabled, the bar is the only AppBar the application registers, and exit restores the work area
- [ ] Opening, closing, renaming, flashing, and activating windows on every monitor update the matching task button within one second; UWP applications appear once, with their own icon and title
- [ ] Click, click on the active task, and middle click activate, minimize, and ask to close the window; the bar never takes focus
- [ ] With 1, 10, and 40 tasks, titled task buttons stay between 48 and 160 DIP, all buttons become 32 DIP icon-only buttons when 48 DIP does not fit, and the rest move to `»`; the left sections and the clock keep their position and width
- [ ] The clock shows the regional short time format in both a 24-hour and a 12-hour regional setting, follows format, time zone, and system-time changes within one minute boundary, and its tooltip shows the full date
- [ ] Explorer restart, resolution and DPI changes, and primary-monitor change leave a complete, current task list; the evidence is recorded under `docs/acceptance/`
- [ ] All affected B007 gates, including CPU and memory budgets with the task strip enabled, pass and are recorded

## Out of scope

Hiding the Windows taskbar, the bar's own tray, and the shell indicators belong to B018. Task grouping, pinned applications, thumbnails, jump lists, and `ITaskbarList3` progress or overlay icons are not authorized by the product plan. A calendar or any action on clicking the clock is not authorized.
