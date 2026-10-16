# Discrete GPU selected by DXCore and correlated by LUID

Status: accepted, 2026-10-16. Applies to B08 and to the GPU rule in [CONTEXT](../../CONTEXT.md#language) and the [product plan](../plan.md#gpu). Replaces the earlier integrated-adapter fallback in those documents. [ADR 0011](0011-exit-when-a-collector-run-faults.md) remains in force.

## Decision

The bar shows GPU load for exactly one discrete adapter and never for an integrated adapter.

- **Enumeration.** Desktop enumerates adapters with DXCore (`DXCoreCreateAdapterFactory`) using the `DXCORE_ADAPTER_ATTRIBUTE_D3D12_GRAPHICS` attribute, so compute-only devices such as NPUs are not candidates. A candidate is discrete when DXCore reports `IsHardware = true` and `IsIntegrated = false`. The software adapter is excluded by `IsHardware = false`.
- **Selection.** One discrete candidate is selected. No discrete candidate makes GPU load unavailable with reason `NoDiscreteAdapter`; more than one makes it unavailable with reason `AmbiguousAdapter`. Neither case falls back to an integrated adapter or picks by order, name, or memory size.
- **Correlation.** GPU load comes from one reused PDH query on `\GPU Engine(*)\Utilization Percentage`, added with `PdhAddEnglishCounterW`. Each instance name carries the adapter LUID as `luid_0x<HighPart>_0x<LowPart>`. A counter instance belongs to the selected adapter only when its LUID equals the DXCore `InstanceLuid` and its physical index is `0`. Anything else that prevents proving this — no matching instance, an unparseable instance name, a matching instance with a non-zero physical index, or no matching engine of the counted types — makes GPU load unavailable with reason `Uncorrelated`.
- **Load.** For each engine of the selected adapter, values are summed across processes. Load is the maximum of those engine totals over the 3D, Compute, Copy, and Video engine types, as defined in CONTEXT.
- **Adapter changes.** Desktop keeps the DXCore adapter list and re-selects only when `IDXCoreAdapterList::IsStale` reports a change. The PDH query is not rebuilt per poll.
- **Handle ownership.** The GPU source owns the PDH query handle for the process lifetime. It closes the handle only when no read is pending; a read still pending at the shutdown deadline leaves the handle for process exit to release, as the [shutdown rules](../implementation-plan.md#settings-and-lifecycle) require.

GPU collection runs in-process on a thread-pool worker, like the first-release CPU and RAM reads, and is measured against the same two-consecutive-overrun responsiveness rule. If it fails that rule, record a new ADR before moving it to a child process.

## Alternatives considered

- **Integrated fallback when no discrete adapter exists.** Rejected by the user on 2026-10-16: the bar shows only the discrete GPU.
- **Pick one of several discrete adapters** by the Windows high-performance preference order or by enumeration order. Rejected: either choice is a guess about which device the user means, and the product plan prefers unavailable to guessed.
- **Classify adapters with DXGI** by `DedicatedVideoMemory` or vendor ID. Rejected: memory size and vendor are heuristics, not an integrated/discrete property.
- **Classify adapters with `D3DKMTQueryAdapterInfo`** (`KMTQAITYPE_ADAPTERTYPE`). Rejected: its hybrid flags are set only on hybrid systems, so they do not classify a single-adapter machine.
- **Read engine running time with `D3DKMTQueryStatistics`.** Rejected: it is documented for driver development rather than as an application monitoring API, and the implementation plan specifies reused PDH queries.
- **Rebuild the PDH query each poll.** Rejected: it adds per-poll allocation and enumeration cost to a counter set that already returns hundreds of instances.

## Acceptance

B08 proves the decision on the target PC: native evidence lists every DXCore candidate with its LUID and flags, the selected adapter, and every PDH instance used for a displayed value with its parsed LUID. If correlation cannot be proven there, B08 follows its rejection path; this ADR does not authorize a different selection rule.
