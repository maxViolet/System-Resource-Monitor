# B003 — Tray, edge settings, and shell/display recovery

| | |
|---|---|
| **Area** | Desktop |
| **Depends on** | B002 |
| **Unblocks** | B004 |
| **Sources** | [Product plan — Product and first release](../plan.md#product-and-first-release), [Implementation plan — Settings and lifecycle](../implementation-plan.md#settings-and-lifecycle) |

## Why

A useful bar must remain controllable and must repair its shell integration when Explorer or monitor geometry changes. These behaviors are separable from fullscreen detection and can be proven on the empty bar.

## What done looks like

The tray can switch the bar between the top and bottom of the primary monitor and exit. The choice survives restart when persistence succeeds. Explorer restart, resolution/DPI changes, and a primary-monitor change recreate or reposition the AppBar and tray without stale reservations or focus stealing.

## Context

The only first-release setting is edge, defaulting to Top, stored at `%LocalAppData%\SystemResourceMonitor\settings.json`. Replace atomically from a temporary file in the same directory. Missing settings use defaults. Preserve malformed settings as `settings.invalid-<timestamp>.json` when possible. Read, backup, or write failure must not stop monitoring; a failed write reports that the current position applies only until restart.

Handle AppBar position notifications, `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `TaskbarCreated`, and primary-monitor changes on the WPF thread. On `TaskbarCreated`, re-register and renegotiate the AppBar, recreate the tray icon with `NIM_ADD`, and set its callback behavior with `NIM_SETVERSION`. Report failures instead of assuming recovery succeeded; a tray that cannot be recreated ends the application and releases its reservation ([ADR 0009](../adr/0009-exit-when-tray-recovery-fails.md)). The tray menu uses the hidden foreground owner from [ADR 0008](../adr/0008-tray-menu-foreground-owner.md); no other B003 path may make a Desktop window foreground.

## Acceptance criteria

- [ ] Ten consecutive top/bottom switches leave exactly one reservation at the selected edge
- [ ] Valid settings survive restart; missing settings select Top
- [ ] Malformed settings are preserved when possible and the bar still starts
- [ ] Injected read, backup, and write failures do not prevent the bar from starting or remaining controllable; write failure produces the restart-only message
- [ ] Explorer restart restores the reserved strip and a working tray with position and Exit actions
- [ ] Resolution, DPI, and primary-monitor changes move and reserve the bar correctly while restoring the old monitor's work area
- [ ] Auto-hidden Windows taskbar behavior is recorded and leaves no stale reservation

## Out of scope

Fullscreen reconciliation and the final two-day placement gate belong to B004. Appearance and system metric text belong to B006.
