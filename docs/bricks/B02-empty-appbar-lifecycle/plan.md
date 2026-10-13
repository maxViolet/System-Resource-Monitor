# B02 implementation plan for coding agents

Status: planned. This package implements [B02 — Empty AppBar lifecycle](../B02-empty-appbar-lifecycle.md). The [product placement gate](../../plan.md#1-validate-placement-first), [implementation plan](../../implementation-plan.md#settings-and-lifecycle), [ADR 0001](../../adr/0001-separate-windows-appbar.md), and [ADR 0002](../../adr/0002-appbar-first-cpu-ram-release.md) govern ambiguity. B01 must be complete before B02 implementation begins.

## Outcome and handoff

Deliver one empty, borderless, non-activating bar on the primary monitor that reserves one strip through AppBar. A second launch exits before any settings access or AppBar registration. Normal exit and a handled failure after registration restore the prior work area before any bounded wait. B03 can then add tray actions, edge settings, and shell/display recovery without replacing B02's lifecycle.

Do not mark B02 done from build or test results alone. The published Desktop app needs the named Windows 11 checks below, and the separate `SystemResourceMonitor.Desktop.TestHost` executable (the test host) needs real HWND/AppBar fault evidence.

## Work order

Complete these steps in order. Keep the solution buildable after each step. Do not add CPU/RAM collection, tray actions, persisted edge selection, or B03/B04 recovery behavior.

### 1. Establish the prerequisite and start the placement clock

- Verify B01's locked restore, Release build/tests, architecture checks, negative-probe evidence, and independent test-host smoke result in its acceptance report. If evidence is missing, record B02 as blocked on B01; do not treat untracked or merely compiling files as a completed prerequisite.
- At the first B02 implementation action, record the local start timestamp, timezone, and the resulting two-working-day deadline in the B02 acceptance report. This is one clock across B02–B04, not a fresh clock for each brick. Do not start it merely by writing this plan.
- Capture the target Windows 11 build, monitor arrangement, primary monitor, taskbar configuration, DPI, and work area before launch. Describe monitors by role, resolution, scale, and relative position, not by model or serial number.

**Gate:** the B01 prerequisite has evidence, and the shared placement deadline is dated before B02 code changes.

### 2. Acquire ownership before starting the bar

- Remove `StartupUri` from `App.xaml` so WPF creates no window on its own. At the start of `App.xaml.cs` startup, before any settings access, window creation, or `ABM_NEW`, acquire a named kernel mutex in the per-session namespace (`Local\SystemResourceMonitor-<fixed GUID>`) without waiting. Do not use `Global\`, because AppBar space and the work area belong to one session; do not use a lock file, because it would outlive an abrupt termination. Treat `AbandonedMutexException` as acquired ownership. If another process owns the mutex or opening it fails, exit promptly as a non-owner without constructing the bar or reading or writing settings. Hold ownership until shutdown finishes.
- Put the startup sequence in a Desktop class that `App.xaml.cs` composes: guard acquisition, then a settings-start step that is a no-op in B02 and later receives B03's settings load, then bar creation. A failed acquisition returns before the settings-start step. The test host supplies a recorder for these steps; the published app gets no diagnostic switches, recorders, or test fixtures.
- Add automated coverage: the owner records `guard-acquired` before `settings-start`, and a real second process records no `settings-start` and exits. Two in-process objects cannot prove process exclusivity.
- For the Windows integration check, place a sentinel file at `%LocalAppData%\SystemResourceMonitor\settings.json`, trace the second process's file-system activity (Process Monitor or ETW filtered to its PID), and require zero open, query, read, or write operations under that directory. The sentinel's SHA-256 must be unchanged.

**Gate:** a second real process exits as a non-owner with no `settings-start` event and no file-system access under the settings directory.

### 3. Register and position one empty AppBar

- Declare `PerMonitorV2` in `app.manifest` and keep `requestedExecutionLevel` at `asInvoker`.
- Create one WPF window on the current primary monitor with `WindowStyle=None`, `ResizeMode=NoResize`, `ShowActivated=false`, `ShowInTaskbar=false`, and `AllowsTransparency=false`. Call `WindowInteropHelper.EnsureHandle()` and add `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` to the existing extended style before the first `Show()`. Never call `Activate()` or `Focus()` on the bar.
- Choose one fixed logical thickness in DIPs and record it. Convert it once to physical pixels with `MulDiv(thicknessDip, GetDpiForWindow(hwnd), 96)` and use that value for negotiation and evidence.
- Keep all HWND, AppBar, positioning, message-handling, and monitor/DPI work on the window's owning WPF thread. There, in order: call `RegisterWindowMessage` with a stable name, reject a zero result, and keep the ID for the process lifetime; install and root the `HwndSource` hook, which branches on that ID and dispatches the `ABN_*` code in `wParam` (B02 handlers may be empty; B03 and B04 extend them); call `ABM_NEW` with the ID in `uCallbackMessage`.
- Negotiate the Top edge, the implementation plan's default; B03 adds edge selection. Start the proposed rectangle from the primary monitor's full `rcMonitor`, call `ABM_QUERYPOS`, then reset `bottom = top + thickness`, because `ABM_QUERYPOS` does not preserve the requested size. Call `ABM_SETPOS` and apply the rectangle it returns with `SetWindowPos(..., SWP_NOACTIVATE)`.
- Check native return values, `APPBARDATA` size, pointer widths, callback lifetime, and handle ownership, using `SafeHandle` where ownership applies. If registration or positioning fails, report failure and run an idempotent cleanup that calls `ABM_REMOVE`; step 4 folds it into the shutdown coordinator. Do not leave a partly registered bar, and do not silently replace AppBar with an overlay.
- Add focused native-wrapper/rectangle checks where they test a real conversion or failure path. Validate registration and work-area change against Windows on the target desktop; pure mocks cannot close this gate.

**Gate:** the published app, launched from a non-elevated shell, shrinks `SPI_GETWORKAREA` on the Top edge by exactly the computed thickness, and the bar's `GetWindowRect` equals the rectangle returned by `ABM_SETPOS`. A second launch leaves one Desktop process, one bar window, and no further work-area change.

### 4. Make shutdown and handled startup failure deterministic

- Set `ShutdownMode=OnExplicitShutdown`. `App.xaml.cs` owns process-lifetime cancellation and composes one idempotent shutdown coordinator on the UI dispatcher; do not add Generic Host. The bar's first `Closing` sets `e.Cancel = true` and posts the coordinator to the dispatcher, unless a final-close flag is already set. `Application.SessionEnding` and every handled startup or dispatcher failure enter the same coordinator and complete it through `ABM_REMOVE` before their handlers return. While the HWND still exists, the coordinator stops state publication, cancels future work, and calls `ABM_REMOVE` in a `finally` path. It then sets the final-close flag, calls `Close()`, waits once and asynchronously no more than **two seconds total** for tracked tasks, and calls `Application.Current.Shutdown(exitCode)`.
- If the deadline expires, record which tracked operation remains pending, reject its late result, do not dispose resources it still uses, and exit without awaiting it again. Observe completion/fault of any task that finishes while the process remains alive.
- B02 has no collectors: the production tracked-task set is empty and nothing publishes. The coordinator still accepts tracked tasks and gates publication, so the test host and B05 use the same path.
- Extend the test host to compose the production lifecycle with a real HWND/AppBar. Supply the post-`ABM_SETPOS` failure point and a never-completing, handle-free fake task only through the test host's composition root; Desktop's `App.xaml.cs` passes no fault hooks and reads no command-line argument, environment variable, or configuration to select a fault mode. Add test-host modes `--fail-after-setpos` and `--bounded-shutdown`. Each reads `SPI_GETWORKAREA` before registration, after `ABM_SETPOS`, immediately after `ABM_REMOVE` returns, and at exit, and emits ordered `remove`, `restored`, `wait-start`, and `exit` markers. Exit code 0 means the injection happened and every assertion passed; use distinct nonzero codes for usage errors and failed assertions.
- Cover repeated cleanup calls, failure before registration, failure after registration, and a pending task that completes late. Assert call order and deadline in automated tests; the real work-area result comes from the test-host modes. Abrupt process termination is outside this handled-failure claim.

**Gate:** both test-host modes exit 0; the work area read after `ABM_REMOVE` equals the baseline and `restored` precedes `wait-start`; the bounded-shutdown mode records the pending operation and exits within the deadline without a leftover process or late publication.

### 5. Verify on the target desktop and record the handoff

Run from the repository root on Windows 11 x64. [ADR 0005](../../adr/0005-project-level-runtime-and-testing-platform.md) explains why the build and test commands omit `-r`:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
dotnet publish src/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

- Build both artifacts from one clean commit with no edits in between, and record the full commit and tree IDs. Commit the acceptance report afterwards.
- Launch the exact published `SystemResourceMonitor.Desktop.exe` from a non-elevated shell. Read `SPI_GETWORKAREA` and count Desktop processes and their top-level windows before launch, while the bar runs, after a second launch, and after normal exit. Confirm the Desktop process token is not elevated (`GetTokenInformation` with `TokenElevation`) and that no UAC prompt appears.
- Record `EVENT_SYSTEM_FOREGROUND` and `EVENT_OBJECT_FOCUS` with `SetWinEventHook` while the bar launches and while you switch to and use another application. No event may target the bar window or the Desktop process.
- Trigger normal exit by sending `WM_CLOSE` to the bar window with `SendMessageTimeout`, then wait for the process to exit on its own. A forced kill (`Stop-Process`, Task Manager End task) does not count, because it bypasses the cleanup path. Pass when the work area equals the baseline and no Desktop process remains.
- Run `SystemResourceMonitor.Desktop.TestHost.exe --fail-after-setpos` and `--bounded-shutdown` on the same desktop, and record each exit code and marker sequence.
- Record SHA-256 for the published `SystemResourceMonitor.Desktop.exe`, `SystemResourceMonitor.Desktop.dll`, and `SystemResourceMonitor.Core.dll`, and for the test host's executable, its own assembly, and the `SystemResourceMonitor.Desktop.dll` and `SystemResourceMonitor.Core.dll` beside it. The production assembly hashes must match between the two folders; if they do not, record why and mark the test-host evidence `unverified` for the published code. Confirm the published folder contains no test host, fixtures, or test assemblies, and that launching it with the test-host mode arguments starts the normal bar.
- Create a dated report under `docs/acceptance/`, sanitized per [First-release acceptance gates](../../implementation-plan.md#first-release-acceptance-gates). Include commands with repository-relative paths and summarized results, commit and tree IDs, artifact hashes, Windows/display/DPI configuration, the placement start timestamp and shared deadline, the thickness in DIPs and pixels, before/during/after work-area and window rectangles, focus-event counts, test-host exit codes and marker sequences, and pass/fail for every B02 criterion. Describe the other application by kind, not by process or window title; record no per-scenario times or screenshots. Distinguish automated, Windows integration, and manual desktop evidence.
- If an agent cannot run a desktop check itself, mark it `unverified`, list its command and artifact hash, and leave B02 in progress for a person to run on the target desktop. Update the roadmap status only when every criterion has evidence.

**Gate:** every B02 acceptance criterion has direct evidence.

## Boundary for B03 and B04

B03 owns tray actions, persisted Top/Bottom edge selection, Explorer recovery, and display/DPI/primary-monitor changes. B04 owns fullscreen reconciliation and the final placement decision. Their scenarios remain required for the shared two-working-day placement gate, but they do not become B02 completion criteria. If a blocking AppBar problem remains at the deadline, stop dependent implementation and record the explicit placement decision required by the product plan and ADR 0002; do not assume a fallback. Record a new ADR for any new design choice with meaningful alternatives.
