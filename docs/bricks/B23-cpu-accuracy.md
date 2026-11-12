# B023 — Measure and improve CPU accuracy against Task Manager

| | |
|---|---|
| **Area** | Desktop + Core when a calculation changes + acceptance evidence |
| **Depends on** | B007 |
| **Unblocks** | — |
| **Sources** | [Implementation plan — CPU accuracy](../implementation-plan.md#cpu-accuracy), [Initial model and workflows](../implementation-plan.md#initial-model-and-workflows), [First-release acceptance gates](../implementation-plan.md#first-release-acceptance-gates), [B014 manual and resource report](../acceptance/2026-11-12-B14-manual-desktop-resources.md), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

The bar computes CPU load from `GetSystemTimes` busy-time deltas, while the CPU accuracy gate compares it with Task Manager. B014 did not observe Task Manager. Its supplementary scripted comparison found the bar within 0.15 points of the `% Processor Time` counter but 11–14 points from the frequency-scaled `% Processor Utility` counter under sustained load, against a 5-point budget. RAM matched within 2 points in 30 of 30 pairs but was also not compared with Task Manager.

## What done looks like

The reference Task Manager figure on the target PC is measured, the cause of any difference from the bar is shown with evidence, and the bar's CPU reading meets the CPU accuracy gate in both regimes, or the gate records an explicit `fail` with its reason. A change of measurement method or gate reference is recorded in an ADR before code.

## Context

Measure first. For 30 paired observations at 2-second intervals in an idle and a sustained-load regime, record the bar, Task Manager, `% Processor Time`, and `% Processor Utility` in the same 60-second windows, and record `% Processor Performance` and the power source to test the frequency-scaling explanation. Then compare candidate sources: the current busy-time method, the documented PDH utility counter (with a defined rule for values above 100%), and any other documented source. Compare accuracy, polling cost, failure classification, freshness, and fit with the Core/Desktop boundary; native calls stay in Desktop.

If the bar should follow a different measurement, or the gate should use a different reference, create a new ADR with the alternatives and update the implementation plan before changing code. Keep the 2-second polling, no-overlap, 6-second expiry, and unavailable-reason contracts. Use the B014 identity fields for every report.

## Acceptance criteria

- [ ] A dated report records the Task Manager comparison in both regimes on a named published artifact, with source commit, executable hashes, Windows build, hardware category, logical processor count, and power source
- [ ] The cause of any CPU difference between the bar and Task Manager is stated with measured evidence
- [ ] A new ADR records the chosen measurement method or gate reference and its alternatives whenever either changes, and the implementation plan is aligned
- [ ] The CPU accuracy gate passes within 5 points in the idle and sustained-load regimes, or records `fail` with its reason; transition spikes are recorded separately
- [ ] The RAM accuracy gate is compared with Task Manager in both regimes and recorded as `pass` or `fail`
- [ ] Tests cover any new calculation, value bound, and unavailable reason; the 2-second no-overlap and 6-second freshness rules still hold
- [ ] Automated checks follow [automated verification and handoff](../implementation-plan.md#automated-verification-and-handoff), and the native-read responsiveness gate is rerun when the CPU source changes

## Out of scope

RAM collection logic, CPU/RAM text format and section widths, GPU, provider limits, resource budgets, and the bar layout. B023 does not change B014's recorded results. It is a separate track and is not a B015 dependency, but it does not waive B015's gates: an unresolved CPU-gate failure still prevents B015 acceptance.
