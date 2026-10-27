# B017 — Task strip and clock

| | |
|---|---|
| **Area** | Desktop |
| **Depends on** | B007 |
| **Unblocks** | B018; optionally B016 |
| **Sources** | [Product plan — Shared bar design](../plan.md#shared-bar-design), [Implementation plan — Independent later slices](../implementation-plan.md#independent-later-slices), [ADR 0008](../adr/0008-replace-taskbar-buttons-in-phases.md), [ADR 0009](../adr/0009-managedshell-for-tasks-and-tray.md), [ADR 0023](../adr/0023-icon-only-task-buttons.md), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

The user switches windows through the Windows taskbar while the bar already occupies a full-width strip. Showing tasks and time on the bar is phase A of replacing the Windows taskbar, and it must work before phase B hides the Windows taskbar.

## What done looks like

The bar shows CPU/RAM and limit sections on the left, the task strip in the centre, and the clock on the right. The task strip has one task button for each task on every monitor. A click activates the task's window, or minimizes it when it is already active; a middle click asks the window to close. The Windows taskbar remains visible or auto-hidden as the user configured it.

## Context

Take tasks only from ManagedShell's task services in Desktop, as ADR 0009 requires; keep the bar's own AppBar, fullscreen reconciliation, and shell recovery. Verify that enabling ManagedShell registers no second AppBar and does not change the work area.

A task button is always 32 device-independent pixels (DIP) wide and shows only the window icon, or a neutral window icon if the task has none. The full window title is in its tooltip and accessible name. Keep task-button order stable on activation, append new tasks at the end, and close gaps without re-sorting. Show the active state and a steady, non-animated attention marker when a task is flashing; distinguish both without color alone. Task buttons are centred in the space between the fixed-width left sections and the clock. Move trailing tasks that do not fit to `»`, which lists their full titles. If the active task is in `»`, mark both `»` and its menu item as active. Overflow never shrinks or moves the metric and limit sections.

Activating a task must bring its window to the foreground even though the bar is a non-activating window. The visible bar never takes focus when a task button is clicked or when the task list changes. A separately focusable surface entered by a keyboard command must let the user select tasks and open `»`. The B017 package plan must fix that command after checking conflicts and feasibility before implementation.

Before implementing the layout, measure and record the minimum supported width in the real WPF bar with the widest allowed metric and limit values, both regional time formats, and 40 tasks. At that width, the fixed sections, clock, and `»` remain reachable. Do not infer the bound from the HTML prototype; a narrower target needs a revised design decision before implementation.

The clock shows the time in the short time format of the current Windows regional settings, 24-hour or 12-hour, and shows the full date in its tooltip. It updates at each minute boundary rather than by polling, and follows changes of time zone, system time, and regional format. Clicking the clock does nothing.

The task list survives Explorer restart, display and DPI changes, and primary-monitor change, under the same scenarios as B003. Record ManagedShell's version and every Windows 11 scenario under `docs/acceptance/`.

## Acceptance criteria

- [ ] Only Desktop references ManagedShell, at the exact version pinned in `packages.lock.json`; the architecture test still passes for both Core projects
- [ ] With ManagedShell enabled, the bar is the only AppBar the application registers, and exit restores the work area
- [ ] Opening, closing, renaming, flashing, and activating windows on every monitor update the matching task button within one second; UWP applications appear once, with their own icon and full title in the tooltip, accessible name, and `»` when overflowed
- [ ] Click, click on the active task, and middle click activate, minimize, and ask to close the window; the bar never takes focus
- [ ] With 1, 10, and 40 tasks, every task button stays 32 DIP and icon-only; trailing tasks move to `»`, which marks a hidden active task, and the left sections and clock keep their position and width
- [ ] A task without a window icon uses the neutral fallback; duplicate icons keep separate buttons and remain identifiable by their full titles; activation does not reorder buttons, new tasks append, and flashing shows a steady marker distinguishable from active state without color alone
- [ ] The B017 package plan records the keyboard entry command and conflict/feasibility result before implementation; keyboard-only use can select tasks and open/use `»` through the focusable surface, while ordinary pointer use leaves the visible bar non-activating
- [ ] The B017 package plan records a measured WPF minimum supported width before layout implementation; with 40 tasks at that width, the fixed sections, clock, and `»` remain reachable without clipping
- [ ] The clock shows the regional short time format in both a 24-hour and a 12-hour regional setting, follows format, time zone, and system-time changes within one minute boundary, and its tooltip shows the full date
- [ ] Explorer restart, resolution and DPI changes, and primary-monitor change leave a complete, current task list; the evidence is recorded under `docs/acceptance/`
- [ ] All affected B007 gates, including CPU and memory budgets with the task strip enabled, pass and are recorded

## Out of scope

Hiding the Windows taskbar, the bar's own tray, and the shell indicators belong to B018. Task grouping, pinned applications, thumbnails, jump lists, and `ITaskbarList3` progress or overlay icons are not authorized by the product plan. A calendar or any action on clicking the clock is not authorized.
