# Exit when tray recovery fails

Status: accepted, 2026-10-14. Applies to Explorer recovery in the [implementation plan](../implementation-plan.md#appbar-and-shell-behaviour) and B03. [ADR 0001](0001-separate-windows-appbar.md) and [ADR 0008](0008-tray-menu-foreground-owner.md) remain in force.

## Decision

On `TaskbarCreated`, the bar re-establishes the AppBar first and then the tray icon:

- It calls `ABM_NEW`. A spurious `TaskbarCreated` reaches a bar that Explorer still has registered, and Windows rejects `ABM_NEW` for an already-registered window. On rejection, the bar sends a best-effort `ABM_REMOVE` and calls `ABM_NEW` again. A second rejection is a genuine failure, and the bar does not attempt placement. Placement cannot verify the registration, because `ABM_QUERYPOS` and `ABM_SETPOS` return TRUE for an unregistered window without reserving anything. The bar keeps its responsibility to send `ABM_REMOVE` at shutdown in every case.
- It recreates the icon. It deletes any icon that survived with `NIM_DELETE`, ignoring the result, and then calls `NIM_ADD` and `NIM_SETVERSION`.

If the AppBar is not recovered but the tray is, the failure is reported through the tray, which keeps Position and Exit available.

If the tray icon cannot be recreated, the application enters the shared shutdown path with the failure exit code. That path sends `ABM_REMOVE`, so the reservation is released.

## Reason and alternatives

The tray is the bar's only control surface. Without the tray there is no Position and no Exit, and the bar itself cannot take input. Leaving a reserved strip that the user can only end through Task Manager is worse than exiting.

- **Keep running without a tray.** Rejected: the reservation becomes uncontrollable, and the failure is invisible because the status path needs the icon.
- **Retry `NIM_ADD` with a bounded delay.** Explorer broadcasts `TaskbarCreated` only after the taskbar exists, so a failure at that point indicates a persistent shell problem rather than a startup race. A retry adds a timer state machine for a case with no observed transient failure. Revisit if Windows evidence shows transient `NIM_ADD` failures after `TaskbarCreated`. Startup timing at logon belongs to the autostart work, not here.

## Acceptance

Deterministic tests cover four behaviors: the recovery decision for each AppBar and tray outcome, re-registration after a rejected `ABM_NEW` whose registration survived, a rejected re-registration that is reported as a failure even though unregistered placement would succeed, and deletion of a surviving icon before it is added again. A Windows probe recorded that `ABM_QUERYPOS` and `ABM_SETPOS` return TRUE for an unregistered window and that a second `ABM_NEW` returns FALSE. A real Explorer restart on Windows 11 must restore the reservation and a working tray. A tray failure must exit with the work area restored.
