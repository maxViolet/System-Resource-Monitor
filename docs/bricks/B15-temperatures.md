# B15 — Temperature readings

| | |
|---|---|
| **Area** | Core + Desktop |
| **Depends on** | B14 accepted |
| **Unblocks** | optionally B16 |
| **Sources** | [Product plan — Temperatures](../plan.md#temperatures), [B14 qualification](B14-temperature-source.md), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

An accepted source still needs bounded execution, trustworthy sensor identity, and independent failure handling before its readings can appear on the bar.

## What done looks like

On hardware supported by B14, the bar shows each qualified CPU-package or selected-GPU temperature in degrees Celsius. Unsupported, unqualified, ambiguous, failed, or stale sensors are unavailable and explain why. Temperature work cannot block system metrics or subscription limit snapshots and cannot elevate the bar.

## Context

Before implementation, amend the product plan or add an accepted ADR defining measured polling interval, freshness deadline, plausible value bounds, display format, and accuracy comparison for B14's source. These product-level behaviors are not specified by the current plan and must not be guessed in code.

Keep the qualified adapter in Desktop and expose policy through Core only after the contract exists. A genuinely blocking source runs in a child process with timeout, process-tree termination, and confirmed exit. Package only licensed B14 artifacts.

## Acceptance criteria

- [ ] The missing polling, freshness, value, display, and accuracy contract is accepted before production code is added
- [ ] Tests cover every qualified sensor's identity, unsupported hardware, invalid values, independent failure/expiry, no overlap, cancellation, and late-result rejection
- [ ] Timeout and shutdown behavior matches B14's measured ownership model and the two-second total exit deadline
- [ ] Manual evidence compares each displayed reading with the accepted source/reference under the predeclared tolerance
- [ ] Windows Defender and no-elevation checks pass for the final published payload
- [ ] Affected B07 gates pass with temperatures enabled, including idle CPU, memory ceiling/growth, shutdown, desktop behavior, and artifact hygiene

## Out of scope

Per-core temperatures, fan control, overclocking, a privileged helper, and guessed values. If B14 is rejected, this brick is not applicable rather than simulated.
