# B06 — CPU/RAM sources and bar presentation

| | |
|---|---|
| **Area** | Core + Desktop |
| **Depends on** | B05 |
| **Unblocks** | B07 |
| **Sources** | [Product plan — Deliver CPU/RAM](../plan.md#2-deliver-cpuram), [Implementation plan — Slice 1](../implementation-plan.md#slice-1-first-personal-cpuram-release), [First-release acceptance gates](../implementation-plan.md#first-release-acceptance-gates) |

## Why

This brick turns the reliable placement shell into the intended first-release bar while keeping native acquisition isolated from policy and UI state.

## What done looks like

The bar shows fixed-width `CPU 12%` and `RAM 64%` sections on a thin dark surface. CPU comes from `GetSystemTimes` deltas; RAM comes from `GlobalMemoryStatusEx`. The RAM tooltip shows used/total GiB. An unavailable reading shows `—` and a tooltip explaining the missing update while the other reading continues normally.

## Context

Run both short synchronous native calls on background thread-pool workers. The first CPU sample is unavailable; reset or invalid deltas discard the baseline. Calculate RAM usage from physical `(total - available) / total`. Check return codes, sizes, pointer widths, and ownership. Neither wrapper retains a native handle.

Marshal collector results to the WPF dispatcher before updating UI-bound state. The two-second polling, six-second freshness, no-overlap, and late-result rules come from B05. The 500 ms fullscreen reconciliation remains active and counts toward resource measurements.

## Acceptance criteria

- [ ] CPU delta arithmetic, first-sample behavior, counter reset, and invalid-delta behavior have deterministic tests
- [ ] Windows integration checks exercise both production native wrappers and their failure paths
- [ ] The actual bar displays fixed-width CPU/RAM text, RAM GiB details, and explanatory unavailable states
- [ ] Fault injection visibly expires one reading within 0.5 seconds of its six-second deadline while the other keeps updating
- [ ] The published app remains responsive throughout desktop and measurement runs

## Out of scope

First-release accuracy, 30-minute native-read responsiveness, eight-hour resource gates, artifact hygiene, user documentation, and the release acceptance report belong to B07. GPU, subscriptions, and temperatures have no placeholder sections in this brick.
