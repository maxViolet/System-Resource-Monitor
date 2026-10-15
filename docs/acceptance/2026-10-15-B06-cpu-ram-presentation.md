# B06 CPU/RAM presentation report — 2026-10-15

Brick: [B06 — CPU/RAM sources and bar presentation](../bricks/B06-cpu-ram-presentation.md). Package plan: [B06 plan](../bricks/B06-cpu-ram-presentation/plan.md).

Status: in progress. Criteria 1 and 2 pass. Criteria 3–5 are partial: their automated test-host checks pass on a real HWND/AppBar at 100% scaling, but automated success alone does not close them. Published-app tooltip hover, 150% and 200% scaling, a visual observation of expiry, and published-app tray and edge interaction are unverified. B06 is not done.

## Prerequisite

The plan requires B05 done before B06 implementation begins. [B05's 2026-10-15 report](2026-10-15-B05-metric-monitoring.md) states "implemented; automated criteria pass; not accepted: B04 gate open". On 2026-10-15 the user marked B04 done, and the roadmap progress table now records B04 as `done`. B05 remains `planned` in that table and has no accepted report. The user directed B06 implementation to proceed on that basis.

The B06 branch was based on the B06 plan commit `a9cce39`, which predates the B05 merge. Commit `189cfb0` merges `origin/main` (`47281fc`, PR #19) into the branch. The B05 contract matches the plan: `IMetricSource` (`Kind`, synchronous `Read()`), `MeasuredSample`, `MeasuredRamSample`, `SampleUnavailable(NotYetSampled | Invalid | ReadFailed)`, `MetricReading`, `AvailableMetric.RamBytes`, `DashboardState`, and `MetricMonitor(cpu, ram, TimeProvider)` with `Current`, `Changed`, and `RunAsync(CancellationToken)`.

## Environment

| | |
|---|---|
| Source | Branch `FEAT-006-cpu-ram-presentation`, uncommitted working tree based on commit `189cfb01363229a8e3ff499dd0ec1548070a16e8` (tree `facee77ee0d91e2e2b3eac4229e3b5a18fdd114f`) |
| Windows | Windows 11 Pro x64, build 10.0.26300.9711 |
| Hardware | Desktop PC, 22 logical processors |
| Display | One 1920×1200 primary display at 100% scaling; Windows taskbar at the bottom |
| .NET SDK | 10.0.401, pinned in `global.json`, from a session-local install because the machine-wide `dotnet` has only SDK 8.0.425 |
| Test runner | Microsoft.Testing.Platform with xunit.v3 4.0.2 ([ADR 0005](../adr/0005-project-level-runtime-and-testing-platform.md)) |

## Baseline

The baseline ran at `189cfb0` before any B06 change.

| Command | Exit code | Result |
|---|---|---|
| `dotnet build SystemResourceMonitor.slnx -c Release` | 0 | 0 warnings, 0 errors. |
| `dotnet test --project tests/SystemResourceMonitor.Core.Tests/SystemResourceMonitor.Core.Tests.csproj -c Release --no-build` | 0 | 69 total, 69 succeeded. |
| `dotnet test --project tests/SystemResourceMonitor.Desktop.Tests/SystemResourceMonitor.Desktop.Tests.csproj -c Release --no-build` | 0 | 83 total, 83 succeeded. |

## Implemented behavior

- Core `CpuTimes` and `CpuLoadCalculator` turn successive `GetSystemTimes` counters into load samples. The first read is `NotYetSampled`. A lower counter, a zero or overflowing `ΔKernel + ΔUser`, or idle greater than the total rebases and returns `Invalid`.
- Desktop `Adapters/Metrics` holds `Win32SystemTimesApi` and `Win32MemoryStatusApi` (`LibraryImport`, `SetLastError = true`, `BOOL` checked, no handles or native memory), `SystemTimesCpuSource`, and `GlobalMemoryRamSource`. A failed read is `ReadFailed` and leaves the CPU baseline unchanged. RAM rejects `total == 0` and `available > total` as `Invalid` before subtraction.
- `DesktopStartup` takes the `MetricMonitor` and lifetime token. After `bar-positioned`, it subscribes to `Changed`, shows `Current`, tracks `RunAsync` as `metric-monitor`, and records `metrics-started`. `MetricRelay` stores the latest state and posts one dispatcher action while none is pending. That action shows the state through `ShutdownCoordinator.TryPublish`.
- `BarWindow` shows two left-aligned Segoe UI 12 DIP sections on the `#1E1E1E` strip. Each section's width is fixed to its widest candidate (`0%`…`100%` and `—`), measured with `FormattedText` at the window's `PixelsPerDip`, rounded up to whole device pixels, and recomputed in `OnDpiChanged`. `MetricSectionFormat` produces the text and tooltips with invariant culture.
- The test host adds `--metric-freshness [cpu|ram]`, `--metric-display [seconds]`, and `--native-reads <seconds>`, with handle-free scripted sources. Every test-host lifecycle mode now fails if metrics are shown after `wait-start`.

## Source and artifacts

The artifacts were built from the uncommitted working tree described above. They are verification artifacts, not clean-commit acceptance artifacts. The production Desktop and Core assemblies copied beside the test host match the published assemblies.

| Artifact | SHA-256 |
|---|---|
| published `SystemResourceMonitor.Desktop.exe` | `b28dd67c0a388d54e980333d8e026bf9d1bc3dceeadacb9cdf1f96763d1702dd` |
| published `SystemResourceMonitor.Desktop.dll` | `3b498b112093a51aa11f763e16719ec0eba58271a96b9a900e39bb1c656e93dc` |
| published `SystemResourceMonitor.Core.dll` | `f55d5bd8f3d25e5c7da1bd891be1dfd46ab6aab8171d3274d119356d9d356a87` |
| test-host `SystemResourceMonitor.Desktop.TestHost.exe` | `dbe37b5206ef6242e108900fdd3d2f47ad70d53cb38c5d89625c814a401c1319` |
| test-host `SystemResourceMonitor.Desktop.TestHost.dll` | `98880b2af72eee32f636e15ee52d6c5b1ff9781231d63f338ae1e53badbc2ce1` |
| test-host `SystemResourceMonitor.Desktop.dll` | `3b498b112093a51aa11f763e16719ec0eba58271a96b9a900e39bb1c656e93dc` |
| test-host `SystemResourceMonitor.Core.dll` | `f55d5bd8f3d25e5c7da1bd891be1dfd46ab6aab8171d3274d119356d9d356a87` |

The self-contained published folder contains 259 files. No file name matches `testhost`, `tests`, `xunit`, `fixture`, `testing`, or `scripted`.

## Automated checks

| Command | Exit code | Result |
|---|---|---|
| Core.Tests and Desktop.Tests (`dotnet test --project … -c Release --no-build`), ten consecutive runs of each | 0 each | Core 81/81 and Desktop 120/120 in every run; no flaky failure. |
| `dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode` | 0 | All six projects restored from lock files. |
| `dotnet build SystemResourceMonitor.slnx -c Release --no-restore` | 0 | 0 warnings, 0 errors. |
| `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build` | 0 | 201 total, 201 succeeded. |
| `dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true` | 0 | Self-contained folder produced. |
| `git diff --check` | 0 | No whitespace errors in tracked files; a separate scan found no trailing whitespace in new files. |

New tests: 12 in `CpuLoadCalculatorTests` (Core); in Desktop.Tests, `MetricSourceTests`, `NativeMetricReadTests`, `MetricSectionFormatTests`, `MetricRelayTests`, two `NativeLayoutTests` assertions, and four `LifecycleProcessTests` cases (`--metric-display`, `--metric-freshness cpu`, `--metric-freshness ram`, `--native-reads 10`).

## Windows integration

| Check | Result |
|---|---|
| `GetSystemTimes`, two real reads 100 ms apart | Passed; counters never decrease, kernel includes idle, and the calculator accepts the delta in `0..100`. |
| `GlobalMemoryStatusEx`, real read | Passed; `0 < available <= total`, and the production RAM source returns `MeasuredRamSample`. |
| `GlobalMemoryStatusEx` with `dwLength = 0` through the production wrapper | Passed; Windows returned `FALSE` with `ERROR_INVALID_PARAMETER` (87), which maps to `ReadFailed`. |
| `GetSystemTimes` failure path | Covered only by a fake `ISystemTimesApi` behind the production `SystemTimesCpuSource`; Windows has no safe way to make the call fail. |
| `FILETIME` and `MEMORYSTATUSEX` sizes | Passed; 8 and 64 bytes. |
| `--native-reads 10` smoke | Passed; 5 calls per reader, maximum 9.7 ms (`GetSystemTimes`) and 5.1 ms (`GlobalMemoryStatusEx`), no call over 2 s. B07 owns the 30-minute run. |

## Desktop runs

All runs below used the test host on the real desktop at 100% scaling with a registered AppBar.

| Scenario | Result |
|---|---|
| `--metric-display` | Passed. Fixed widths: CPU 55 DIP, RAM 59 DIP; bar height 24 DIP. Widest natural text: `CPU 100%` 55 DIP, `RAM 100%` 58 DIP, height 16 DIP. Widths stayed fixed for 0%, 100%, and all four unavailable reasons, and the reserved strip matched the rendered bar in every state. |
| `--metric-freshness cpu` | Passed. CPU showed `—` 2 ms after its deadline, with the expired tooltip. RAM changed 8 times, with a maximum gap of 2002 ms. Exit with the read still blocked: work area restored before `wait-start`; the wait took 7 ms; no `pending:metric-monitor`. |
| `--metric-freshness ram` | Passed. RAM showed `—` 8 ms after its deadline. CPU changed 8 times, with a maximum gap of 2005 ms. The wait took 8 ms. |
| Published app, start and `WM_CLOSE` | Passed. The bar appeared, and the app exited with code 0 337 ms after `WM_CLOSE` while monitoring was active. |

## Review fixes

After review, the working tree changed:

- `DesktopStartup` observes the `MetricMonitor.RunAsync` task through `MetricRunFailure`. A faulted run records `metrics-failed:<type>` and enters failure shutdown on the dispatcher, because a stopped monitor would leave the last readings on the bar as fresh. A cancelled run is not a failure.
- `--native-reads` fails when any call fails.
- `--metric-display` measures each section's text with `FormattedText` at the bar's per-monitor `PixelsPerDip`, not with a detached `TextBlock` at system DPI.
- `MetricSectionFormatTests` covers an undefined `UnavailabilityReason`.

The artifact hashes, the ten-run result, and the 201-test solution run above come from the tree before these fixes. The runs below did not register an AppBar or change the foreground window, because the desktop was in use. `LifecycleProcessTests` (14 tests) was therefore excluded, and the changed `--metric-display` mode, the freshness modes, and the published app were not rerun.

| Command | Exit code | Result |
|---|---|---|
| `dotnet test --project tests/SystemResourceMonitor.Core.Tests/SystemResourceMonitor.Core.Tests.csproj -c Release` | 0 | 81/81. |
| `dotnet test --project tests/SystemResourceMonitor.Desktop.Tests/SystemResourceMonitor.Desktop.Tests.csproj -c Release --no-build -- --filter-not-class "*LifecycleProcessTests"` | 0 | 109/109. |
| `MetricRunFailureTests`, ten consecutive runs | 0 each | 2/2 in every run. |
| `SystemResourceMonitor.Desktop.TestHost.exe --native-reads 10` | 0 | 5 calls per reader, 0 failed, maximum 10.6 ms, no call over 2 s. |
| `dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode` | 0 | Restored from lock files. |
| `dotnet build SystemResourceMonitor.slnx -c Release --no-restore` | 0 | 0 warnings, 0 errors. |
| `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build -- --filter-not-class "*LifecycleProcessTests"` | 0 | 190/190. |
| `dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true` | 0 | Self-contained folder produced. |
| `git diff --check` | 0 | No whitespace errors; new files have no trailing whitespace. |

## Acceptance criteria

| Criterion | Result |
|---|---|
| 1. CPU arithmetic, first sample, reset, and invalid deltas have deterministic tests | Pass. |
| 2. Windows integration checks exercise both native wrappers and their failure paths | Pass, with the `GetSystemTimes` failure path covered by a fake as described above. |
| 3. Actual bar shows fixed-width text, RAM GiB details, and unavailable states; fixed and unclipped at 100%, 150%, and 200% | Partial. 100% passes programmatically in the test host. Not verified: 150% and 200%, cropped screenshots per scale, and tooltip appearance on the published app. |
| 4. Fault injection visibly expires one reading within 0.5 s while the other keeps updating | Partial. The automated timing passes (2 ms and 8 ms). Not verified: a person observing the expiry on screen. |
| 5. Published app stays responsive during desktop and measurement runs | Partial. Exit responded in 337 ms with monitoring active, and test-host edge switches and exit passed with monitoring active. Not verified: published-app tray menu, edge switch, and hover tooltips. |

## Unverified

- Tooltip hover on the published app. One automated hover attempt found no tooltip window, because a fullscreen video was in the foreground and the bar was correctly lowered behind it. The attempt left foreground focus unchanged. No conclusion about tooltip behavior follows from it. Hover over the RAM section and over a `—` section, then confirm that the tooltip appears and that the bar does not take foreground.
- 150% and 200% scaling: `--metric-display 3` and cropped bar screenshots at each scale.
- Visual observation of `--metric-freshness`.
- Published-app tray menu and edge switch while monitoring is active.
- After the review fixes: `LifecycleProcessTests`, including `--metric-display` with per-monitor DPI measurement, the full ten-run stability gate, and new artifact hashes.
