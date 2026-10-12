# Keep AppBar registered during fullscreen

Status: accepted, 2026-10-11. Supplements [ADR 0001](0001-separate-windows-appbar.md) and [ADR 0002](0002-appbar-first-cpu-ram-release.md); their placement, architecture, and feasibility decisions remain in force.

## Decision

Keep AppBar registration and the reserved strip while fullscreen is active. Lower the bar to the bottom of the z-order for active fullscreen on the primary monitor, and restore its normal z-order without activation when fullscreen ends or Alt+Tab activates a normal window. The fullscreen process need not terminate. Fullscreen on another monitor leaves the bar unchanged.

Use ABN_FULLSCREENAPP as a reconciliation trigger and a 500 ms foreground-window/monitor-bounds check to cover changes of active window. The implementation plan defines state reconciliation and Explorer recovery. ABM_REMOVE is reserved for application shutdown and handled fatal startup cleanup, not fullscreen transitions.

## Reason and alternatives

[ABM_REMOVE](https://learn.microsoft.com/en-us/windows/win32/shell/abm-remove) unregisters the AppBar and stops its notifications. Removing it on fullscreen entry and waiting for a subsequent AppBar exit notification could therefore leave the bar permanently lowered or hidden. [ABN_FULLSCREENAPP](https://learn.microsoft.com/en-us/windows/win32/shell/abn-fullscreenapp) describes lowering and restoring z-order for registered AppBars.

Releasing the strip with an independent foreground/location detector is possible, but adds registration transitions and recovery races. The first release deliberately retains the strip; it does not promise extra work area during fullscreen. An independent periodic reconciliation still covers Alt+Tab, but does not own an unregister/re-register cycle.

## Acceptance

Product-plan stage 1 requires ten cycles each for video, exclusive and borderless fullscreen, including Alt+Tab, plus Explorer restart during fullscreen and a second-monitor check. The bar must return without focus stealing and retain its reservation through ordinary fullscreen transitions. Actual Windows 11 evidence is required; notification/state tests alone do not establish desktop correctness.
