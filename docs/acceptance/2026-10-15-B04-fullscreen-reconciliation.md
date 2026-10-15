# B04 fullscreen reconciliation report — 2026-10-15

Brick: [B04 — Fullscreen reconciliation and placement gate](../bricks/B04-placement-gate.md). Package plan: [B04 plan](../bricks/B04-placement-gate/plan.md).

Status: in progress. Deterministic coverage and the available single-monitor real-HWND smoke pass. B03 remains unaccepted, and the named published-app video, game, Explorer, display, and two-monitor scenarios remain unverified. B04 is not accepted and B05, B10, and B11 remain gated.

## Prerequisite and exception

[B02's 2026-10-14 report](2026-10-14-B02-empty-appbar-lifecycle.md) is the accepted lifecycle baseline. The shared placement investigation began at **2026-10-14 12:18 UTC+05:00** and reaches its two-working-day deadline at **2026-10-16 12:18 UTC+05:00**.

[B03's 2026-10-14 report](2026-10-14-B03-shell-position-recovery.md) remains in progress. Explorer restart, resolution changes, 150%/200% DPI, taskbar auto-hide, real tray-menu interaction, focus instrumentation, and the visible failed-save restart-only notification remain unrun. The current desktop also exposes only one 1920×1200 primary display at 100% scaling, so the required primary-monitor-change scenario cannot run. The implementation-order exception approved on 2026-10-15 permits B04 code and deterministic tests while retaining every B03 and B04 acceptance gate. It does not accept either brick or unblock dependent work.

## Implemented behavior

- A fresh Win32 foreground snapshot resolves the root top-level window, revalidates its handle, visibility, geometry, monitor geometry, and current-primary identity, and compares exact physical-pixel window bounds with `rcMonitor`. Invalid or changing observations are indeterminate; two consecutive stable observations with no foreground/on-monitor window establish normal state without reacting to a transient activation gap.
- The bar itself, canonical shell windows, Explorer `WorkerW`/`Progman` desktop hosts, invisible windows, fullscreen on another monitor, and windows bounded by the reduced work area classify as normal. `ABN_FULLSCREENAPP` requests a fresh observation; its payload is not retained as state.
- One dispatcher-owned reconciliation coordinator combines AppBar, display, Explorer, fullscreen-notification, and 500 ms periodic triggers. Placement and successful Explorer recovery complete before fullscreen classification.
- The z-order state machine lowers with `HWND_BOTTOM` and restores with `HWND_TOPMOST`, always using `SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE`. It changes z-order only after a conclusive state transition. Failed operations remain retryable; repeated and indeterminate observations make no state change.
- Fullscreen transitions do not call `ABM_NEW`, `ABM_REMOVE`, `ABM_QUERYPOS`, or `ABM_SETPOS`. Registration, edge, negotiated rectangle, and reserved work area remain unchanged. Explorer recovery invalidates the last z-order result and reapplies the state only after both AppBar and tray recovery succeed. A failed AppBar recovery pauses periodic z-order reconciliation until a later placement successfully re-registers the AppBar.
- The periodic trigger starts after AppBar and tray startup, remains active while lowered, and stops before AppBar removal during shutdown.

## Source and artifacts

The artifacts were built from the uncommitted B04 working tree based on commit `3fb765f1828f5d44b1c329457a83edab7a02844c`. They are verification artifacts, not clean-commit acceptance artifacts. The production Desktop assembly copied beside the test host matches the published assembly.

| Artifact | SHA-256 |
|---|---|
| published `SystemResourceMonitor.Desktop.exe` | `fc563923827f0ab0c74af8e808ccf16dca5fbc51b191199b211425c4acc2de05` |
| published `SystemResourceMonitor.Desktop.dll` | `446646e86d6a21aeeb21e0797c974efcbf095ccc8af3d74e482b104aab101a34` |
| published `SystemResourceMonitor.Core.dll` | `7f055fc5ed472950895c03bb072965e296bde44f3509a743a2d2991449e32e43` |
| test-host `SystemResourceMonitor.Desktop.TestHost.exe` | `9e18245c80b95257c700513e1bc01d2a6dd4dcd91fd518d6498742105809fcfb` |
| test-host `SystemResourceMonitor.Desktop.TestHost.dll` | `42b5eb43f608442bb328d517f3dce2c51808e824dd5f05971d55b78b3f9555ee` |
| test-host `SystemResourceMonitor.Desktop.dll` | `446646e86d6a21aeeb21e0797c974efcbf095ccc8af3d74e482b104aab101a34` |

The self-contained published folder contains 402 files. No file name matches `testhost`, `tests`, `xunit`, `fixture`, or `testing`.

## Automated checks

The commands used the locally installed .NET SDK 10.0.401 host.

| Command | Result |
|---|---|
| `dotnet test --project tests/SystemResourceMonitor.Desktop.Tests/SystemResourceMonitor.Desktop.Tests.csproj -c Release` | Passed, 83/83. |
| `dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode` | Passed; all six projects restored from lock files. |
| `dotnet build SystemResourceMonitor.slnx -c Release --no-restore` | Passed with 0 warnings and 0 errors. |
| `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build` | Passed, 91/91. |
| `dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true --no-restore` | Passed; self-contained folder produced. |
| `git diff --check` | Passed. |

Permanent coverage includes primary fullscreen, other-monitor fullscreen, maximized-window exclusion, bar/shell/Explorer-desktop/invisible/invalid observations, consecutive foreground absence, normal → lowered → restored transitions, repeated observations, failed transition retries, indeterminate observations, Explorer z-order invalidation, fullscreen-trigger burst coalescing, and tray-menu deferral. Ordered recovery tests prove AppBar placement and tray recreation precede z-order, failed AppBar recovery blocks fullscreen passes until re-registration, and tray failure enters shutdown without selecting z-order. The real-HWND lifecycle mode covers periodic lowering/restoration, repeated foreground-window switches while the fullscreen window remains alive, actual topmost-style transitions, stable reservation, and final work-area restoration.

## Windows integration smoke

The test-host `--fullscreen-reconciliation` mode exercised the production lifecycle with a real HWND and registered AppBar on Windows 11 x64. It kept a controlled window at full-primary-monitor bounds while switching foreground to a separate normal top-level window, switched back to the still-alive fullscreen window, departed again, and then exercised an ordinary maximized-window negative case.

| Observation | Result |
|---|---|
| Baseline work area | `(0, 0, 1920, 1168)` |
| AppBar rectangle | `(0, 0, 1920, 24)` |
| Work area with bar | `(0, 24, 1920, 1168)` |
| Fullscreen lowering | Passed; `Normal → Lowered` in 72 ms, with the bar's topmost style cleared. |
| Foreground-window restoration | Passed; `Lowered → Normal` in 322 ms after activating a separate normal window, with the fullscreen window still alive and unchanged. |
| Return and second departure | Passed; reactivating the unchanged fullscreen window lowered the bar again, and activating the normal window restored it again. |
| Focus | Passed; each transition retained the deliberately selected controlled foreground window. |
| Registration/reservation | Passed; AppBar rectangle and work area remained unchanged through both cycles. |
| Maximized negative case | Passed; no additional lowering or indeterminate observation, and the reservation remained unchanged. |
| Exit | Passed; work area restored to `(0, 0, 1920, 1168)` and the mode exited 0. |

This controlled borderless-window smoke is not evidence for fullscreen video, an exclusive game, or a real borderless game.

## Unverified desktop gates

- Ten enter/leave cycles each for fullscreen video, an exclusive game, and a borderless game, including Alt+Tab to a normal window and back.
- Explorer restart during fullscreen, including restored AppBar registration, reservation, one tray icon, Position and Exit actions, and post-recovery z-order.
- Fullscreen on another physical monitor and primary-monitor changes in both directions; only one display is available.
- Published-app resolution changes, 150% and 200% DPI, taskbar auto-hide, real tray-menu interaction, and focus-event instrumentation.
- The remaining B02–B03 published-app regression scenarios listed in the B04 package plan on one clean, matching source commit.

No placement fallback was implemented. Because required desktop evidence is still missing, this report does not make the placement-feasibility decision or mark dependent bricks ready.
