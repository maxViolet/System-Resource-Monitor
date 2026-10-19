# B002 acceptance report — 2026-10-14

Brick: [B002 — Empty AppBar lifecycle](../bricks/B02-empty-appbar-lifecycle.md). Package plan: [B002 plan](../bricks/B02-empty-appbar-lifecycle/plan.md).

Status: accepted. Every criterion has direct evidence below.

## Prerequisite

B001 is complete: [B001 acceptance report](2026-10-12-B01-solution-boundary.md) records the locked restore, Release build and tests, architecture checks, banned-API negative probes, and the independent test-host smoke run.

## Placement investigation clock

| | |
|---|---|
| Started | 2026-10-14 12:18 local time, Wednesday, UTC+05:00 (Windows time zone `Qyzylorda Standard Time`), at the first B002 implementation action |
| Shared deadline | 2026-10-16 12:18 local time, Friday, UTC+05:00 — two working days |
| Scope | One clock across B002, B003, and B004; B004 enforces it |

## Target desktop before launch

| | |
|---|---|
| Windows | Windows 11 Pro x64, build 10.0.26300.9711 |
| Logical processors | 22 |
| Monitors | One monitor, primary, 1920×1200 physical pixels at origin (0, 0) |
| Scale | 100% (96 DPI) |
| Taskbar | Bottom edge, not auto-hidden, 48 physical pixels |
| Work area (`SPI_GETWORKAREA`) | (0, 0, 1920, 1152) |

## Source and artifacts

| | |
|---|---|
| Source | Commit `b28960baf333dcb74551f09e005486efb04f37d9`, tree `272e47b1bedf6edf0e8824491a4b2d24eb337e4f`, on branch `FEAT-002-empty-appbar-lifecycle`. Build output under `src/` and `tests/` was deleted (`git clean -xfd -- src tests`) and both artifacts were rebuilt from that commit with no edits in between; the assemblies carry informational version `1.0.0+b28960baf333dcb74551f09e005486efb04f37d9`. |
| .NET SDK | 10.0.401 |
| Published app | `src/system-resources/SystemResourceMonitor.Desktop/bin/Release/net10.0-windows/win-x64/publish/`, self-contained, 259 files; none matches `testhost`, `tests`, `xunit`, or `testing` |
| Test host | `tests/SystemResourceMonitor.Desktop.TestHost/bin/Release/net10.0-windows/win-x64/` |
| Bar thickness | 24 DIPs = 24 physical pixels at 96 DPI (`MulDiv(24, GetDpiForWindow(hwnd), 96)`) |

| Artifact | SHA-256 |
|---|---|
| published `SystemResourceMonitor.Desktop.exe` | `865cc51f72b5034e398c1e774e6d01daaf73e35607cad74753ace7017a8cb5c4` |
| published `SystemResourceMonitor.Desktop.dll` | `cc5633787b0501e3a9a111afac288772ca7e43541e6e27fd3e03927ebfb51432` |
| published `SystemResourceMonitor.Core.dll` | `edc9f51ced08baaec8ff3e4da8df380c7375bfeabbb7749d6498b9a10eb9665c` |
| test host `SystemResourceMonitor.Desktop.TestHost.exe` | `936dc1749121a944679a1c4d42b4e441c615d28ea149a58339134626267a785d` |
| test host `SystemResourceMonitor.Desktop.TestHost.dll` | `21c5948079ff3ab2730a97da2a98b7848afc0728417677eb9c948dbd963d148a` |
| test host folder `SystemResourceMonitor.Desktop.dll` | `cc5633787b0501e3a9a111afac288772ca7e43541e6e27fd3e03927ebfb51432` |
| test host folder `SystemResourceMonitor.Core.dll` | `edc9f51ced08baaec8ff3e4da8df380c7375bfeabbb7749d6498b9a10eb9665c` |

The production assemblies are identical in both folders, so the test-host evidence applies to the published code.

## Automated checks

| Command | Exit code | Result |
|---|---|---|
| `dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode` | 0 | Lock files match; `Microsoft.Extensions.TimeProvider.Testing` added to Desktop.Tests. |
| `dotnet build SystemResourceMonitor.slnx -c Release --no-restore` | 0 | No warnings, no errors. |
| `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build` | 0 | 22 total, 22 succeeded. |
| `dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true` | 0 | Self-contained folder published. |
| `git diff --check HEAD~1 HEAD` | 0 | No whitespace errors in the commit. |

Desktop.Tests coverage added by B002:

- `ShutdownCoordinatorTests`, with fake time: AppBar removal before the request returns and before the wait; the exact two-second deadline with pending work recorded and its late result rejected; early completion of faulted work; repeated requests with the first exit code; failure before a bar exists; cancellation-callback failure and rejected `ABM_REMOVE` that still close, wait, and exit with failure.
- `NativeLayoutTests`: `RECT`, `APPBARDATA`, and `MONITORINFO` match the x64 sizes passed as `cbSize`.
- `LifecycleProcessTests`, against the real desktop: both test-host fault modes pass; an owner test host records `guard-acquired` before `settings-start`, and a second real process records `non-owner`, no `settings-start`, and exits 0; the Desktop executable reserves one Top strip, a second launch exits 0 without a second window or reservation change, and `WM_CLOSE` restores the baseline work area. These tests fail if a Desktop bar is already running.

## Windows integration: test host

Both modes compose the production `DesktopStartup`, `BarWindow`, and `ShutdownCoordinator` with a real HWND and AppBar. Markers are printed in order; `restored` is emitted only when `SPI_GETWORKAREA` read immediately after `ABM_REMOVE` returns equals the baseline.

| Mode | Exit code | Work area: before / with bar / after `ABM_REMOVE` / at exit | Marker sequence |
|---|---|---|---|
| `--fail-after-setpos` | 0 | (0, 0, 1920, 1152) / (0, 24, 1920, 1152) / (0, 0, 1920, 1152) / (0, 0, 1920, 1152) | `guard-acquired`, `settings-start`, `bar-positioned`, `inject-failure`, `startup-failed:InjectedStartupFailureException`, `remove`, `restored`, `wait-start`, `exit` |
| `--bounded-shutdown` | 0 | (0, 0, 1920, 1152) / (0, 24, 1920, 1152) / (0, 0, 1920, 1152) / (0, 0, 1920, 1152) | `guard-acquired`, `settings-start`, `bar-positioned`, `remove`, `restored`, `wait-start`, `pending:never-completing-source`, `pending:late-source`, `exit` |

In both modes the bar's `GetWindowRect` equals the `ABM_SETPOS` rectangle (0, 0, 1920, 24), and the window has `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`. The bounded-shutdown wait lasted 2015 ms against the 2000 ms deadline; the late source completed after exit and its publication was rejected. No test-host process remained.

## Windows integration: published app

An unelevated probe process (per-monitor DPI aware) launched the published `SystemResourceMonitor.Desktop.exe` directly, read `SPI_GETWORKAREA`, and counted Desktop processes and their visible top-level windows.

| Point | Work area | Desktop processes | Visible top-level windows |
|---|---|---|---|
| Before launch | (0, 0, 1920, 1152) | 0 | 0 |
| Bar running | (0, 24, 1920, 1152) | 1 | 1 |
| After a second launch (exit code 0) | (0, 24, 1920, 1152) | 1 | 1 |
| After `WM_CLOSE` via `SendMessageTimeout` (exit code 0) | (0, 0, 1920, 1152) | 0 | 0 |

- The bar window is (0, 0, 1920, 24); the Top edge shrank by exactly 24 px.
- `GetTokenInformation(TokenElevation)` reports the Desktop process token as not elevated; the manifest requests `asInvoker`, and the process started unattended, so no consent prompt was required.
- Settings sentinel: a sentinel `settings.json` was placed in the settings directory before launch. Its SHA-256 was unchanged after the second launch and at the end; the probe then removed the sentinel and the directory it had created.
- Second-process file-system trace: an elevated, Microsoft-signed Process Monitor 4.11 capture recorded 1,334 events for the second process and zero operations whose path was under `%LocalAppData%\SystemResourceMonitor\`. The second process exited 0, the sentinel SHA-256 remained unchanged, the owner exited through `WM_CLOSE`, the baseline work area returned, and no Desktop process remained. The traced self-contained executable was rebuilt from source commit `b28960baf333dcb74551f09e005486efb04f37d9`, reports product version `1.0.0+b28960baf333dcb74551f09e005486efb04f37d9`, and has SHA-256 `285bc60a888fc4c82c79917714a993a7440311b553e48520ee06cc43d94e4a94`. Process Monitor has SHA-256 `fc3af5317c707e0555ad6e7590ad65ceb5c5085b053b41221944ac3ca3492d9c`; the analyzed PML had SHA-256 `5a9afca3c86b068dd35b0bdb4b5b8ff2c30c48abb85dfbab93834732750d29e7` and was kept outside the repository.
- Launching the published executable with `--fail-after-setpos` or `--bounded-shutdown` started the normal bar: the work area was reserved, `WM_CLOSE` exited with code 0, and the baseline was restored.

### Focus

`SetWinEventHook` recorded `EVENT_SYSTEM_FOREGROUND` and `EVENT_OBJECT_FOCUS` while the bar launched, while a separate single-window GUI text-entry application took the foreground and received typed text, while the bar was clicked, and while typing continued. 9 events were recorded (2 foreground, 7 focus); 0 targeted the bar window or a Desktop process. The other application stayed in the foreground after the bar click and received all typed text.

An earlier build without a `WM_MOUSEACTIVATE` handler raised one foreground and one focus event for the bar when it was clicked, although the foreground window never changed. The bar now answers `WM_MOUSEACTIVATE` with `MA_NOACTIVATE`; the run above is from the fixed build.

## Acceptance criteria

| Criterion | Status |
|---|---|
| The empty published app reserves exactly one strip on the primary monitor without requesting elevation | Passed. |
| Opening a second process leaves one process, one bar, and one reservation, and does not touch settings files | Passed: process, window, reservation, sentinel-hash, guard-order, and elevated file-system-trace checks all passed; the trace recorded zero operations under the settings directory for the second process. |
| The bar never takes keyboard focus during launch or interaction with another application | Passed: 0 focus events for the bar or the Desktop process. |
| Normal exit restores the prior work area and leaves no background process | Passed. |
| A test-host injected handled startup failure restores the prior work area | Passed: `restored` precedes `wait-start`. |
| The placement investigation start timestamp is recorded so B004 can enforce the single two-working-day limit across B002–B004 | Passed: see [Placement investigation clock](#placement-investigation-clock). |
