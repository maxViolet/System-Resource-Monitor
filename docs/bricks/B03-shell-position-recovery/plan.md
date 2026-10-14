# B03 implementation plan for coding agents

Status: planned. This package implements [B03 — Tray, edge settings, and shell/display recovery](../B03-shell-position-recovery.md). The [product plan](../../plan.md#product-and-first-release), [placement gate](../../plan.md#1-validate-placement-first), [implementation plan](../../implementation-plan.md#settings-and-lifecycle), [AppBar behavior](../../implementation-plan.md#appbar-and-shell-behaviour), [ADR 0001](../../adr/0001-separate-windows-appbar.md), [ADR 0002](../../adr/0002-appbar-first-cpu-ram-release.md), [ADR 0007](../../adr/0007-resource-projects-by-package.md), and [ADR 0008](../../adr/0008-tray-menu-foreground-owner.md) govern ambiguity. B02 must be accepted before B03 implementation begins.

## Outcome and handoff

Deliver a native tray icon whose position actions move the empty bar between the Top and Bottom edges of the current primary monitor and whose Exit action enters B02's shutdown coordinator. Persist the selected edge when possible without letting settings failures prevent startup or control. Reconcile the AppBar and tray after Explorer, display, DPI, and primary-monitor changes without leaving a reservation on the old monitor or activating the bar.

B04 must be able to add fullscreen reconciliation to the same window-message and placement path without replacing B03's recovery logic. Do not mark B03 done from automated checks alone: Explorer restart, monitor, DPI, auto-hidden taskbar, focus, and stale-reservation claims need direct Windows 11 evidence.

## Work order

Complete these steps in order. Keep the solution buildable after each step. Preserve B02's single-instance-before-settings order and immediate AppBar removal during shutdown. Do not add fullscreen detection, system metrics, appearance work, or a placement fallback.

### 1. Verify the B02 handoff

- Require an accepted B02 report containing the placement investigation start timestamp and shared two-working-day deadline. If B02 remains in progress or its published-app evidence does not pass, record B03 as blocked rather than implementing against an unaccepted lifecycle.
- Start from B02's production `DesktopStartup`, `BarWindow`, and `ShutdownCoordinator`. Preserve the per-session guard, `PerMonitorV2` manifest, 24-DIP thickness, non-activating HWND styles and `WM_MOUSEACTIVATE` handling, AppBar callback lifetime, and one idempotent shutdown path.
- Capture the target Windows build, at least two-monitor topology needed for the primary-monitor scenario, each monitor's resolution and scale, primary monitor, taskbar edge/auto-hide state, and each monitor's work area. Use role and relative position rather than monitor model or serial number.
- Copy B02's baseline work-area, focus, second-process, normal-exit, and handled-startup-failure scenarios into the B03 report as rerun checks, not as inherited results.

**Gate:** B02 is accepted, its shared deadline is still authoritative, and the B03 report identifies the exact accepted B02 source and evidence used as the starting point.

### 2. Add one validated edge setting and resilient storage

- Add shared Desktop settings types outside feature-specific metric/provider packages: a two-value `BarEdge` (`Top`, `Bottom`), immutable settings containing only that value, and a narrow settings store contract. Do not introduce a generic repository or result abstraction.
- Store `%LocalAppData%\SystemResourceMonitor\settings.json` as a JSON object with one required string property, for example `{"edge":"Top"}`. Reject invalid JSON, a missing/non-string edge, and values other than `Top` or `Bottom`. Missing file means defaults and is not an error. Ignore unknown properties so a future additive field does not invalidate the current setting.
- Acquire the B02 single-instance guard synchronously before the first settings operation. Then load settings off the WPF thread before creating and registering the bar. Refactor startup to an observed asynchronous path if needed; route every exception into the existing handled-startup/shutdown path and never use `async void` except framework event overrides that catch all failures.
- Return a concrete load outcome containing effective settings and an optional `ReadFailed`, `Malformed`, or `BackupFailed` issue. Read failure uses Top. Malformed content uses Top and is moved, byte-for-byte, to `settings.invalid-<UTC timestamp>.json` in the same directory when possible. A backup collision gets a deterministic numeric suffix. Backup failure leaves the original file untouched and still returns Top.
- Save through a uniquely named temporary file in the same directory. Write and flush the complete UTF-8 JSON before committing it with a same-directory atomic move for a new destination or atomic replacement for an existing destination. Never delete or truncate the last valid destination first. On any failure, retain the last valid file when one exists, remove only the owned temporary file on a best-effort basis, and return a concrete write failure.
- Keep all settings file work off the WPF thread. Serialize writes so rapid position actions cannot let an older completion overwrite a newer choice. Production has no command-line, environment, or configuration switch for changing the settings path or injecting failure; tests and the test host supply those dependencies only from their composition roots.
- Test with real temporary directories for missing, Top, Bottom, malformed, backup preservation, atomic replacement, and failed replacement retaining the previous valid file. Use a narrow file-operation seam only for otherwise impractical read, backup, and write failures.

**Gate:** missing settings select Top; valid Top and Bottom files round-trip; malformed bytes are preserved when backup succeeds; injected read, backup, and write failures return explicit outcomes without escaping startup or destroying the last valid file.

### 3. Add the native tray lifecycle

- Implement the tray through `Shell_NotifyIcon`, not Windows Forms. Keep its HWND callback, command IDs, icon handle, and native menu ownership in one Desktop tray component composed by `App.xaml.cs`/`DesktopStartup` and called only on the owning WPF dispatcher.
- After the bar is registered and positioned, call `NIM_ADD`, then `NIM_SETVERSION` with `NOTIFYICON_VERSION_4`; check both results. The menu contains only a Position submenu with checked Top and Bottom actions, plus Exit. Check marks always reflect the currently applied edge, not the last persisted edge.
- Top and Bottom commands call the single placement operation from step 4. Exit calls B02's shutdown coordinator; it does not close the window or call `ABM_REMOVE` independently. Shutdown removes the tray icon idempotently on the UI thread while preserving B02's invariant that AppBar removal occurs before the bounded background-work wait.
- Show recoverable settings and shell failures without activating the bar. A failed save reports exactly that the current position applies only until restart. Use the tray notification/status path when the icon exists; if tray creation itself fails, propagate a failed shell-integration result to the lifecycle reporter rather than treating recovery as successful.
- Displaying and selecting the tray menu must not call `Activate`/`Focus` on the bar, make the bar the foreground window, or remove `SWP_NOACTIVATE`. Per [ADR 0008](../../adr/0008-tray-menu-foreground-owner.md), only a hidden native menu owner may become foreground while the menu is open; it posts `WM_NULL` after tracking and returns foreground to the previous window unless the user activated another one. If foreground is refused, the menu still opens. Preserve the foreground application through position changes.

**Gate:** the tray can select Top, select Bottom, and exit; its checks follow the applied edge; a write failure leaves the selected edge applied and visibly reports the restart-only limitation; no tray action focuses the bar.

### 4. Make edge changes one placement transaction

- Extend B02's AppBar placement code to accept `BarEdge`. For every placement, obtain the current primary monitor and current window DPI, convert the fixed 24-DIP thickness to physical pixels, initialize the proposal from that monitor's full `rcMonitor`, call `ABM_QUERYPOS`, restore the requested thickness on the selected edge, call `ABM_SETPOS`, validate the returned rectangle, and apply it with `SetWindowPos(..., SWP_NOACTIVATE)`.
- For Top, set `bottom = top + thickness` after `ABM_QUERYPOS`; for Bottom, set `top = bottom - thickness`. Never position from WPF DIP properties or independently offset for the Windows taskbar; the shell-negotiated rectangle is authoritative, including an auto-hidden taskbar.
- Run the transaction on the window's dispatcher. A normal edge change does not call `ABM_REMOVE` or `ABM_NEW`. Update the applied edge and tray checks only after `ABM_SETPOS` and `SetWindowPos` both succeed. Persist only an applied edge; a placement failure leaves the previous applied and persisted values unchanged and reports the failure.
- Complete one save at a time in selection order. The placement remains usable while a save is pending, but the last successfully requested edge must be the last value offered to storage. A save failure does not roll back the live AppBar.
- Factor rectangle calculation into a pure, allocation-free helper and cover Top/Bottom, non-zero and negative monitor origins, and 100/150/200-percent DPI. Keep native calls in the existing platform wrapper and check structure sizes, callback lifetimes, return values, and pointer widths.

**Gate:** ten consecutive Top/Bottom switches use one AppBar registration, leave one reservation at the final edge, keep the foreground application unchanged, and persist the final edge when storage succeeds.

### 5. Reconcile shell and display changes

- Register `TaskbarCreated` once for the process lifetime and handle it in the bar's HWND hook. Also handle `ABN_POSCHANGED`, `ABN_STATECHANGE`, `WM_DISPLAYCHANGE`, and `WM_DPICHANGED` there. Leave `ABN_FULLSCREENAPP` as a B04 trigger with no B03 fullscreen state or z-order policy.
- Funnel every trigger through one dispatcher-owned reconciliation coordinator. Coalesce a burst into one pending pass; if another trigger arrives during a pass, run at most one follow-up pass using fresh primary-monitor geometry and DPI. Do not recursively negotiate from AppBar notifications.
- For position/state/display/DPI reconciliation, keep the existing registration and rerun the step-4 placement transaction. On `WM_DPICHANGED`, do not accept the suggested WPF rectangle as final; recompute 24 DIPs at the new DPI and renegotiate with the shell.
- On a primary-monitor change, use current primary-monitor geometry rather than cached coordinates, negotiate the selected edge on the new primary monitor, and verify the previous monitor's work area returns to its baseline. Keep the persisted edge unchanged.
- On `TaskbarCreated`, assume Explorer lost both shell resources. On the owning WPF thread, call `ABM_NEW` with the existing callback message, then `ABM_QUERYPOS`/`ABM_SETPOS` and `SetWindowPos`, then recreate the icon with `NIM_ADD` followed by `NIM_SETVERSION`. Do not call `ABM_REMOVE` as part of successful recovery. Keep separate AppBar and tray results so a partial failure is observable; never mark recovery healthy merely because the message handler returned.
- Recreate native menu/icon resources without duplicating managed handlers. Repeated placement notifications and a later Explorer restart must still leave one logical AppBar, one tray icon, and one shutdown path.
- Preserve B02's focus behavior on every reconciliation. B04 will add fullscreen state reconciliation after this shell-recovery sequence; do not set z-order beyond the existing normal AppBar positioning.

**Gate:** successful Explorer recovery has the order `ABM_NEW` → `ABM_QUERYPOS`/`ABM_SETPOS` → `NIM_ADD` → `NIM_SETVERSION`; display, DPI, taskbar-state, and primary-monitor changes converge on one correctly sized reservation with no stale old-monitor work area.

### 6. Automate failure and state-transition coverage

- Add permanent settings tests for the behaviors in step 2. Assert externally visible outcomes and preserved bytes; do not pin serializer implementation details or merely echo fake calls.
- Add lifecycle tests proving guard acquisition precedes settings load; settings failures still reach a positioned, controllable bar; successful placement precedes save; failed placement is not persisted; failed save retains the live edge and emits the restart-only message; and Exit uses the shared shutdown coordinator.
- Add AppBar/tray state tests for ten alternating edge changes without `ABM_NEW`/`ABM_REMOVE`, notification coalescing, DPI renegotiation, primary-monitor replacement, repeated callbacks, Explorer recovery order, partial recovery failure, idempotent tray deletion, and no B04 fullscreen transition.
- Extend the separate test host, not the published app, with isolated settings roots and read/backup/write fault modes. Each mode composes the production startup, placement, tray, and shutdown components with a real HWND/AppBar, proves the bar remains controllable after injection, records the applied edge and sanitized issue code, restores the baseline work area, and exits without leftovers. Distinct nonzero exit codes identify usage and assertion failures.
- Keep real shell integration checks separate from deterministic tests. Fakes can prove ordering and failure propagation; only Windows can prove work-area, Explorer, tray, DPI, taskbar, monitor, and focus behavior.

**Gate:** the focused automated suite passes, every test-host fault mode exits 0 after restoring work area, and no fault hook or alternate settings path is reachable from the published executable.

### 7. Verify the published app and record evidence

Run from the repository root on Windows 11 x64. Use the Desktop project path present in the accepted B02 tree; after the resource-package layout cutover it is `src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj`.

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

- Build the published app and test host from one clean commit with no edits between builds. Record full commit/tree IDs and SHA-256 for both executables and the production assemblies beside each; production assembly hashes must match before test-host evidence is attributed to the published code. Confirm the published folder contains no test host, fixtures, failure switches, or alternate settings configuration.
- With no settings file, launch the exact published executable and verify Top. Select Bottom, exit through the tray, relaunch, and verify Bottom. Repeat for Top. Preserve and restore any pre-existing user settings before and after the run.
- Put malformed non-secret bytes in `settings.json`, launch, and verify Top plus a byte-identical `settings.invalid-<timestamp>.json`. Run the test-host read, backup, and write fault modes; for write failure confirm the live edge changes, the restart-only message appears, the previous valid file remains valid, and position/Exit remain usable.
- Alternate Top and Bottom ten times. After each action record the selected edge, bar rectangle, primary-monitor work area, foreground-window result, and absence of an old-edge reservation. Exit and require every work area to equal its baseline and no Desktop process to remain.
- Restart Explorer while the bar runs. Verify the strip is reserved again, exactly one tray icon returns, Position still moves the bar, Exit still restores work area, and failed native recovery is not falsely reported as success. Rerun with repeated AppBar position notifications where practical.
- At 100%, 150%, and 200% scaling, change resolution and DPI while the bar runs. Require the rendered and reserved strip to remain 24 DIPs at the current DPI with matching physical rectangles. With two monitors, change which monitor is primary in both directions; require the bar and reservation on the new primary and baseline work area on the former primary.
- Enable and disable Windows taskbar auto-hide with the bar at Top and Bottom. Record negotiated rectangles and before/during/after work areas; no stale reservation may remain. Do not infer correctness solely from the bar's visible location.
- Use `SetWinEventHook` for `EVENT_SYSTEM_FOREGROUND` and `EVENT_OBJECT_FOCUS` while opening the tray, selecting both edges, changing display settings, restarting Explorer, and using another application. Only the hidden tray-menu owner and its menu may receive events, and only while a real tray click has the menu open; no event may target the visible bar, and no other scenario may target the Desktop process. After a dismissed menu, the prior application must regain foreground. Verify that both an outside click and Escape dismiss the menu. Describe the other application by kind, not process or window title.
- Rerun B02's second-process, normal shutdown, and handled-startup-failure checks. Record the shared placement deadline. B03 does not reset or extend it.
- Create a dated sanitized report under `docs/acceptance/` with separate automated, Windows integration, and manual desktop sections. Include commands and exit codes, source/artifact identities, sanitized display/taskbar configuration, settings outcomes, fault codes, recovery call/result sequence, rectangles/work areas, focus-event counts, and pass/fail for every B03 criterion. Mark any unavailable desktop scenario `unverified`; do not close B03 or unblock B04 from tests alone.

**Gate:** every B03 acceptance criterion has direct evidence, all B02 regression checks still pass, and the report contains no credentials, usernames, absolute user paths, monitor serials, process/window titles, precise activity timeline, or unredacted screenshots.

## Boundary for B04

B04 owns `ABN_FULLSCREENAPP`, the 500-ms foreground-window reconciliation, registered-to-lowered-to-restored state, primary-monitor fullscreen classification, and the final placement decision. B03 must hand off one dispatcher-owned placement/recovery path and must not lower the bar, unregister for fullscreen, or start a second shell-recovery mechanism. If the shared two-working-day deadline has passed with a blocking placement failure, stop dependent work and request the explicit placement-or-suspension decision required by the product plan; do not implement a non-reserving fallback. Record a new ADR for any new design choice with meaningful alternatives.
