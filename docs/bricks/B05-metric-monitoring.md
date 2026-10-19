# B005 — Independent metric monitoring

| | |
|---|---|
| **Area** | Core |
| **Depends on** | B004 |
| **Unblocks** | B006 |
| **Sources** | [Implementation plan — Initial model and workflows](../implementation-plan.md#initial-model-and-workflows), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

CPU and RAM must remain independent under failure, slowness, and expiry. This policy belongs in Core and must be deterministic before Windows adapters and WPF presentation are connected.

## What done looks like

Given fake sources and time, Core polls CPU and RAM every two seconds without overlapping a source's own reads. Each successful reading updates immutable dashboard state. At age six seconds, that reading becomes unavailable independently of the other source, even if its collector is blocked.

## Context

`MetricReading` is an explicit union. `AvailableMetric` carries kind, a finite percentage from 0 through 100, UTC `MeasuredAt`, a monotonic timestamp from `TimeProvider.GetTimestamp()`, and, for RAM, used and total physical bytes. `UnavailableMetric` carries kind and one reason: `NotYetSampled`, `Expired`, `Invalid`, or `ReadFailed`. `DashboardState` is immutable and emits one change notification for each accepted reading or availability transition, including expiry.

Inject `TimeProvider`; use UTC only for display and evidence and the monotonic timestamp for age and deadlines. Do not add `IClock`. Failed reads do not renew either timestamp. Known-invalid values become `Invalid` immediately. Cancellation stops new polls and rejects late results but does not overlap or pretend to interrupt synchronous native work. Notifications cross to WPF once at the Desktop boundary.

## Acceptance criteria

- [ ] Tests cover percentage bounds, finite values, RAM byte invariants, each unavailability reason, and valid zero as distinct from unavailable
- [ ] Exact-boundary tests prove availability before six seconds and `Expired` at monotonic age `>= 6s`, unaffected by wall-clock changes
- [ ] A blocked CPU source expires while RAM continues updating, and the inverse also passes
- [ ] Slow reads never overlap for the same source and never prevent the other source's schedule or expiry
- [ ] Failed reads do not renew measurement time; known-invalid readings become unavailable immediately
- [ ] Cancellation prevents new scheduling and publication of late results
- [ ] Core remains free of Windows, WPF, file, registry, process, and interop dependencies

## Out of scope

`GetSystemTimes`, `GlobalMemoryStatusEx`, WPF controls, tooltips, and native-duration measurements belong to B006.
