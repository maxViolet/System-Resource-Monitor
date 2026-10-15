# B18 — Own tray, shell indicators, and hidden Windows taskbar

| | |
|---|---|
| **Area** | Desktop + acceptance |
| **Depends on** | B17 |
| **Unblocks** | optionally B16 |
| **Sources** | [Product plan — Task strip and taskbar replacement](../plan.md#task-strip-and-taskbar-replacement), [Implementation plan — Independent later slices](../implementation-plan.md#independent-later-slices), [Implementation plan — Settings and lifecycle](../implementation-plan.md#settings-and-lifecycle), [ADR 0008](../adr/0008-replace-taskbar-buttons-in-phases.md), [ADR 0009](../adr/0009-managedshell-for-tasks-and-tray.md), [ADR 0010](../adr/0010-own-shell-indicators.md), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

After B17 the Windows taskbar still exists only for its tray and its volume, network, and battery controls. Phase B gives the bar its own tray and shell indicators so the Windows taskbar can be hidden without making any of them unreachable.

## What done looks like

From left to right after the task strip, the bar shows the tray, the shell indicators, and the clock. The tray shows up to 6 tray icons in the Windows order and puts the rest in the tray overflow menu `^`, including the bar's own icon with the position and exit actions when it does not fit. The Windows taskbar is hidden while the bar runs and is visible again after every handled exit.

## Context

Run the feasibility gate first, on Windows 11, before writing code that hides the Windows taskbar. With applications already running when the bar starts, ManagedShell's tray services must deliver every icon those applications show in the Windows tray, including the icons in its overflow. Record the applications, the icons received, and any icon that is not recovered. If an icon cannot be recovered, mark this brick `rejected`: the Windows taskbar is not hidden, and B17 remains the delivered phase.

Take tray icons only from ManagedShell's tray services in Desktop, as ADR 0009 requires. Icons, tooltips, clicks, and context menus must reach the owning applications as they do from the Windows tray. Build the shell indicators as ADR 0010 specifies.

Hide the Windows taskbar only while the bar's tray works. If the tray cannot start, leave the Windows taskbar visible; if the tray stops working while the bar runs, show the Windows taskbar again. Report both cases in the bar's tooltip.

Windows 11 may show the hidden taskbar again. Hide it again after Explorer restart, display, DPI, and primary-monitor changes. Restore it on exit, on handled fatal startup failure, and on the shutdown deadline path in the implementation plan. Abrupt termination does not run that cleanup; record the recovery for it, a restart of Explorer or one more start and exit of the bar, and prove it.

Create `plan.md` in this brick's package before implementation; this brief does not yet fix the method of hiding the Windows taskbar.

## Acceptance criteria

- [ ] Feasibility gate: with at least five applications already showing tray icons, visible and in the Windows tray overflow, every icon appears in the bar's tray after start; the evidence is recorded under `docs/acceptance/`, or the brick is `rejected` with the unrecovered icons named
- [ ] Icons added, changed, or removed while the bar runs follow within one second
- [ ] Left click, right click, double click, tooltip, and context menu of tray icons from at least five different applications behave as in the Windows tray
- [ ] With 1, 6, and 20 tray icons, the bar shows at most 6, puts the rest in `^`, and the task strip, the shell indicators, and the clock keep their own space rules
- [ ] The bar's own position and exit actions work from its tray icon, on the bar and in `^`
- [ ] The volume indicator follows level and mute changes made elsewhere within one second, changes the level on scroll, and toggles mute on click
- [ ] The network indicator follows connect, disconnect, and limited-connectivity changes; the battery indicator follows charge and charging changes and is absent on a system without a battery
- [ ] A right click on each shell indicator opens its `ms-settings:` page; a failed indicator source shows unavailable
- [ ] A tray that cannot start leaves the Windows taskbar visible; a tray that stops while the bar runs makes it visible again
- [ ] The Windows taskbar stays hidden after Explorer restart, resolution and DPI changes, and primary-monitor change
- [ ] Exit, handled startup failure, and the shutdown deadline path each leave the Windows taskbar visible and the work area restored
- [ ] After the bar is terminated through Task Manager, the recorded recovery makes the Windows taskbar visible again
- [ ] All affected B07 and B17 gates, including CPU and memory budgets with the tray and shell indicators enabled, pass and are recorded under `docs/acceptance/`

## Out of scope

Replacing Explorer as the Windows shell, Start, and the notification center are rejected by ADR 0008. Volume device selection, Wi-Fi network selection, brightness, and power-mode controls stay in Windows, as ADR 0010 records.
