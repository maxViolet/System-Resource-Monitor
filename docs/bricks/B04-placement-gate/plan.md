# B04 implementation plan for coding agents

Status: planned. This package implements [B04 — Fullscreen reconciliation and placement gate](../B04-placement-gate.md). The [product-plan placement gate](../../plan.md#1-validate-placement-first), [AppBar and shell behaviour](../../implementation-plan.md#appbar-and-shell-behaviour), [first-release desktop gate](../../implementation-plan.md#first-release-acceptance-gates), [ADR 0003](../../adr/0003-keep-appbar-registered-during-fullscreen.md), [ADR 0009](../../adr/0009-exit-when-tray-recovery-fails.md), and [B03 handoff](../B03-shell-position-recovery/plan.md#boundary-for-b04) govern ambiguity. B03 must be accepted before B04 implementation begins.

## Outcome and handoff

Deliver fullscreen reconciliation for the empty, registered AppBar and a recorded placement-feasibility decision. Active fullscreen on the primary monitor lowers the bar to the bottom of the z-order without releasing its strip. Departure from fullscreen, including Alt+Tab to a normal window while the fullscreen process remains alive, restores normal z-order without activation within one second. Fullscreen on another monitor and an ordinary maximized window leave the bar in its normal state. Explorer recovery and primary-monitor changes reconcile against the current foreground window and current primary monitor.

B04 is the final gate before B05, B10, and B11. Automated tests cannot accept it without the named Windows 11 desktop scenarios. Do not add system metric collection, CPU/RAM presentation, a non-reserving placement, or a second shell-recovery coordinator.

## Work order

Complete these steps in order. Keep the solution buildable after each implementation step. Record outcomes in a dated report under `docs/acceptance/`; distinguish automated tests, Windows integration, and manual published-app observations.

### 1. Verify the B03 handoff and shared clock

- Require an accepted B03 report with direct evidence for its tray, edge, Explorer, display/DPI, primary-monitor, auto-hidden taskbar, focus, and stale-reservation checks. Rerun the B02 start/exit and handled-startup-failure checks during B04 rather than inheriting their results.
- Use B02's accepted [2026-10-14 report](../../acceptance/2026-10-14-B02-empty-appbar-lifecycle.md) as the lifecycle baseline. Its second-process file-system trace recorded zero settings-directory operations. The shared investigation started at **2026-10-14 12:18 UTC+05:00** and reaches its two-working-day deadline at **2026-10-16 12:18 UTC+05:00**.
- Inspect the accepted B03 dispatcher-owned placement/recovery path, its `ABN_FULLSCREENAPP` hook point, normal z-order behavior, AppBar callback lifetime, tray recovery result, and shutdown ownership. Capture the Windows build, display topology, primary monitor, resolution/scale, taskbar state, baseline work areas, and a foreground application used for focus checks. Identify the exact source commit and published/test-host artifacts used for the B04 baseline.
- At the shared deadline, if a blocking placement failure remains in B03–B04, record it, mark B05 blocked, and request the explicit non-reserving-placement-or-suspension decision. Do not implement a fallback or change ADR 0001 without a new decision. B10 and B11 also remain gated by B04 acceptance.

**Gate:** B03 is accepted before code work; the B04 report identifies the accepted B02 and B03 source/evidence and the one shared deadline. If B03 is not accepted, leave B04 unaccepted and record the blocker.

### 2. Classify active primary-monitor fullscreen

- Add a narrow Desktop-only foreground/window-geometry reader. On the WPF dispatcher, inspect the current foreground top-level window, resolve its owning monitor, and compare its bounds with that monitor's full `rcMonitor` in physical pixels. Classify fullscreen only when it covers the **current primary** monitor; never compare with the reduced work area. Exclude the bar, shell desktop windows, invisible or invalid windows, and ordinary maximized windows.
- Keep the classification separate from AppBar notifications. `ABN_FULLSCREENAPP` is only a request to inspect current state; its payload is not a durable fullscreen flag. A second-monitor fullscreen window cannot lower the primary-monitor bar.
- Make physical-pixel conversion, monitor selection, invalid-handle handling, and native return-value checks explicit. Keep Win32 interop inside Desktop. Do not infer fullscreen from a window title, process name, or stored game list.
- If an API fails or a foreground snapshot is inconsistent, report the observation as indeterminate and keep the current registration/reservation. Recheck on the next trigger; do not turn an uncertain sample into a permanent lowered state.

**Gate:** deterministic classification cases cover primary fullscreen, other-monitor fullscreen, Alt+Tab to a normal window, ordinary maximized window, bar/shell foreground, and invalid observations. Native integration checks confirm physical-pixel coordinates on the target Windows desktop.

### 3. Reconcile z-order from one state machine

- Extend B03's dispatcher-owned coordinator with `normal` and `lowered` z-order states. Funnel `ABN_FULLSCREENAPP`, B03 shell/display/DPI/primary-monitor triggers, and a 500-ms periodic check into the same reconciliation path. Coalesce queued triggers; never overlap or recursively run placement and fullscreen passes.
- Start the periodic check after the AppBar is positioned and stop it during shutdown. Keep it running while lowered so Alt+Tab can restore the bar while the fullscreen process stays alive. Use fresh foreground and primary-monitor geometry for each pass.
- Lower to the bottom of z-order only on a `normal → lowered` transition. Restore normal AppBar z-order only on a `lowered → normal` transition. Preserve B03's negotiated rectangle and use `SWP_NOACTIVATE`; do not focus or activate the bar. Repeated notifications or timer ticks in the same state make no z-order call.
- Never call `ABM_REMOVE` or release the reserved strip for fullscreen. Preserve B02's immediate removal on actual shutdown. Surface failed z-order operations as failed reconciliation, retain a retryable state, and do not report the bar restored before the native operation succeeds.
- After a primary-monitor change, let B03 renegotiate the reservation on the new primary, then classify the current foreground window against that new primary. Do not carry a lowered decision from the former primary monitor.

**Gate:** lifecycle tests prove registered → lowered → restored without `ABM_REMOVE` or a changed reservation; repeated triggers are idempotent; Alt+Tab restores without process termination; second-monitor fullscreen and maximized windows do not lower the bar; a primary-monitor change uses the new monitor.

### 4. Reconcile after Explorer recovery

- Preserve B03's `TaskbarCreated` recovery on the owning WPF thread ([ADR 0009](../../adr/0009-exit-when-tray-recovery-fails.md)): call `ABM_NEW`; if it is rejected, send a best-effort `ABM_REMOVE` and retry `ABM_NEW` once. A second rejection is an AppBar failure and must not proceed to placement. After successful registration, run `ABM_QUERYPOS`/`ABM_SETPOS` and apply the rectangle. Delete any surviving tray icon with `NIM_DELETE` (ignore that result), then call `NIM_ADD` and `NIM_SETVERSION`. Check AppBar and tray results separately; a returned window message is not proof of recovery.
- Only after AppBar and tray recovery succeed, inspect the current foreground/primary-monitor state and choose the bar's z-order. Recovery during primary-monitor fullscreen ends lowered; recovery after an Alt+Tab ends normal. Keep the periodic check alive after recovery.
- If AppBar recovery fails but the tray is recovered, report the failure through the tray and keep Position and Exit available for retry. If tray recreation fails, enter the shared failure-shutdown path with a failure exit code; that path sends `ABM_REMOVE` and releases the reserved strip. Do not leave a running bar without its tray control surface.
- Do not duplicate tray icons, managed handlers, timers, or AppBar registrations across repeated `TaskbarCreated` messages. The conditional remove-and-re-register branch belongs only to shell recovery; fullscreen z-order transitions never unregister the AppBar.

**Gate:** ordered tests prove resources are recreated before fullscreen z-order selection, including the rejected-`ABM_NEW` retry and delete-before-add tray sequence. AppBar failure with a recovered tray remains reported and controllable; tray failure enters shared failure shutdown and restores work area. A real Explorer restart during fullscreen must separately prove reservation, tray actions, and correct z-order.

### 5. Automate the transition and regression checks

- Add focused Desktop tests around observable state, call order, and timing using controlled foreground/monitor snapshots and time. Assert the 500-ms reconciliation remains active while lowered and restoration is requested on the first observation after Alt+Tab; the one-second desktop threshold still requires real Windows evidence.
- Cover repeated `ABN_FULLSCREENAPP`, timer/notification bursts, failed and retried z-order changes, Explorer recovery during fullscreen, other-monitor fullscreen, maximized-window exclusion, and a change of primary monitor. Preserve B03's tests for a rejected `ABM_NEW` and its conditional `ABM_REMOVE`/second `ABM_NEW`, delete-before-add tray recreation, AppBar failure with a working tray, and tray failure causing shared shutdown and work-area restoration. Assert no fullscreen path calls `ABM_REMOVE`, `ABM_NEW`, or `ABM_SETPOS` solely because of a z-order transition.
- Rerun the focused B02/B03 lifecycle, settings, tray, and placement tests. Use a separate test-host mode only where a real HWND/AppBar is needed; keep any fault injection and probes out of the published app.

**Gate:** focused tests pass with externally visible state/call-order assertions. Native integration and published-app checks remain distinct and unverified until run.

### 6. Run the published-app placement gate on Windows 11

Run targeted Desktop tests first, then the repository checks from [ADR 0005](../../adr/0005-project-level-runtime-and-testing-platform.md) with the current project path:

```powershell
dotnet test --project tests/SystemResourceMonitor.Desktop.Tests/SystemResourceMonitor.Desktop.Tests.csproj -c Release
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

- Build the published app and any test host from one source commit without intervening edits. Record commit/tree IDs, executable SHA-256 values, and matching production-assembly hashes. Check that the published folder contains no test host, fixtures, or diagnostic/fault switches.
- On actual Windows 11, run **ten enter/leave cycles each** for fullscreen video, an exclusive game, and a borderless game. Each cycle includes Alt+Tab to a normal window and back while the fullscreen process remains alive. Record foreground/focus events, bar z-order/visibility, AppBar registration, bar rectangle, and primary-monitor work area before, during, and after. Require lowering only for active primary-monitor fullscreen, unchanged registration and reserved strip, restoration within one second of departure, and no bar focus/activation.
- Separately restart Explorer during fullscreen and confirm AppBar registration, reserved strip, one restored tray icon, correct lowered state, working Position and Exit actions, and restoration after Alt+Tab. Test fullscreen on another monitor, an ordinary maximized window, and changing the primary monitor while running.
- Rerun the B02–B03 desktop regressions: launch and second launch, normal exit, handled startup failure, ten Top/Bottom switches, Explorer recovery, resolution changes, 100/150/200-percent DPI where available, auto-hidden taskbar, and both directions of primary-monitor change. Record work area and focus before/during/after; require no stale reservation after exit or monitor/edge changes.
- Record unavailable scenarios as unverified, not passed. The single-monitor configuration in the B02 report cannot prove the second-monitor or primary-monitor-change gates; use a two-monitor Windows 11 setup for those checks. Preserve and restore any pre-existing user settings during the run.

**Gate:** every B04 criterion and B02–B03 regression passes on the tested published artifact. Automated success alone never closes the desktop gate.

### 7. Record the decision and hand off

- Write a dated, sanitized `docs/acceptance/` report separating automated, Windows integration, and manual published-app evidence. Include source/artifact identity, Windows build, monitor topology and DPI, taskbar configuration, scenario counts, measured restoration times, registration/reservation/focus results, failures, and unresolved checks. Exclude credentials, usernames, absolute user paths, process/window titles, precise activity timelines, and unredacted screenshots.
- If all gates pass, mark B04 accepted in the roadmap and unblock B05, B10, and B11. If a blocking failure remains at the shared deadline, mark B05 blocked, keep dependent bricks gated, and request the placement-or-suspension decision specified in the product plan. A non-reserving panel needs a decision superseding or qualifying ADR 0001; do not silently add one.
- Keep any later change to the product plan, implementation plan, brick states, or ADRs under the repository's documentation-approval rule.

**Gate:** the report states an evidence-backed B04 outcome, identifies every unverified check, and applies the shared two-working-day decision rule without extending or restarting the clock.
