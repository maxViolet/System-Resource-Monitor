# B08 implementation plan for coding agents

Status: planned. This package implements [B08 — GPU adapter selection, correlation, and display](../B08-gpu.md). The [product plan — GPU](../../plan.md#gpu), [implementation plan — Initial model and workflows](../../implementation-plan.md#initial-model-and-workflows), [Settings and lifecycle](../../implementation-plan.md#settings-and-lifecycle), [Independent later slices](../../implementation-plan.md#independent-later-slices), [first-release acceptance gates](../../implementation-plan.md#first-release-acceptance-gates), [CONTEXT](../../../CONTEXT.md#language), [ADR 0011](../../adr/0011-exit-when-a-collector-run-faults.md), [ADR 0012](../../adr/0012-discrete-gpu-selected-by-dxcore-and-correlated-by-luid.md), and [ADR 0013](../../adr/0013-foreign-empty-gpu-engine-instances.md) govern ambiguity. B07 must be `done` before B08 implementation begins.

## Outcome and handoff

The bar gains a fixed-width `GPU 37%` section after RAM. Its value is the load of the one discrete adapter: the maximum, over the 3D, Compute, Copy, and Video engines, of each engine's utilization summed across processes. The section never shows an integrated adapter. With no discrete adapter, more than one, or counters that cannot be proven to belong to the selected adapter, it shows `GPU —` with a tooltip that says why. CPU and RAM polling, freshness, and presentation stay unchanged when the GPU read fails or blocks.

B08 adds GPU contracts to the system Core, DXCore and PDH adapters to Desktop, the GPU section, test-host evidence modes, and the GPU acceptance runs. B14 relies on the same discrete-only adapter identity. Temperatures, multi-adapter display, and aggregation across adapters are out of scope.

## Decisions fixed by this plan

These values are set before any measurement, as the product plan requires:

- **Engine types.** Each PDH instance's `engtype_<name>` is matched case-insensitively by prefix: `3d` → 3D, `compute` → Compute, `copy` → Copy, `video` → Video (covering `VideoDecode`, `VideoEncode`, `VideoProcessing`, and `Video Codec`). Every other engine type is excluded and listed in the probe report. If Task Manager's value is driven by an excluded engine type, record the divergence; do not widen the CONTEXT definition in this brick.
- **Per-engine sums.** PDH values are read uncapped (`PDH_FMT_DOUBLE | PDH_FMT_NOCAP100`). A single instance value that is non-finite, negative, or above 100 makes the sample `Invalid`. A per-engine sum above 100 is capped at 100, matching Task Manager's display range.
- **Physical index.** An instance of the selected LUID with a physical index other than `0` makes the sample `Uncorrelated`; linked multi-node adapters are not shown.
- **Empty engine names.** An instance with a fully parseable PID/LUID/physical/engine identity and an empty `engtype_` is ignored only when its LUID differs from the selected adapter. An empty engine type for the selected LUID, or any unparseable identity, makes the sample `Uncorrelated` ([ADR 0013](../../adr/0013-foreign-empty-gpu-engine-instances.md)).
- **Task Manager tolerance.** For idle and for sustained GPU load separately: 30 paired observations at 2-second intervals in the same 60-second window, on the same adapter. The difference between window means must be at most 5 percentage points in each regime. Record transition spikes separately.
- **In-process read.** The GPU read runs in-process on a thread-pool worker. It must pass the same rule as CPU and RAM: no two consecutive reads longer than 2 seconds. A failure requires a new ADR before moving it to a child process.

## Work order

Complete these steps in order and keep the solution buildable after each. Core code lives in `src/system-resources/SystemResourceMonitor.Core/Metrics/`; native adapters in `src/system-resources/SystemResourceMonitor.Desktop/Adapters/Metrics/`; presentation in `Bar/`. Use `LibraryImport` and source-generated COM (`[GeneratedComInterface]`, `StrategyBasedComWrappers`); do not add a COM interop package, DI, or an abstraction over `MetricMonitor`.

### 1. Verify the B07 handoff and the baseline

- Require B07 `done` in the [roadmap progress table](../README.md#progress) with its acceptance reports. If B07 is not done, record B08 as `blocked` and do not implement ahead of it.
- From a clean tree, run the targeted Core and Desktop tests and the solution build. Record the starting commit and the B07 published-app and test-host hashes; they are the baseline for the gate reruns in step 9.

**Gate:** B07 is done and the baseline tests and build pass.

### 2. Probe adapters and counters on the target PC

Add a test-host mode `--gpu-probe` that only reads and reports:

- every DXCore adapter with the D3D12 graphics attribute: driver description, `InstanceLuid` (high and low parts), `IsHardware`, `IsIntegrated`, and `IsDetachable`;
- the discrete-only selection result under ADR 0012;
- for one reused PDH query on `\GPU Engine(*)\Utilization Percentage` added with `PdhAddEnglishCounterW`: instance count, every distinct LUID and its match to a DXCore adapter, every distinct `engtype`, and every instance name that does not parse;
- whether a process started between two collections appears in the next collection of the same query without re-adding the counter;
- the duration of each `PdhCollectQueryData` plus `PdhGetFormattedCounterArrayW` over 60 collections at 2-second intervals.

**Gate:** the selected adapter's LUID appears in the PDH instances and the same query picks up new instances. If either fails, record the evidence and go to the rejection path in step 9. If new instances require rebuilding the query, record a new ADR before continuing.

### 3. Extend the Core contracts

- `MetricKind.Gpu`.
- `GpuAdapter`: record with `Description` (`string`) and `Luid` (`AdapterLuid(uint LowPart, int HighPart)`).
- `UnavailabilityReason` gains `NoDiscreteAdapter`, `AmbiguousAdapter`, and `Uncorrelated`. Only a GPU source may report them; a CPU or RAM sample carrying one is `Invalid`. A source still never reports `Expired`.
- `MetricSample` gains `MeasuredGpuSample(GpuAdapter Adapter, double Percentage)` and `GpuUnavailableSample(GpuAdapter? Adapter, UnavailabilityReason Reason)`. `Adapter` is `null` only for `NoDiscreteAdapter` and `AmbiguousAdapter`.
- `AvailableMetric.Create` accepts a GPU percentage under the same finite `0..100` rule as CPU. `DashboardState` gains `Gpu` (initially `NotYetSampled`) and `GpuAdapter?`, the adapter from the latest GPU sample. Expiry replaces the reading but keeps `GpuAdapter`, so the tooltip still names it.
- `MetricMonitor(cpu, ram, gpu, time)`: one polling loop and one expiry timer per kind, all serialized by the existing lock. Generalize the per-kind fields instead of adding a third copy of each. Existing CPU/RAM rules stay unchanged; `NoDiscreteAdapter`, `AmbiguousAdapter`, and `Uncorrelated` follow the `Invalid` rule: replace an available reading immediately, cancel its timer, and publish when the reason changes. If GPU selection changes adapters, a `NotYetSampled` or `ReadFailed` result also replaces the previous adapter's available reading immediately and cancels its timer; never show that old percentage as the new adapter's value.
- Update every caller: `App.xaml.cs`, `LifecycleRun`, and the Core tests.

**Gate:** Core builds with the existing `RS0030` bans unchanged, and every existing Core test passes with a GPU source added.

### 4. Add the GPU rules in Core

- `GpuAdapterSelector.Select(IReadOnlyList<GpuAdapterCandidate>)`, where a candidate has `Description`, `Luid`, `IsHardware`, and `IsIntegrated`. Discrete means `IsHardware && !IsIntegrated`. It returns the selected `GpuAdapter`, `NoDiscreteAdapter`, or `AmbiguousAdapter`.
- `GpuEngineInstance.TryParse(string)` for `pid_<n>_luid_0x<8 hex>_0x<8 hex>_phys_<n>_eng_<n>_engtype_<name>`, where the first hex group is `HighPart` and the second `LowPart`; an empty engine name is structurally parsed as `Unspecified` for the foreign-LUID rule below.
- `GpuLoadCalculator.Calculate(GpuAdapter selected, IReadOnlyList<GpuCounterItem>)`. An item is an instance name, a value, and whether PDH marked it valid. The calculator returns a `MetricSample`:
  - any unparseable name, or no valid item of the selected LUID with a counted engine type → `Uncorrelated`;
  - a selected-LUID item with a physical index other than `0` → `Uncorrelated`;
  - items of other LUIDs are ignored;
  - a structurally valid empty-engine instance from a foreign LUID is ignored; the same instance on the selected LUID → `Uncorrelated`;
  - PDH-invalid items are skipped (a process that appeared since the last collection). If every counted item is skipped, the result is `NotYetSampled` on the source's first collection and `Invalid` afterwards;
  - otherwise apply the per-engine sum and cap rules above and return `MeasuredGpuSample` with the maximum.
- Keep the calculator stateless; the source tracks whether it has collected before.

**Gate:** Core tests cover one discrete among integrated adapters, integrated-only, no adapter, software-only, two discrete adapters, LUID byte order, every parse failure, other-LUID instances ignored, non-zero physical index, excluded engine types, the per-engine sum and cap, maximum across engines, each `Invalid` value case, all-skipped first and later collections, and `Uncorrelated` with no matching instance.

### 5. Wrap DXCore and PDH in Desktop adapters

- `IGpuAdapterApi`: enumerate candidates and report whether the current list is stale. `DxCoreAdapterApi` calls `DXCoreCreateAdapterFactory`, creates a list with `DXCORE_ADAPTER_ATTRIBUTE_D3D12_GRAPHICS`, and reads `InstanceLuid`, `DriverDescription`, `IsHardware`, and `IsIntegrated` for each adapter. Check every `HRESULT` and release every COM object.
- `IGpuCounterApi`: `TryCollect(out IReadOnlyList<GpuCounterItem> items, out int status)`. `PdhGpuCounterApi` owns a `SafeHandle` for the query, opened once on first use with the English counter path and reused for every collection. It uses `PDH_MORE_DATA` sizing for `PdhGetFormattedCounterArrayW` and reuses its buffer between polls.
- `GpuLoadSource : IMetricSource` (`Kind = Gpu`). On each `Read()`: re-select only when there is no adapter list yet or the list is stale; map `NoDiscreteAdapter` and `AmbiguousAdapter` to `GpuUnavailableSample(null, reason)` without collecting; otherwise collect and pass the items to `GpuLoadCalculator`. A failed DXCore or PDH call maps to `GpuUnavailableSample(adapter, ReadFailed)`. No GPU failure throws out of `Read()` or faults the collector run (ADR 0011).
- Handle ownership: `GpuLoadSource.Dispose()` closes the query only when no `Read()` is in progress, using an interlocked in-read flag. If a read is pending, it leaves the handle for process exit.
- Add the PDH and DXCore structures that cross the boundary (`PDH_FMT_COUNTERVALUE_ITEM_W`, `LUID`) to `NativeLayoutTests`.

**Gate:** adapter tests with fake APIs prove the failure mapping, re-selection only on a stale list, one query across many reads, and dispose-while-reading leaving the handle open.

### 6. Compose the GPU source

- `App.xaml.cs` constructs `GpuLoadSource` and passes it as the third `MetricMonitor` source. `DesktopStartup` and `ShutdownCoordinator` keep their order: the collector run is still tracked as `metric-monitor`.
- After the tracked run completes within the shutdown deadline, dispose the GPU source; if the run is still pending at the deadline, do not dispose it.

**Gate:** startup and shutdown tests still show `bar-positioned` before `metrics-started`, no publication after `wait-start`, and work area restored before the collector wait.

### 7. Present the GPU section

- Extend `MetricSectionFormat`: available `GPU {n}%` with the CPU rounding rule, unavailable `GPU —`. Tooltips, never containing error codes or exception text:
  - available: `{adapter description}`;
  - `NoDiscreteAdapter`: `No discrete GPU found.`;
  - `AmbiguousAdapter`: `More than one discrete GPU found; none is chosen.`;
  - `Uncorrelated`: `{adapter description}: GPU counters could not be matched to this adapter.`;
  - `NotYetSampled`, `Expired`, `Invalid`, `ReadFailed`: the existing B06 text, prefixed with `{adapter description}: ` when an adapter is known.
- Truncate the adapter description to 64 characters in tooltips. Width and DPI handling follow B06: the section width is fixed to the widest of `GPU 0%` … `GPU 100%` and `GPU —`, and is recomputed on DPI change.
- Section order: CPU, RAM, GPU, then any accepted provider sections. CPU and RAM widths and positions do not change.

**Gate:** formatter tests cover 0, 100, rounding at 0.5/99.5, every reason with and without an adapter, and description truncation.

### 8. Add test-host evidence modes

Add these modes through the production `DesktopStartup` seam and wire each into `LifecycleProcessTests`:

- `--gpu-correlation <seconds>`: runs the production `GpuLoadSource` and, for each read, records the selected adapter, the result, and every counted instance with its parsed LUID, physical index, and engine. It fails if any counted instance's LUID differs from the selected adapter.
- `--metric-freshness gpu`: GPU returns scripted values, then blocks forever; it expires within 0.5 seconds of its six-second deadline while CPU and RAM keep updating. Then exit with the read blocked: the work area is restored before `wait-start` and the process exits within the 2-second deadline.
- `--metric-freshness cpu` and `--metric-freshness ram` gain an updating scripted GPU source, which must keep changing while the other reading expires.
- `--metric-display` cycles GPU through 0%, 100%, and every unavailable reason with and without an adapter.
- `--native-reads` also times the production `GpuLoadSource.Read()`.

**Gate:** all modes pass through `dotnet test` and on the target Windows 11 desktop.

### 9. Prove acceptance or reject

- **Criterion 1:** steps 3–5 tests.
- **Criterion 2:** `--gpu-probe` and a 30-minute `--gpu-correlation` run on the target PC. The report lists every candidate adapter, the selected adapter's description and LUID, and confirms that every counted instance belongs to it.
- **Criterion 3:** on the published app, compare the GPU section with Task Manager's overall utilization for the same adapter, identified by name and, when names repeat, by the LUID-to-adapter mapping from the probe. Use the tolerance fixed above, at idle and under a sustained windowed 3D load. Record the load tool, sample counts, both window means, and the difference.
- **Criterion 4:** on the published app, hover the GPU section in the available state, and run `--metric-display` for each unavailable reason. Tooltips appear without activating the bar.
- **Criterion 5:** step 3 Core tests with a blocked and a failing GPU source, plus the step 8 freshness modes.
- **Criterion 6:** rerun with GPU enabled, on newly identified artifacts: app CPU cost, app memory ceiling, eight-hour memory growth, 30-minute native-read responsiveness (including GPU), freshness and independence, normal and fault shutdown, DPI and rendering at 100%/150%/200%, and artifact hygiene. CPU and RAM accuracy, provider gates, settings failures, and desktop placement scenarios are not affected by a third section; record that reason in the report instead of rerunning them.
- **Rejection path (criterion 7):** if step 2 or criterion 2 cannot prove correlation, ship the source so it reports `Uncorrelated` and the bar shows `GPU —`. Write the rejection report naming the failed evidence, mark criteria 2–3 not applicable, and still pass criteria 1, 4, 5, and 6. Mark B08 `rejected`. This does not change the selection rule and does not block B14.

### 10. Verify and record evidence

Run from the repository root on Windows 11 x64:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

- Run the targeted Core and Desktop test projects ten consecutive times without a flaky failure.
- Build the published app and the test host from one commit; record the commit and SHA-256 of both executables and their loaded production assemblies.
- Create separate dated automated, Windows integration, and manual desktop/resource reports under `docs/acceptance/`, following the sanitization rules in the [first-release gates](../../implementation-plan.md#first-release-acceptance-gates). Adapter descriptions and LUIDs may be recorded; process IDs, process names, and window titles from PDH instances may not. Mark any unrun scenario `unverified`.
- Update the roadmap progress table to `done` or `rejected` only when every applicable criterion has passing evidence.

**Gate:** each criterion in the [B08 brief](../B08-gpu.md#acceptance-criteria) points to passing evidence or, on the rejection path, is marked not applicable with the reason.
