# B08 — GPU adapter selection, correlation, and display

| | |
|---|---|
| **Area** | Core + Desktop |
| **Depends on** | B07 |
| **Unblocks** | B14; optionally B16 |
| **Sources** | [Product plan — Add GPU after the first release](../plan.md#3-add-gpu-after-the-first-release), [Implementation plan — Later slices](../implementation-plan.md#later-slices), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

GPU counters are useful only when they can be tied to the intended physical adapter. Guessing from counter order or names would present a trustworthy-looking value for the wrong device.

## What done looks like

The bar shows GPU load for the discrete adapter, or the integrated adapter when no discrete adapter exists. The value is the maximum across 3D, Compute, Copy, and Video engines. If adapter/counter correlation is not reliable, GPU is unavailable rather than guessed.

## Context

Add GPU contracts only in this brick. Keep adapter enumeration and PDH interop in Desktop, reuse queries rather than rebuilding them per poll, and publish through the existing Core monitoring seam. Record selected adapter identity, correlation method, Task Manager comparison method, sample count, and numeric tolerance before declaring acceptance.

If reliable correlation cannot be established, record the ambiguity, show GPU as unavailable, and mark this brick `rejected`. Rejection does not authorize a different adapter-selection rule and does not block B14.

## Acceptance criteria

- [ ] Deterministic tests cover discrete-over-integrated selection, integrated fallback, engine maximum, invalid samples, and unavailable correlation
- [ ] Native integration evidence identifies the selected adapter and proves every displayed counter belongs to it
- [ ] Idle and load observations compare the same adapter with Task Manager using a recorded sample count and predeclared tolerance
- [ ] The GPU tooltip identifies the selected adapter and explains unavailable correlation
- [ ] Existing CPU/RAM independence and freshness behavior remains passing when GPU fails or blocks
- [ ] All affected B07 gates, including CPU and memory budgets with GPU enabled, pass and are recorded
- [ ] If correlation cannot be proven, the rejection report identifies the failed evidence, the bar shows unavailable rather than a guessed value, and positive correlation/accuracy criteria are marked not applicable

## Out of scope

Temperature sensors belong to B14–B15. Multi-adapter display and guessed aggregation across adapters are not authorized by the product plan.
