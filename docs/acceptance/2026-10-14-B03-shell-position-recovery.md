# B03 acceptance report — 2026-10-14

Brick: [B03 — Tray, edge settings, and shell/display recovery](../bricks/B03-shell-position-recovery.md). Package plan: [B03 plan](../bricks/B03-shell-position-recovery/plan.md).

Status: in progress. Automated checks and the available single-monitor Windows integration checks pass. Explorer restart, multiple-monitor primary changes, 150%/200% DPI, resolution changes, taskbar auto-hide, tray-menu interaction, and focus instrumentation remain unverified, so B03 is not accepted and B04 is not unblocked by this report.

## Prerequisite and placement clock

[B02's acceptance report](2026-10-14-B02-empty-appbar-lifecycle.md) is accepted. Its accepted source is commit `b28960baf333dcb74551f09e005486efb04f37d9`; the second-process trace records zero settings-directory operations and closes B03's prerequisite. The shared placement investigation started on 2026-10-14 and retains the 2026-10-16 deadline recorded by B02.

## Implemented behavior

- Settings contain only `Top`/`Bottom`, default to Top, load off the WPF thread after guard acquisition, accept a UTF-8 BOM, preserve malformed bytes when possible, and commit a flushed same-directory temporary file with `File.Move(..., overwrite: true)` (`MoveFileEx` replace-existing). `File.Replace` was removed because its documented errors 1176/1177 can leave neither the old nor the new file under the settings name.
- Applied edge changes use the existing AppBar registration, fresh primary-monitor geometry and window DPI, shell negotiation, validated physical-pixel rectangles, and `SetWindowPos` with `SWP_NOACTIVATE`. Successful changes are persisted serially; write failure leaves the live edge in place and enters the restart-only notification path.
- A native `Shell_NotifyIcon` tray owns Position/Top/Bottom and Exit commands. Per [ADR 0008](../adr/0008-tray-menu-foreground-owner.md), its menu is tracked by a hidden owner window that takes foreground for the menu; the visible bar is never the owner. Shell recovery is deferred while the menu's modal loop runs, and shutdown inside that loop calls `EndMenu` first. Shutdown removes AppBar first, then deletes the tray icon and owner idempotently, before the bounded wait.
- AppBar position/state notifications, `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, and `TaskbarCreated` enter one dispatcher-owned coalescing coordinator. `ABN_FULLSCREENAPP` produces no B03 transition. Explorer recovery preserves the required AppBar-register, position, tray-add, tray-version order and reports AppBar/tray results independently. Per [ADR 0009](../adr/0009-exit-when-tray-recovery-fails.md), a rejected `ABM_NEW` is followed by a best-effort `ABM_REMOVE` and one more `ABM_NEW`. A second rejection is reported as a failure, and shutdown keeps its `ABM_REMOVE` responsibility. Tray recreation deletes a surviving icon before `NIM_ADD`. A failed tray recovery enters failure shutdown, so the reservation is released instead of leaving a bar without Exit.
- Startup does not create the bar when shutdown began while settings were loading. Without that guard, the new test-host mode reproduced a bar positioned after shutdown with a stale `(0, 24, 1920, 1168)` reservation at exit.
- The separate test host owns isolated settings roots and read, malformed-backup, write, ten-edge-switch, and shutdown-during-settings-load modes. No production command-line, environment, or configuration fault switch was added.

## Source and artifacts

The implementation was built from clean commit `04c3943a6e131d72e24149c7783f38afd191b00b` and tree `5e040d0853ad63648a58c08bbee4c8ce51ef9250`. Restore, build, test, and publish ran with no source edits between them. The production assemblies below match between the published folder and test-host folder.

| Artifact | SHA-256 |
|---|---|
| published `SystemResourceMonitor.Desktop.exe` | `3da081ffa1d775285eca73313f0c12a5e5e4cc06b7fe64cb8e74436eda5a4c0e` |
| published `SystemResourceMonitor.Desktop.dll` | `8b14da439e60751382e814350b047196aedcff3579ce299491708f5d76726ea6` |
| published `SystemResourceMonitor.Core.dll` | `93925fe37eae232aa67c54edb99577205f103c4b0f85491a6cf7fe90af81f7c5` |
| test-host `SystemResourceMonitor.Desktop.TestHost.exe` | `75916f19210d1783c82bbf24d8abc705c32d5d65638d7dc1f736efa00274906e` |
| test-host `SystemResourceMonitor.Desktop.TestHost.dll` | `e37e1112f373e2ad52516911f4018664f5d6a6f21c5c1c287a4fbb11d0cdae4b` |
| test-host `SystemResourceMonitor.Desktop.dll` | `8b14da439e60751382e814350b047196aedcff3579ce299491708f5d76726ea6` |
| test-host `SystemResourceMonitor.Core.dll` | `93925fe37eae232aa67c54edb99577205f103c4b0f85491a6cf7fe90af81f7c5` |

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
| `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build` | 0 | 68/68 passed across Desktop and Core tests, including real HWND/AppBar lifecycle modes. |
| `dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true --no-restore` | 0 | Self-contained published folder created. |
| `git diff --check` | 0 | No whitespace errors. |

Permanent coverage includes valid/missing/malformed settings, a UTF-8 BOM, unknown additive properties, invalid edge shapes and values, byte-preserving malformed backup with collision suffix, read/backup/write failures, a failed commit retaining the last valid file, ordered saves, exact restart-only failure text, Top/Bottom geometry at 100%/150%/200% DPI and negative origins, ten edge changes without re-registration, failed placement rollback, fresh primary geometry and DPI per placement, message classification, notification coalescing, Explorer recovery order and partial failures, recovery after a rejected `ABM_NEW` whose registration survived, retained `ABM_REMOVE` responsibility, the recovery decision for every AppBar/tray outcome, delete-before-add tray recreation, failed tray version cleanup, idempotent tray deletion, the tray-menu owner's foreground sequence, refusal and non-override behavior, owner removal during tracking without losing the chosen command, shutdown during the settings load, and the B02 shutdown regressions.

The lost-registration regression proves that an edge change fails without calling `ABM_SETPOS` while `ABM_NEW` remains rejected, preserves the prior applied edge, and succeeds only after re-registration.

## Windows integration

### Published application

The settings scenarios below ran against the exact published executable from commit `989a961`. Between that commit and `2769fca`, `Adapters/` and `Settings/` are unchanged, and `Lifecycle/` changed only to record `startup-skipped` when shutdown preempts startup. A private pre-existing settings directory, if present, was moved aside and restored afterwards; no settings contents or absolute paths are recorded.

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
- On the earlier build `8f5b806`, a direct `--settings-write-failure` run confirmed live Bottom at `(0, 1144, 1920, 1168)`, Bottom work area `(0, 0, 1920, 1144)`, the restart-only marker, AppBar removal before `tray-remove` and `wait-start`, baseline restoration, and exit 0. On `2769fca` the same mode passes inside the automated lifecycle test.
- `--shutdown-during-settings-load` requests shutdown while the load is pending and keeps the dispatcher alive through the bounded wait. The load completes inside the wait, startup records `startup-skipped` before `exit`, no bar is created, exit is 0, and the work area remains `(0, 0, 1920, 1168)`.
- B02's handled-startup-failure, bounded-shutdown, second-process-before-settings, production second launch, and normal-close scenarios pass in the same 68-test run.

### Tray menu, published application

On `04c3943`, a probe posted the tray callback (`WM_CONTEXTMENU`, `NOTIFYICON_VERSION_4` packing) to the exact published bar, then posted a second callback while the menu was open. The menu opened, no nested menu or crash followed, and the visible bar was never the foreground window. Posting `WM_CLOSE` while the menu's modal loop ran closed the menu, exited 0, restored `(0, 0, 1920, 1168)`, and left the foreground window unchanged. The owner-foreground path requires a real tray click, because Explorer grants foreground rights for it.

### AppBar registration probe

A probe created a hidden window and sent AppBar messages without ever calling `ABM_NEW`.

- `ABM_QUERYPOS` and `ABM_SETPOS` both returned TRUE, and the work area stayed `(0, 0, 1920, 1168)`.
- A first `ABM_NEW` returned TRUE, and a second `ABM_NEW` on the registered window returned FALSE.
- A second probe showed that `ABM_REMOVE` returns TRUE in every state: for a window that was never registered, a registered one, and one already removed.
- The final work area equalled the baseline in both probes.

So placement cannot prove that a registration exists, and a spurious `TaskbarCreated` makes `ABM_NEW` fail for a bar that is still registered. ADR 0009's remove-and-re-register step handles both, and so does the rule that placement after a lost registration retries `ABM_NEW` first. Shutdown's `ABM_REMOVE` cannot fail because a registration was lost.

Known limitation: if the user selects Top or Bottom while a real Explorer restart is deferred behind the open menu, the selection is applied before recovery. Per the probe, it is unreserved until then. The deferred recovery runs as soon as the menu closes and reserves the newly applied edge, so the user's choice is kept.

## Manual and unavailable desktop gates

| Scenario | Status |
|---|---|
| Open the real tray menu with a mouse click, select Top and Bottom, confirm checks, dismiss by outside click and by Escape, and Exit | Unverified; a synthetic callback opened the menu without focusing the bar (see above), but Explorer's foreground grant for a real click was not exercised. |
| Ten published-app tray selections with foreground/focus instrumentation | Unverified; per ADR 0008 only the taskbar, the hidden owner, and its menu may receive events, and the window that was foreground when the menu opened must regain foreground. |
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
