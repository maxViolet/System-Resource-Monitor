# B04 — Fullscreen reconciliation and placement gate

| | |
|---|---|
| **Area** | Desktop + acceptance |
| **Depends on** | B03 |
| **Unblocks** | B05, B10, B11 |
| **Sources** | [Product plan — Validate placement first](../plan.md#1-validate-placement-first), [First-release gate — Desktop behavior](../implementation-plan.md#first-release-acceptance-gates), [ADR 0003](../adr/0003-keep-appbar-registered-during-fullscreen.md) |

## Why

Fullscreen and shell recovery are the highest-risk desktop interactions. The plan permits only two working days for this feasibility gate; unresolved blocking failures must stop dependent implementation rather than producing a fragile fallback.

## What done looks like

The empty bar remains registered and keeps its reserved strip while active fullscreen content occupies the primary monitor. It lowers to the bottom of the z-order, then restores without activation within one second after fullscreen departure or Alt+Tab to a normal window. Other-monitor fullscreen and ordinary maximized windows leave it unchanged.

## Context

Treat `ABN_FULLSCREENAPP` only as a reconciliation trigger. Every 500 ms, compare the foreground top-level window with full primary-monitor bounds in physical pixels. Exclude the bar and shell desktop windows, coalesce checks, and change z-order only when state changes. Never call `ABM_REMOVE` for fullscreen.

After `TaskbarCreated`, recreate AppBar and tray resources, reconcile current fullscreen state, and only then select z-order. Keep reconciliation active while lowered. Record focus and work area before, during, and after every manual scenario.

## Acceptance criteria

- [ ] Automated lifecycle tests cover registered → lowered → restored without `ABM_REMOVE`, repeated notifications, Alt+Tab while the fullscreen process stays alive, other-monitor fullscreen, and maximized-window exclusion
- [ ] On actual Windows 11, ten enter/leave cycles each pass for fullscreen video, an exclusive game, and a borderless game; the AppBar registration and reserved strip remain unchanged in every cycle
- [ ] Each cycle includes Alt+Tab to a normal window and back; restoration occurs within one second and never steals focus
- [ ] Explorer restart during fullscreen restores AppBar, tray, reservation, and the correct z-order
- [ ] Fullscreen on another monitor does not lower the bar; an ordinary maximized window is verified as a negative case; changing the primary monitor reconciles against the new primary
- [ ] Start/exit, handled startup failure, edge changes, resolution/DPI changes, auto-hidden taskbar, and stale-reservation checks from B02–B03 remain passing
- [ ] Dated evidence is recorded under `docs/acceptance/`
- [ ] If any blocking failure remains two working days after the B02 start timestamp, B05 is marked blocked and an explicit placement-or-suspension decision is requested; no fallback is implemented automatically

## Out of scope

System metric collection and visual CPU/RAM sections begin in B05 and B06. A non-reserving panel requires a new decision superseding or qualifying ADR 0001.
