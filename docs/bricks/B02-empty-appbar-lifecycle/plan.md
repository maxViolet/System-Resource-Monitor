# B02 implementation plan for coding agents

Status: planned. This package implements [B02 — Empty AppBar lifecycle](../B02-empty-appbar-lifecycle.md). The [product placement gate](../../plan.md#1-validate-placement-first), [implementation plan](../../implementation-plan.md#settings-and-lifecycle), [ADR 0001](../../adr/0001-separate-windows-appbar.md), and [ADR 0002](../../adr/0002-appbar-first-cpu-ram-release.md) govern ambiguity. B01 must be complete before B02 implementation begins.

## Outcome and handoff

Deliver one empty, borderless, non-activating bar on the primary monitor that reserves one strip through AppBar. A second launch exits before any settings access or AppBar registration. Normal exit and a handled failure after registration restore the prior work area before any bounded wait. B03 can then add tray actions, edge settings, and shell/display recovery without replacing B02's lifecycle.

Do not mark B02 done from build or test results alone. The published Desktop app needs the named Windows 11 checks below, and the separate TestHost needs real HWND/AppBar fault evidence. Record automated, Windows integration, and manual desktop evidence separately under `docs/acceptance/`.

## Work order

Complete these steps in order. Keep the solution buildable after each step. Do not add CPU/RAM collection, tray actions, persisted edge selection, or B03/B04 recovery behavior.

### 1. Establish the prerequisite and start the placement clock

- Verify B01's locked restore, Release build/tests, architecture checks, negative-probe evidence, and independent TestHost smoke result in its acceptance report. If evidence is missing, record B02 as blocked on B01; do not treat untracked or merely compiling files as a completed prerequisite.
- At the first B02 implementation action, record the local start timestamp, timezone, and the resulting two-working-day deadline in the B02 acceptance report. This is one clock across B02–B04, not a fresh clock for each brick. Do not start it merely by writing this plan.
- Capture the target Windows 11 build, monitor arrangement, primary monitor, taskbar configuration, DPI, and work area before launch. Keep the report sanitized as required by the implementation plan.

**Gate:** the B01 prerequisite has evidence, and the shared placement deadline is dated before B02 code changes.

### 2. Acquire ownership before starting the bar

- Add a process-lifetime single-instance guard in Desktop. Acquire it at the start of `App.xaml.cs` startup, before any settings access, window creation, or `ABM_NEW`. Hold ownership until shutdown finishes. A failed acquisition exits promptly without constructing the bar or reading or writing settings.
- Keep settings persistence for B03. Provide a narrow observable seam in TestHost to prove the ordering if needed; do not add production diagnostic switches or test fixtures to the published app.
- Add automated coverage for first-owner/second-process behavior and guard-before-settings ordering. Use a real second process for the Windows integration check, since two in-process objects cannot prove process exclusivity.

**Gate:** a second launch leaves one owning process, one bar, and one reservation; a monitored settings location is unchanged by the second process.

### 3. Register and position one empty AppBar

- Declare `PerMonitorV2` in `app.manifest`. Create one thin, borderless, non-activating WPF window on the current primary monitor. Use a documented fixed thickness in physical pixels derived from that monitor's DPI; record the chosen thickness so the reserved and rendered rectangles can be compared.
- On the window's owning WPF thread, create the HWND, register a persistent `RegisterWindowMessage` callback, install the `HwndSource` hook, call `ABM_NEW`, then `ABM_QUERYPOS` and `ABM_SETPOS`. Apply the negotiated physical-pixel rectangle with `SetWindowPos(..., SWP_NOACTIVATE)`. Keep HWND access, AppBar calls, positioning, message handling, and monitor/DPI work on that thread.
- Check native return values, `APPBARDATA` size, pointer widths, callback lifetime, and ownership. If registration or positioning fails, report failure and run the same idempotent cleanup path; do not leave a partly registered bar. Do not silently replace AppBar with an overlay.
- Add focused native-wrapper/rectangle checks where they test a real conversion or failure path. Validate registration and work-area change against Windows on the target desktop; pure mocks cannot close this gate.

**Gate:** the published app reserves exactly one strip on the primary monitor without elevation; the rendered bar matches the negotiated rectangle and does not activate itself.

### 4. Make shutdown and handled startup failure deterministic

- Let `App.xaml.cs` own process-lifetime cancellation and shutdown. Stop state publication, cancel future work, remove the AppBar immediately in an idempotent `finally` path on the window thread, then wait no more than **two seconds total** for background work. The removal must occur before the wait. Reject late results and observe any task completion/fault that occurs while the process remains alive. Do not dispose resources still used by pending work.
- Extend the separate Desktop.TestHost to compose the production lifecycle with a real HWND/AppBar. Add a failure point after successful `ABM_SETPOS`, plus a never-completing, handle-free fake source to exercise the total shutdown deadline. Keep the injection code and fake out of the published Desktop artifact.
- Cover repeated cleanup calls, failure before registration, failure after registration, and a pending task that completes late. Assert call order and deadline in automated tests; use Windows work-area observations for the actual removal result. Abrupt process termination is outside this handled-failure claim.

**Gate:** normal exit and TestHost's handled startup failure restore the prior work area before any wait; the bounded-wait path exits without a leftover process or late publication.

### 5. Verify on the target desktop and record the handoff

Run from the repository root on Windows 11 x64:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release -r win-x64 --no-restore
dotnet test SystemResourceMonitor.slnx -c Release -r win-x64 --no-build --no-restore
dotnet publish src/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

- On the exact published folder, record the work area before launch, while the empty bar is running, after second launch, and after normal exit. Confirm one process/window/reservation, no elevation prompt, no keyboard-focus theft while switching to and interacting with another application, and no background process after exit.
- On the separate TestHost build from the same source identity, inject the post-registration handled failure and bounded-wait case. Record work-area restoration and process exit. Record the published and TestHost executable SHA-256 values and verify the published folder contains no TestHost executable, fixtures, or production fault-injection switch.
- Create a dated sanitized report under `docs/acceptance/` with commands and results, source commit or working-tree identity, artifact hashes, Windows/display/DPI configuration, timestamps and shared deadline, before/during/after work-area rectangles, focus observation, and pass/fail for every B02 criterion. Mark unrun checks `unverified`; distinguish automated, Windows integration, and manual desktop evidence. Update the roadmap status only when its evidence supports the new state.

**Gate:** every B02 acceptance criterion has direct evidence. Build success does not stand in for work-area, focus, or fault-host observations.

## Boundary for B03 and B04

B03 owns tray actions, persisted Top/Bottom edge selection, Explorer recovery, and display/DPI/primary-monitor changes. B04 owns fullscreen reconciliation and the final placement decision. Their scenarios remain required for the shared two-working-day placement gate, but they do not become B02 completion criteria. If a blocking AppBar problem remains at the deadline, stop dependent implementation and record the explicit placement decision required by the product plan and ADR 0002; do not assume a fallback. Record a new ADR for any new design choice with meaningful alternatives.
