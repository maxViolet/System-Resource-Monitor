# B03 acceptance report — 2026-10-14

Brick: [B03 — Tray, edge settings, and shell/display recovery](../bricks/B03-shell-position-recovery.md). Package plan: [B03 plan](../bricks/B03-shell-position-recovery/plan.md).

Status: in progress. Automated checks and the available single-monitor Windows integration checks pass. Explorer restart, multiple-monitor primary changes, 150%/200% DPI, resolution changes, taskbar auto-hide, tray-menu interaction, and focus instrumentation remain unverified, so B03 is not accepted and B04 is not unblocked by this report.

## Prerequisite and placement clock

[B02's acceptance report](2026-10-14-B02-empty-appbar-lifecycle.md) is accepted. Its accepted source is commit `b28960baf333dcb74551f09e005486efb04f37d9`; the second-process trace records zero settings-directory operations and closes B03's prerequisite. The shared placement investigation started on 2026-10-14 and retains the 2026-10-16 deadline recorded by B02.

## Implemented behavior

- Settings contain only `Top`/`Bottom`, default to Top, load off the WPF thread after guard acquisition, accept a UTF-8 BOM, preserve malformed bytes when possible, and commit a flushed same-directory temporary file with `File.Move(..., overwrite: true)` (`MoveFileEx` replace-existing). `File.Replace` was removed because its documented errors 1176/1177 can leave neither the old nor the new file under the settings name.
- Applied edge changes use the existing AppBar registration, fresh primary-monitor geometry and window DPI, shell negotiation, validated physical-pixel rectangles, and `SetWindowPos` with `SWP_NOACTIVATE`. Successful changes are persisted serially; write failure leaves the live edge in place and enters the restart-only notification path.
- A native `Shell_NotifyIcon` tray owns Position/Top/Bottom and Exit commands. Per [ADR 0008](../adr/0008-tray-menu-foreground-owner.md), its menu is tracked by a hidden owner window that takes foreground for the menu; the visible bar is never the owner. Shutdown removes AppBar first, then deletes the tray icon and owner idempotently, before the bounded wait.
- AppBar position/state notifications, `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, and `TaskbarCreated` enter one dispatcher-owned coalescing coordinator. `ABN_FULLSCREENAPP` produces no B03 transition. Explorer recovery preserves the required AppBar-register, position, tray-add, tray-version order and reports AppBar/tray results independently. A rejected `ABM_NEW` keeps the existing `ABM_REMOVE` responsibility; tray recreation deletes a surviving icon before `NIM_ADD`; a failed tray recovery enters failure shutdown so the reservation is released instead of leaving a bar without Exit.
- Startup does not create the bar when shutdown began while settings were loading.
- The separate test host owns isolated settings roots and read, malformed-backup, write, and ten-edge-switch modes. No production command-line, environment, or configuration fault switch was added.

## Source and artifacts

The implementation was built from clean commit `989a9619d194f9c3f7ac7871cf0b5fee55962304` and tree `817425fdf6027dc7bd28512fb2e2732206766b16`. Restore, build, test, and publish ran with no source edits between them. The production assemblies below match between the published folder and test-host folder.

| Artifact | SHA-256 |
|---|---|
| published `SystemResourceMonitor.Desktop.exe` | `2650c18c06d61514e275a0cfbf12603268d29402d6035ea1ae8a3811b0e0e504` |
| published `SystemResourceMonitor.Desktop.dll` | `236a44878086798f9012e1d8913668d5f849b1ed49119635634026e7e6a6a907` |
| published `SystemResourceMonitor.Core.dll` | `a11915bc7fd0a295a213517c87aed93274203711ce18cdc3dde56d090ee80d09` |
| test-host `SystemResourceMonitor.Desktop.TestHost.exe` | `43af227c899f06488f2921d4aba451783dae72c2a66039d454a7edc5782c4dfb` |
| test-host `SystemResourceMonitor.Desktop.TestHost.dll` | `496753c1fa46628d66e277a7122c65fd842fa32c0b42cb6f0409b14ada32c10c` |
| test-host `SystemResourceMonitor.Desktop.dll` | `236a44878086798f9012e1d8913668d5f849b1ed49119635634026e7e6a6a907` |
| test-host `SystemResourceMonitor.Core.dll` | `a11915bc7fd0a295a213517c87aed93274203711ce18cdc3dde56d090ee80d09` |

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
| `dotnet test --project tests/SystemResourceMonitor.Desktop.Tests/SystemResourceMonitor.Desktop.Tests.csproj -c Release` | 0 | 49/49 passed, including real HWND/AppBar lifecycle modes. |
| `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build` | 0 | 57/57 passed across Desktop and Core tests. |
| `dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true` | 0 | Self-contained published folder created. |
| `git diff --check origin/main...HEAD` | 0 | No whitespace errors. |

Permanent coverage includes valid/missing/malformed settings, a UTF-8 BOM, unknown additive properties, invalid edge shapes and values, byte-preserving malformed backup with collision suffix, read/backup/write failures, a failed commit retaining the last valid file, ordered saves, exact restart-only failure text, Top/Bottom geometry at 100%/150%/200% DPI and negative origins, ten edge changes without re-registration, failed placement rollback, fresh primary geometry and DPI per placement, message classification, notification coalescing, Explorer recovery order and partial failures including retained `ABM_REMOVE` responsibility, failed tray version cleanup, idempotent tray deletion, the tray-menu owner's foreground sequence and non-override of a user-chosen window, and the B02 shutdown regressions.

## Windows integration

### Published application

The exact published executable from commit `989a961` was launched with production settings paths. A private pre-existing settings directory, if present, was moved aside and restored afterwards; no settings contents or absolute paths are recorded.

| Scenario | Applied edge | Window rectangle | Work area with bar | Exit/restoration |
|---|---|---|---|---|
| Missing settings | Top | (0, 0, 1920, 24) | (0, 24, 1920, 1168) | Exit 0; baseline restored |
| Valid Bottom | Bottom | (0, 1144, 1920, 1168) | (0, 0, 1920, 1144) | Exit 0; baseline restored; settings hash unchanged |
| Valid Bottom with UTF-8 BOM | Bottom | (0, 1144, 1920, 1168) | (0, 0, 1920, 1144) | Exit 0; baseline restored |
| Valid Top | Top | (0, 0, 1920, 24) | (0, 24, 1920, 1168) | Exit 0; baseline restored |
| Malformed bytes | Top | (0, 0, 1920, 24) | (0, 24, 1920, 1168) | Exit 0; baseline restored; original moved to one byte-identical backup |

Every run created the 24-DIP/24-pixel reservation and processed normal `WM_CLOSE`; no Desktop process remained. Successful startup also exercised production AppBar registration, tray `NIM_ADD`, and `NIM_SETVERSION`, because tray creation failure is a handled fatal startup failure.

### Test host

- `--edge-switches` applied ten alternating changes through production placement against a real AppBar. The final Top window and reservation were `(0, 0, 1920, 24)` and `(0, 24, 1920, 1168)`; all ten saves completed in selection order, final persisted edge was Top, exit restored `(0, 0, 1920, 1168)`, and the mode exited 0.
- `--settings-read-failure`, `--settings-backup-failure`, and `--settings-write-failure` each pass in the automated lifecycle test. All reach a positioned controllable bar, switch to Bottom, restore the baseline, remove the tray, and exit 0. The write mode keeps the previous valid Top file, leaves live Bottom applied, and records `settings-save-restart-only` before shutdown.
- On the earlier build `8f5b806`, a direct `--settings-write-failure` run confirmed live Bottom at `(0, 1144, 1920, 1168)`, Bottom work area `(0, 0, 1920, 1144)`, the restart-only marker, AppBar removal before `tray-remove` and `wait-start`, baseline restoration, and exit 0. On `989a961` the same mode passes inside the automated lifecycle test.
- B02's handled-startup-failure, bounded-shutdown, second-process-before-settings, production second launch, and normal-close scenarios pass in the same 49-test Desktop run.

### Tray menu, published application

A probe posted the tray callback (`WM_CONTEXTMENU`, `NOTIFYICON_VERSION_4` packing) to the exact published bar. The menu opened, and the visible bar was never the foreground window before, during, or after it. The probe's `AllowSetForegroundWindow` grant was refused because the probe process could not itself set foreground, so the hidden owner did not become foreground. This exercised the ADR 0008 degraded path: the menu still opened, Escape did not reach it, foreground stayed with the prior application, and `WM_CLOSE` with the menu open exited 0 and restored `(0, 0, 1920, 1168)`. The owner-foreground path requires a real tray click, because Explorer grants foreground rights for it.

## Manual and unavailable desktop gates

| Scenario | Status |
|---|---|
| Open the real tray menu with a mouse click, select Top and Bottom, confirm checks, dismiss by outside click and by Escape, and Exit | Unverified; a synthetic callback opened the menu without focusing the bar (see above), but Explorer's foreground grant for a real click was not exercised. |
| Ten published-app tray selections with foreground/focus instrumentation | Unverified; per ADR 0008 only the hidden owner and its menu may receive events, and the prior application must regain foreground. |
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
