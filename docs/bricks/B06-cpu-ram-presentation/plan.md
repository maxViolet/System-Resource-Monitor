# B006 implementation plan for coding agents

Status: done. This package implements [B006 — CPU/RAM sources and bar presentation](../B06-cpu-ram-presentation.md). The [product plan — Deliver CPU/RAM](../../plan.md#2-deliver-cpuram), [implementation plan — Slice 1](../../implementation-plan.md#slice-1-cpuram), [Initial model and workflows](../../implementation-plan.md#initial-model-and-workflows), [Technology and layout](../../implementation-plan.md#technology-and-layout), [Settings and lifecycle](../../implementation-plan.md#settings-and-lifecycle), [AppBar and shell behaviour](../../implementation-plan.md#appbar-and-shell-behaviour), [first-release acceptance gates](../../implementation-plan.md#first-release-acceptance-gates), [automated verification](../../implementation-plan.md#automated-verification-and-handoff), [CONTEXT](../../../CONTEXT.md#language), [ADR 0002](../../adr/0002-appbar-first-cpu-ram-release.md), [ADR 0005](../../adr/0005-project-level-runtime-and-testing-platform.md), [ADR 0007](../../adr/0007-resource-projects-by-package.md), [ADR 0011](../../adr/0011-exit-when-a-collector-run-faults.md), and the [B005 handoff](../B05-metric-monitoring/plan.md#boundary-for-b06) govern ambiguity. B005 must be done before B006 implementation begins.

## Outcome and handoff

Deliver the first-release bar content. The published app shows fixed-width `CPU 12%` and `RAM 64%` sections on the existing dark 24-DIP strip. CPU load comes from `GetSystemTimes` deltas, and RAM usage from `GlobalMemoryStatusEx`. The RAM tooltip shows used and total GiB. An unavailable reading shows `—` with a tooltip that gives a safe explanation, while the other reading keeps updating. B005's `MetricMonitor` owns polling, expiry, and state. B006 adds only native source adapters, CPU delta arithmetic, Desktop composition, dispatcher marshalling, presentation, and test-host instrumentation.

B012 and B013 add provider sections next to these sections without changing them. B007 runs the 30-minute native-read responsiveness, accuracy, resource, artifact-hygiene, and release gates on artifacts that include this work. Do not add GPU, subscription, or temperature placeholders.

## Work order

Complete these steps in order. Keep the solution buildable after each step. The Core code lives in `src/system-resources/SystemResourceMonitor.Core/Metrics/` (namespace `SystemResourceMonitor.Core.Metrics`). The native adapters live in `src/system-resources/SystemResourceMonitor.Desktop/Adapters/Metrics/`, following `Adapters/Settings/`. Presentation lives in `Bar/`. Use `LibraryImport`, like `Bar/NativeMethods.cs`. Do not add DI, Generic Host, a view-model framework, XAML resources for the sections, or an abstraction over `MetricMonitor`.

### 1. Verify the B005 handoff and the baseline

- Require B005 done, shown either in the roadmap progress table or in an accepted B005 report. B005 itself requires an accepted B004. If either is not done, record B006 as blocked and do not implement against a guessed contract.
- Confirm the contract this plan relies on: `IMetricSource` (`Kind`, synchronous `Read()`), the `MetricSample` cases `MeasuredSample`, `MeasuredRamSample`, and `SampleUnavailable(NotYetSampled | Invalid | ReadFailed)`, `MetricReading`/`AvailableMetric.RamBytes`, `DashboardState`, and `MetricMonitor(cpu, ram, TimeProvider)` with `Current`, `Changed`, and `RunAsync(CancellationToken)`. A mismatch is a B005 defect or a documented change. Do not work around it in Desktop.
- From a clean tree, run the targeted Core and Desktop tests and the solution build. Record the starting commit in the B006 report.

**Gate:** B005 is done, its contract matches, and the baseline tests and build pass.

### 2. Calculate CPU load from counter deltas in Core

- `CpuTimes`: a record struct with `Idle`, `Kernel`, and `User` as `ulong` 100-ns ticks. As Windows reports them, `Kernel` includes `Idle`.
- `CpuLoadCalculator`: stateful, holding one optional baseline, with `MetricSample Next(CpuTimes current)`:
  - No baseline: store `current` and return `SampleUnavailable(NotYetSampled)`.
  - If any counter is lower than the baseline, treat it as a counter reset. If `total = ΔKernel + ΔUser` overflows (checked arithmetic) or is `0`, or `ΔIdle > total`, the delta is invalid. In every one of these cases, discard the old baseline, store `current` as the new baseline, and return `SampleUnavailable(Invalid)`.
  - Otherwise, store `current` and return `MeasuredSample((total - ΔIdle) / (double)total * 100)`.
- The calculator receives only successful reads. A failed native read leaves the baseline unchanged, so the next delta covers the gap. The source never reports `NotYetSampled` after its first successful read. This matters because B005 defines no rule for `NotYetSampled` while a reading is available.
- The calculator is not thread-safe. Its single caller is a B005 polling loop, which never overlaps reads. Document that in the type comment.

**Gate:** Core tests cover first-sample `NotYetSampled`; exact 0%, 50%, and 100% deltas; a reset of each counter independently; zero total; idle greater than total; overflow; and a valid value on the read after each `Invalid`, computed against the new baseline.

### 3. Wrap the two native calls in Desktop adapters

- `ISystemTimesApi.TryRead(out CpuTimes times, out int error)` and `IMemoryStatusApi.TryRead(out ulong totalPhys, out ulong availPhys, out int error)` are the native seams. `Win32SystemTimesApi` calls `GetSystemTimes` with three `FILETIME` outputs and converts each as `((ulong)high << 32) | low`. `Win32MemoryStatusApi` calls `GlobalMemoryStatusEx` with a `MEMORYSTATUSEX` whose `dwLength` is set from its marshalled size. Declare both with `SetLastError = true`, check the `BOOL` result, and read `Marshal.GetLastPInvokeError()` only on failure. Neither wrapper holds a handle, allocates native memory, or caches state between calls.
- `SystemTimesCpuSource : IMetricSource` (`Kind = Cpu`) maps a failed read to `SampleUnavailable(ReadFailed)` without touching the calculator. A successful read goes to `CpuLoadCalculator.Next`.
- `GlobalMemoryRamSource : IMetricSource` (`Kind = Ram`) maps a failed read to `ReadFailed`. `totalPhys == 0` or `availPhys > totalPhys` maps to `SampleUnavailable(Invalid)` before any subtraction. Otherwise it returns `MeasuredRamSample(totalPhys - availPhys, totalPhys)`, and Core computes the percentage.
- Production has no logging or diagnostic hook for error codes. The `error` out-parameter exists for tests and the test host.
- Add `FILETIME` (8 bytes) and `MEMORYSTATUSEX` (64 bytes) to `NativeLayoutTests`.

**Gate:** adapter tests with fake APIs prove the failure mapping, the RAM invariants, and that a failed CPU read between two successes keeps the original baseline.

### 4. Compose monitoring in Desktop startup and shutdown

- `App.xaml.cs` constructs `MetricMonitor` from the two production sources and `TimeProvider.System`. It passes the monitor to `DesktopStartup`, which takes the lifetime token from `shutdown.Lifetime` and is the composition seam the test host also uses with fake sources.
- In `DesktopStartup.CompleteStartup`, after `bar.Start()` and `bar-positioned`: subscribe to `Changed`, apply `monitor.Current`, then start `monitor.RunAsync(shutdown.Lifetime)` and pass the task to `shutdown.TrackOwner("metric-monitor", …)`. A faulted run enters failure shutdown ([ADR 0011](../../adr/0011-exit-when-a-collector-run-faults.md)). Record `metrics-started`. Collectors therefore start after the guard, settings, and AppBar, as the plan requires.
- The `Changed` handler only marshals. It stores the latest state with `Interlocked.Exchange` and posts one `Dispatcher.BeginInvoke` while no post is pending. The posted action applies the latest state through `shutdown.TryPublish(() => bar.ShowMetrics(state))`. The handler never blocks and never touches UI objects. A blocked UI thread therefore builds no queue, and nothing is published after shutdown begins.
- Shutdown order stays unchanged: cancel `_lifetime`, remove the AppBar and tray, close the bar, then wait at most 2 seconds for tracked work. A read still blocked at cancellation does not delay exit, because B005 completes `RunAsync` on cancellation without waiting for the read.
- Leave the 500 ms fullscreen reconciliation timer as it is. It stays active in every measurement run.

**Gate:** startup tests and test-host markers show `bar-positioned` before `metrics-started`, nothing published after `wait-start`, and normal exit still restoring the work area before the wait.

### 5. Present fixed-width sections and tooltips

- `MetricSectionFormat` is a pure Desktop formatter. It maps a `MetricReading` to section text and an optional tooltip, using invariant culture:
  - Available: `CPU {n}%` or `RAM {n}%`, with `n = Math.Round(Percentage, MidpointRounding.AwayFromZero)`.
  - Available CPU has no tooltip. Available RAM's tooltip is `RAM {used:0.0} / {total:0.0} GiB used`, with GiB = bytes / 2^30.
  - Unavailable: `CPU —` or `RAM —`, with this tooltip per reason:
    - `NotYetSampled`: `Waiting for the first measurement.`
    - `Expired`: `No update for 6 seconds.`
    - `Invalid`: `Windows returned an inconsistent measurement.`
    - `ReadFailed`: `Windows did not return a measurement.`
  - Tooltips never contain exception text or error codes. An undefined reason is a programmer error and throws.
- `BarWindow` gets a code-built content of two left-aligned `TextBlock` sections: Segoe UI 12 DIP, light foreground on the existing `#1E1E1E` background, `UseLayoutRounding = true`, and no trimming or wrapping. Each section's width is fixed to the widest measured candidate among `{label} 0%` … `{label} 100%` and `{label} —`, measured with `FormattedText` at the window's current `PixelsPerDip`. Recompute the widths in `OnDpiChanged`. Text changes never change layout.
- `BarWindow.ShowMetrics(DashboardState)` verifies dispatcher access and updates text and tooltips only. The bar stays non-activating: hovering for a tooltip must not activate the bar or take foreground. If WPF tooltips do not appear, or they activate the window, stop and record an ADR before choosing another tooltip mechanism.

**Gate:** formatter tests cover 0, 100, rounding at 0.5/99.5, every unavailable reason, GiB formatting, and the absence of a CPU tooltip. A real-HWND check in the test host finds each section's `DesiredSize` within its fixed width and the bar's height for 0%, 100%, and unavailable.

### 6. Add test-host modes for desktop and fault evidence

Add these modes to `SystemResourceMonitor.Desktop.TestHost` through the production `DesktopStartup` seam, with handle-free fake sources. They stay out of the published app. Wire each one into `LifecycleProcessTests`, following the existing pattern.

- `--metric-freshness`: CPU returns scripted values, then blocks forever on its next read while RAM keeps returning values. Record a marker when each rendered section text changes: after `ShowMetrics`, at `DispatcherPriority.Render`. Pass when CPU shows `—` within 0.5 s of `last successful Timestamp + 6 s` (same `Stopwatch` clock as `TimeProvider.System`), and RAM keeps changing every ≤ 2.5 s before and after. Repeat with RAM blocked and CPU updating. Then exit with the read still blocked: the work area is restored before `wait-start`, and the process exits within the 2-second deadline without publishing.
- `--metric-display [seconds]`: cycle scripted CPU/RAM states of 0%, 100%, and each unavailable reason, holding each state long enough to capture. In each state, assert fixed section widths, unclipped `DesiredSize`, and `CheckPositionedBar` (reserved strip matches the rendered bar).
- `--native-reads <seconds>`: call the production `Win32SystemTimesApi` and `Win32MemoryStatusApi` every 2 seconds on thread-pool workers, timestamp every call, and report the call count, the maximum duration, any call over 2 s, and the longest run of consecutive overruns. Fail on two consecutive overruns from either reader. B006 runs it for a short smoke; B007 owns the 30-minute gate run.

**Gate:** all three modes pass on the target Windows 11 desktop and through `dotnet test`.

### 7. Prove the acceptance criteria

- **CPU arithmetic (criterion 1):** step 2 Core tests and step 3 adapter tests.
- **Native wrappers (criterion 2):** Windows integration tests in Desktop.Tests call the real `Win32SystemTimesApi` twice. They expect counters that never decrease and a delta that the calculator accepts as `0..100`. They call the real `Win32MemoryStatusApi` and expect `0 < avail <= total`. For the RAM failure path, test the production wrapper through an internal overload that takes `dwLength`. Passing `0` must make Windows return `FALSE` (expected error `ERROR_INVALID_PARAMETER`; verify), which maps to `ReadFailed`. Windows has no safe way to make `GetSystemTimes` fail, so cover the CPU failure path with a fake `ISystemTimesApi` behind the production `SystemTimesCpuSource`, and say so in the report.
- **Display and DPI (criterion 3):** run `--metric-display` at 100%, 150%, and 200% scaling. Capture cropped bar screenshots, record the programmatic width/clip results, and confirm that the RAM tooltip and the unavailable tooltips appear on the published app without activating the bar.
- **Freshness (criterion 4):** use `--metric-freshness` timings, plus a visual observation of the same run.
- **Responsiveness (criterion 5):** during every published-app and test-host run, check that tray menu, edge switch, and exit respond, and that hover tooltips appear, while monitoring is active. Record any hang.

**Gate:** `dotnet test --project tests/SystemResourceMonitor.Core.Tests/SystemResourceMonitor.Core.Tests.csproj -c Release` and `dotnet test --project tests/SystemResourceMonitor.Desktop.Tests/SystemResourceMonitor.Desktop.Tests.csproj -c Release` pass ten consecutive runs without a flaky failure.

### 8. Verify and record evidence

Run from the repository root on Windows 11 x64:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

- Build the published app and the test host from one commit. Record commit/tree IDs and both executable SHA-256 values. Confirm that the published folder contains no test-host modes or fakes.
- Create `docs/acceptance/<date>-B06-cpu-ram-presentation.md` with separate automated, Windows integration, and manual desktop sections. For each section, record commands and exit codes, test counts, the ten-run stability result, measured expiry offsets, native-read smoke statistics, display/DPI configuration with screenshots per scale, and pass/fail per B006 criterion. Mark unavailable scaling or scenarios as unverified. Follow the sanitization rules in the [first-release gates](../../implementation-plan.md#first-release-acceptance-gates).
- Update the roadmap progress table when B006 is done.

**Gate:** every B006 criterion maps to passing evidence, and the full handoff commands pass. Automated success alone does not close criteria 3–5.

## Boundary for B007, B012, and B013

B007 runs CPU/RAM accuracy, the 30-minute `--native-reads` responsiveness gate, the resource and eight-hour memory gates, artifact hygiene, fault shutdown, user documentation, and the release report on clean artifacts. B012 and B013 add provider sections and their AI Core state through the same dispatcher marshalling and `TryPublish` path, without resizing or reordering the CPU/RAM sections. Combining system metric state with limit snapshots is their Desktop composition, not a change to `DashboardState`. Record a new ADR for any new design choice with meaningful alternatives.
