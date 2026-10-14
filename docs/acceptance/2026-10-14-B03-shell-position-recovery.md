# B03 acceptance report — 2026-10-14

Brick: [B03 — Tray, edge settings, and shell/display recovery](../bricks/B03-shell-position-recovery.md). Package plan: [B03 plan](../bricks/B03-shell-position-recovery/plan.md).

Status: in progress. Automated checks and the available single-monitor Windows integration checks pass. Explorer restart, multiple-monitor primary changes, 150%/200% DPI, resolution changes, taskbar auto-hide, tray-menu interaction, and focus instrumentation remain unverified, so B03 is not accepted and B04 is not unblocked by this report.

## Prerequisite and placement clock

[B02's acceptance report](2026-10-14-B02-empty-appbar-lifecycle.md) is accepted. Its accepted source is commit `b28960baf333dcb74551f09e005486efb04f37d9`; the second-process trace records zero settings-directory operations and closes B03's prerequisite. The shared placement investigation started on 2026-10-14 and retains the 2026-10-16 deadline recorded by B02.

## Implemented behavior

- Settings contain only `Top`/`Bottom`, default to Top, load off the WPF thread after guard acquisition, preserve malformed bytes when possible, and atomically replace the destination from a flushed same-directory temporary file.
- Applied edge changes use the existing AppBar registration, fresh primary-monitor geometry and window DPI, shell negotiation, validated physical-pixel rectangles, and `SetWindowPos` with `SWP_NOACTIVATE`. Successful changes are persisted serially; write failure leaves the live edge in place and enters the restart-only notification path.
- A native `Shell_NotifyIcon` tray owns Position/Top/Bottom and Exit commands. Shutdown removes AppBar first, then deletes the tray icon idempotently, before the bounded wait.
- AppBar position/state notifications, `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, and `TaskbarCreated` enter one dispatcher-owned coalescing coordinator. `ABN_FULLSCREENAPP` produces no B03 transition. Explorer recovery preserves the required AppBar-register, position, tray-add, tray-version order and reports AppBar/tray results independently.
- The separate test host owns isolated settings roots and read, malformed-backup, write, and ten-edge-switch modes. No production command-line, environment, or configuration fault switch was added.

## Source and artifacts

The implementation was built from clean commit `8f5b806225cc22796d3a673d6eee197691f3a1f3` and tree `76e4dd2d2b22c02304eae758cf293c62d4c26eea`. Restore, build, test, and publish ran with no source edits between them. The production assemblies below match between the published folder and test-host folder.

| Artifact | SHA-256 |
|---|---|
| published `SystemResourceMonitor.Desktop.exe` | `d8b9a219c92c8b63f310c702f63add7f55c6e2d078d8118c3dea3adc12401725` |
| published `SystemResourceMonitor.Desktop.dll` | `19e70bd1f2d17756f1cb4f824771d65a956d98639ffa1591014c5a82f6981800` |
| published `SystemResourceMonitor.Core.dll` | `98daecfdf4ef3f9f0ff689de92c06d98ba72c8e1cef1a6253fa398203164672d` |
| test-host `SystemResourceMonitor.Desktop.TestHost.exe` | `36dcc3ffa69192c2bc980d1844aab61102aa1188af291f328e13e563fbc17731` |
| test-host `SystemResourceMonitor.Desktop.TestHost.dll` | `d0c8c53e9a5e016a8ea4a57ecc63f84edb95a9536d6ff8365915231127f76e8c` |
| test-host `SystemResourceMonitor.Desktop.dll` | `19e70bd1f2d17756f1cb4f824771d65a956d98639ffa1591014c5a82f6981800` |
| test-host `SystemResourceMonitor.Core.dll` | `98daecfdf4ef3f9f0ff689de92c06d98ba72c8e1cef1a6253fa398203164672d` |

The self-contained published folder contains 402 files. No file name matches `testhost`, `tests`, `xunit`, `fixture`, or `testing`.

## Target desktop

| | |
|---|---|
| Windows | Windows 11 x64, build family 10.0.26300 |
| Monitors | One primary monitor, 1920×1200 physical pixels at origin (0, 0) |
| Scale | 100%, 96 DPI |
| Baseline work area | (0, 0, 1920, 1168) |
| Available scope | Single-monitor, current resolution/DPI, taskbar not varied during evidence collection |

## Automated checks

The commands below replace the private absolute path of the locally installed .NET 10.0.401 SDK with `dotnet`; arguments are unchanged.

| Command | Exit code | Result |
|---|---:|---|
| `dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode` | 0 | All six projects restored from their lock files. |
| `dotnet build SystemResourceMonitor.slnx -c Release --no-restore` | 0 | No warnings or errors. |
| `dotnet test --project tests/SystemResourceMonitor.Desktop.Tests/SystemResourceMonitor.Desktop.Tests.csproj -c Release --no-build` | 0 | 44/44 passed, including real HWND/AppBar lifecycle modes. |
| `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build` | 0 | 52/52 passed across Desktop and Core tests. |
| `dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true` | 0 | Self-contained published folder created. |
| `git diff --check origin/main...HEAD` | 0 | No whitespace errors. |

Permanent coverage includes valid/missing/malformed settings, unknown additive properties, invalid edge shapes and values, byte-preserving malformed backup with collision suffix, read/backup/write failures, atomic replacement retaining the last valid file, ordered saves, exact restart-only failure text, Top/Bottom geometry at 100%/150%/200% DPI and negative origins, ten edge changes without re-registration, failed placement rollback, fresh primary geometry, message classification, notification coalescing, Explorer recovery order and partial failures, failed tray version cleanup, idempotent tray deletion, and the B02 shutdown regressions.

## Windows integration

### Published application

The exact published executable was launched with production settings paths. A private pre-existing setting, if present, was preserved and restored; no settings contents or absolute paths are recorded.

| Scenario | Applied edge | Window rectangle | Work area with bar | Exit/restoration |
|---|---|---|---|---|
| Missing settings | Top | (0, 0, 1920, 24) | (0, 24, 1920, 1168) | Exit 0; baseline restored |
| Valid Bottom | Bottom | (0, 1144, 1920, 1168) | (0, 0, 1920, 1144) | Exit 0; baseline restored; settings hash unchanged |
| Valid Top | Top | (0, 0, 1920, 24) | (0, 24, 1920, 1168) | Exit 0; baseline restored |
| Malformed bytes | Top | (0, 0, 1920, 24) | (0, 24, 1920, 1168) | Exit 0; baseline restored; backup byte-identical |

Every run created the 24-DIP/24-pixel reservation and processed normal `WM_CLOSE`; no Desktop process remained. Successful startup also exercised production AppBar registration, tray `NIM_ADD`, and `NIM_SETVERSION`, because tray creation failure is a handled fatal startup failure.

### Test host

- `--edge-switches` applied ten alternating changes through production placement against a real AppBar. The final Top window and reservation were `(0, 0, 1920, 24)` and `(0, 24, 1920, 1168)`; all ten saves completed in selection order, final persisted edge was Top, exit restored `(0, 0, 1920, 1168)`, and the mode exited 0.
- `--settings-read-failure`, `--settings-backup-failure`, and `--settings-write-failure` each pass in the automated lifecycle test. All reach a positioned controllable bar, switch to Bottom, restore the baseline, remove the tray, and exit 0. The write mode keeps the previous valid Top file, leaves live Bottom applied, and records `settings-save-restart-only` before shutdown.
- A direct `--settings-write-failure` smoke run confirmed live Bottom at `(0, 1144, 1920, 1168)`, Bottom work area `(0, 0, 1920, 1144)`, the restart-only marker, AppBar removal before `tray-remove` and `wait-start`, baseline restoration, and exit 0.
- B02's handled-startup-failure, bounded-shutdown, second-process-before-settings, production second launch, and normal-close scenarios pass in the same 44-test Desktop run.

## Manual and unavailable desktop gates

| Scenario | Status |
|---|---|
| Open the real tray menu, select Top and Bottom, confirm checks, and Exit | Unverified; position and Exit callbacks are automated, but the visible menu interaction was not performed. |
| Ten published-app tray selections with foreground/focus instrumentation | Unverified; the test host proved ten real placement transactions, not user tray interaction or focus events. |
| Explorer restart and repeated post-restart AppBar notifications | Unverified; deterministic recovery order and partial failure pass, but Explorer was not restarted. |
| Resolution changes and 150%/200% DPI | Unverified; pure physical-pixel geometry passes, but the desktop was available only at 100%. |
| Primary-monitor changes in both directions | Unverified; only one monitor was available. |
| Enable/disable taskbar auto-hide at Top and Bottom | Unverified; taskbar configuration was not changed. |
| Failed-save notification visibly states the restart-only limitation | Unverified visually; the production status path and exact message pass automated and test-host checks. |
| B02 focus-event regression while using the tray and recovery paths | Unverified; no new `SetWinEventHook` run was performed. |

## Acceptance criteria

| Criterion | Status |
|---|---|
| Ten consecutive Top/Bottom switches leave exactly one reservation at the selected edge | Passed in the real-AppBar test host; published tray interaction remains unverified. |
| Valid settings survive restart; missing settings select Top | Passed with the published app. |
| Malformed settings are preserved when possible and the bar still starts | Passed with byte-identical published-app evidence. |
| Injected read, backup, and write failures do not prevent control; write failure produces restart-only handling | Passed in the test host; visible notification text remains unverified. |
| Explorer restart restores the reserved strip and working tray | Unverified. |
| Resolution, DPI, and primary-monitor changes converge without a stale old reservation | Unverified on the desktop. |
| Auto-hidden Windows taskbar behavior leaves no stale reservation | Unverified. |

B03 remains open until every unverified Windows desktop gate has direct evidence from clean, matching published and test-host artifacts.
