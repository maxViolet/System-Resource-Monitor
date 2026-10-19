# B005 implementation plan for coding agents

Status: planned. This package implements [B005 — Independent metric monitoring](../B05-metric-monitoring.md). The [product plan](../../plan.md#product-and-first-release), [implementation plan — Initial model and workflows](../../implementation-plan.md#initial-model-and-workflows), [automated verification](../../implementation-plan.md#automated-verification-and-handoff), [CONTEXT](../../../CONTEXT.md#language), [ADR 0005](../../adr/0005-project-level-runtime-and-testing-platform.md), and [ADR 0007](../../adr/0007-resource-projects-by-package.md) govern ambiguity. B004 must be accepted before B005 implementation begins; a failed B004 blocks B005 pending the placement decision in the [roadmap](../README.md#stages).

## Outcome and handoff

Deliver platform-independent CPU and RAM monitoring policy in `SystemResourceMonitor.Core`. Given fake metric sources and a fake `TimeProvider`, Core polls each source every two seconds without overlapping that source's reads, validates every result, publishes immutable `DashboardState` changes, and expires each reading at monotonic age `>= 6s` on its own timer, independently of the other source and of a blocked read.

B006 must be able to plug in the `GetSystemTimes` and `GlobalMemoryStatusEx` adapters as source ports and marshal state notifications to the WPF dispatcher without changing B005 policy. B005 has no Desktop, native, WPF, or manual desktop work; every acceptance criterion closes with automated tests. The freshness gate's real UI transition within 0.5 seconds of the deadline belongs to B006.

## Work order

Complete these steps in order. Keep the solution buildable after each step. All new production code lives in `src/system-resources/SystemResourceMonitor.Core/Metrics/` in namespace `SystemResourceMonitor.Core.Metrics`. Do not add `IClock`, a generic result type, DI registrations, or a scheduler abstraction.

### 1. Verify the B004 handoff and the Core baseline

- Require B004 accepted in the roadmap progress table or an accepted B004 acceptance report. If B004 is in progress or blocked, record B005 as blocked rather than implementing ahead of the placement decision.
- Run the targeted Core tests and the solution build from a clean tree. Record the starting commit in the B005 report.

**Gate:** B004 is accepted, and the baseline Core tests and solution build pass.

### 2. Model readings as an explicit union

- `MetricKind`: `Cpu`, `Ram`. Do not add GPU or temperature values; their bricks extend the enum.
- `MetricReading`: abstract record with `Kind`, sealed into exactly two cases.
  - `AvailableMetric`: `Kind`, `Percentage` (`double`, finite, `0..100` inclusive), `MeasuredAt` (`DateTimeOffset`, offset zero), `Timestamp` (`long` from `TimeProvider.GetTimestamp()`), and `RamBytes?` (`UsedBytes`, `TotalBytes` as `ulong`), present for RAM only and absent for CPU.
  - `UnavailableMetric`: `Kind` and `UnavailabilityReason` (`NotYetSampled`, `Expired`, `Invalid`, `ReadFailed`).
- Make construction of `AvailableMetric` impossible with out-of-range values: a private constructor plus one validating factory that returns either an `AvailableMetric` or `UnavailableMetric(kind, Invalid)`. Do not throw for measurement data; throw only for programmer errors such as an undefined `MetricKind` or a non-UTC `MeasuredAt`.
- Invalid when: percentage is NaN, infinite, below 0, or above 100; RAM has no bytes, `TotalBytes == 0`, or `UsedBytes > TotalBytes`; CPU carries RAM bytes. For RAM, Core computes `Percentage = UsedBytes / TotalBytes * 100` from the bytes so percentage and bytes cannot disagree.
- Valid zero is an `AvailableMetric` with `Percentage == 0`. Nothing in Core maps unavailable to zero or zero to unavailable.
- `DashboardState`: immutable record with `Cpu` and `Ram` readings. Initial state is `NotYetSampled` for both. Provide `With(MetricReading)` that replaces the reading of the matching kind and leaves the other unchanged.

**Gate:** model tests cover the 0 and 100 boundaries, values just outside them, NaN and both infinities, RAM byte invariants including `used == total` and `used == 0`, each unavailability reason, CPU with bytes, and valid zero being `AvailableMetric` rather than `UnavailableMetric`.

### 3. Define the source port

- `IMetricSource`: `MetricKind Kind { get; }` and synchronous `MetricSample Read()`. The port is synchronous because the accepted first-release reads are short synchronous native calls; B006 adapters must not wrap them in fake asynchrony.
- `MetricSample`: closed union of `MeasuredSample(double Percentage)` for CPU, `MeasuredRamSample(ulong UsedBytes, ulong TotalBytes)` for RAM, and `SampleUnavailable(UnavailabilityReason Reason)` where `Reason` is `NotYetSampled`, `Invalid`, or `ReadFailed`. A source never reports `Expired` and never supplies timestamps. A sample whose shape does not match the source's kind is `Invalid`.
- Core stamps `MeasuredAt` (`GetUtcNow()`) and `Timestamp` (`GetTimestamp()`) from the injected `TimeProvider` when `Read()` returns. Any exception thrown by `Read()` becomes `ReadFailed`; the exception does not escape the collector.

**Gate:** the port compiles without native types, and the B006 CPU first-sample, reset, and invalid-delta outcomes are expressible without changing it.

### 4. Apply readings and expiry in one state owner

- `MetricMonitor` owns the current `DashboardState`, takes both sources and `TimeProvider` in its constructor, and exposes `DashboardState Current` and `event EventHandler<DashboardState>? Changed`. One private lock serializes every state transition; raise `Changed` after the transition while still serialized so subscribers observe states in order. Document that handlers must return quickly and must not block; the Desktop handler only posts to the dispatcher (B006).
- Apply a completed read for one kind with these rules, notifying exactly once whenever a rule says "publish":
  - Valid measurement: replace the reading, publish, and re-arm that kind's expiry timer for `6s` after the new `Timestamp`. Publish even when the value equals the previous value, because the measurement time changed.
  - Invalid measurement or `SampleUnavailable(Invalid)`: replace with `Invalid` immediately, cancel the expiry timer, and publish if the previous reading was not already `Invalid`.
  - `ReadFailed` while the current reading is `AvailableMetric`: keep it and its timer. A failed read does not renew `MeasuredAt` or `Timestamp`; the reading expires on its original deadline.
  - `ReadFailed` or `NotYetSampled` while the current reading is unavailable: replace and publish only when the reason changes.
- Expiry: one `ITimer` per kind from `TimeProvider.CreateTimer`, due at the reading's deadline. When it fires, under the lock, expire only if the current reading is still that same `AvailableMetric` and `TimeProvider.GetElapsedTime(reading.Timestamp) >= 6s`; otherwise do nothing. Expiry replaces the reading with `Expired`, publishes once, and never touches the other kind. Age and deadlines use only the monotonic timestamp; `MeasuredAt` is for display and evidence.
- After expiry, the next failed read changes `Expired` to `ReadFailed` and publishes; the next successful read makes the reading available again.

**Gate:** state tests drive `MetricMonitor` directly through readings and fake time and observe each publication through `Changed`.

### 5. Poll each source without overlap

- `MetricMonitor.RunAsync(CancellationToken)` starts one polling loop per source and returns a task that completes when both loops have stopped. The monitor runs once; a second call throws `InvalidOperationException`.
- Each loop: record the poll's due time, run `source.Read()` on a thread-pool worker through `Task.Run`, await it, apply the result, then wait with `Task.Delay(remaining, timeProvider, token)` until `previousDue + 2s`. If the read finished after that time, start the next read immediately and skip missed ticks; never queue a burst. A loop never starts a read while its previous read is pending.
- The loops share nothing except the lock-protected state. A blocked CPU read must not delay RAM polling, RAM publication, or CPU expiry, and the inverse.
- Cancellation: no new read starts after the token is cancelled. A pending read is awaited with `WaitAsync(token)` so the loop exits promptly, but the synchronous call is not interrupted; its eventual result is discarded and never published. After cancellation, also dispose both expiry timers and reject every later state change, so neither late results nor expiry fire `Changed`. `RunAsync` completes without throwing on cancellation and surfaces any other unexpected failure.
- Do not record overrun durations in Core. Native-duration measurement belongs to B006; the responsiveness gate belongs to B007.

**Gate:** polling tests prove the two-second cadence, no overlap under slow reads, independence of both sources, and no publication after cancellation.

### 6. Keep Core's boundary and time discipline enforced

- Core keeps referencing no Windows, WPF, file, registry, process, or interop API; the existing `RS0030` build check and `ArchitectureTests` continue to enforce it unchanged.
- Add direct clock and timer APIs to Core's `BannedSymbols.txt` so all time flows through `TimeProvider`: `P:System.DateTime.Now`, `P:System.DateTime.UtcNow`, `P:System.DateTimeOffset.Now`, `P:System.DateTimeOffset.UtcNow`, `P:System.Environment.TickCount`, `P:System.Environment.TickCount64`, `T:System.Diagnostics.Stopwatch`, `T:System.Threading.Timer`, and the `Task.Delay` overloads without a `TimeProvider`. Add the same IDs to `ArchitectureTests.BannedSymbolIds`. The AI Core receives the same additions so both Core projects keep identical enforcement.

**Gate:** Core builds with the new bans, and `ArchitectureTests` fail if any ban is removed.

### 7. Prove the acceptance criteria with deterministic tests

Add `tests/SystemResourceMonitor.Core.Tests/Metrics/` and reference `Microsoft.Extensions.TimeProvider.Testing` from the Core test project; regenerate its `packages.lock.json` for `win-x64`. Tests use `FakeTimeProvider`, a controllable fake source per kind (gate to block, completion signal when a read starts, scripted results), and a recorder subscribed to `Changed`. Wait for asynchronous loop progress with explicit signals and a real-time safety timeout; never `Thread.Sleep` to wait for a state.

- **Invariants:** step 2 gate cases, including valid zero.
- **Exact boundary:** after a reading at `t0`, advancing to `t0 + 6s - 1 tick` leaves it available; advancing the final tick publishes `Expired` once.
- **Wall clock:** with a test `TimeProvider` whose UTC clock jumps independently of its monotonic timestamp, moving UTC forward or backward by hours neither expires nor extends a reading. If `FakeTimeProvider` cannot separate the two clocks, write a minimal test-only `TimeProvider` for this case.
- **Blocked source:** block CPU after one successful read; advance time in two-second steps; RAM publishes every two seconds and CPU publishes `Expired` at exactly six seconds while its read stays blocked. Repeat with RAM blocked and CPU updating.
- **No overlap:** a read that lasts five seconds of fake time produces no concurrent `Read()` for that source (assert the maximum in-flight count is one) and no tick burst after it completes; the other source's cadence and expiry stay unchanged.
- **Failed reads:** `ReadFailed` while available does not change the reading or its deadline and expiry still occurs at the original `t0 + 6s`; `ReadFailed` after expiry publishes once; a thrown exception behaves as `ReadFailed`.
- **Known-invalid:** NaN, out-of-range, and inconsistent RAM bytes replace an available reading with `Invalid` immediately and publish once; the other source is unaffected.
- **Notifications:** one notification per accepted reading and per availability transition, including expiry; none for an unchanged unavailable reason.
- **Cancellation:** after cancellation, no new `Read()` starts, a blocked read released later publishes nothing, expiry publishes nothing, and `RunAsync` completes.

**Gate:** `dotnet test --project tests/SystemResourceMonitor.Core.Tests/SystemResourceMonitor.Core.Tests.csproj -c Release` passes ten consecutive runs without a flaky failure.

### 8. Verify and record evidence

Run from the repository root on Windows 11 x64:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
git diff --check
```

B005 does not change the published app's behavior because Desktop does not compose `MetricMonitor` until B006; skip `dotnet publish` and say so in the report.

Create `docs/acceptance/<date>-B05-metric-monitoring.md` with commit and tree IDs, commands and exit codes, test counts, the ten-run stability result, and pass/fail for every B005 criterion with the test that proves it. The report has an automated section only and states that Windows integration and manual desktop checks are not applicable to B005. Update the roadmap progress table when B005 is accepted.

**Gate:** every B005 acceptance criterion maps to a passing named test, and the full handoff commands pass.

## Boundary for B006

B006 owns the `GetSystemTimes` and `GlobalMemoryStatusEx` adapters, CPU delta arithmetic and baseline reset, native-call duration measurement, composing `MetricMonitor` in Desktop startup and shutdown, marshalling `Changed` to the WPF dispatcher once, presentation, and tooltips that map each `UnavailabilityReason` to a safe explanation. B005 must hand off a stable `IMetricSource`, `MetricSample`, `MetricReading`, `DashboardState`, and `MetricMonitor` contract without Desktop code. Combining system metric state with subscription limit state is Desktop composition in B012/B013, not a B005 concern. Record a new ADR for any new design choice with meaningful alternatives.
