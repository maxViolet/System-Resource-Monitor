# B02 — Empty AppBar lifecycle

| | |
|---|---|
| **Area** | Desktop |
| **Depends on** | B01 |
| **Unblocks** | B03 |
| **Sources** | [Product plan — Validate placement first](../plan.md#1-validate-placement-first), [Implementation plan — Settings and lifecycle](../implementation-plan.md#settings-and-lifecycle), [ADR 0001](../adr/0001-separate-windows-appbar.md), [ADR 0002](../adr/0002-appbar-first-cpu-ram-release.md) |

## Why

AppBar feasibility is the release's first risk. This brick proves the basic reservation and cleanup lifecycle before system metrics or optional-source work can hide placement failures.

## What done looks like

Launching the Desktop app creates one thin, borderless, non-activating bar on the primary monitor and reserves its strip. A second launch creates no second window or reservation. Normal exit and injected handled startup failure restore the previous work area before any bounded background-work wait.

## Context

Declare `PerMonitorV2` DPI awareness in `app.manifest`. On the owning WPF thread, register with `ABM_NEW` and a `RegisterWindowMessage` callback, negotiate with `ABM_QUERYPOS`, and commit with `ABM_SETPOS`. Treat AppBar rectangles as physical pixels and apply them with `SetWindowPos(..., SWP_NOACTIVATE)`. Keep HWND, `SetWindowPos`, message handling, monitor/DPI work, and AppBar calls on that thread. Check native return values and structure sizes.

Startup acquires the single-instance guard before any settings access or bar registration; a second process exits without reading or writing settings. `App.xaml.cs` owns cancellation and shutdown; no Generic Host. Cleanup is idempotent: stop state publication, cancel work, and call `ABM_REMOVE` immediately in a `finally` path, then wait no more than two seconds total. Late results cannot publish. The first-release native readers introduced later will own no long-lived handles. The placement investigation's two-working-day clock starts when work on this brick starts and runs across B02–B04.

## Acceptance criteria

- [ ] The empty published app reserves exactly one strip on the primary monitor without requesting elevation
- [ ] Opening a second process leaves one process, one bar, and one reservation, and does not touch settings files
- [ ] The bar never takes keyboard focus during launch or interaction with another application
- [ ] Normal exit restores the prior work area and leaves no background process
- [ ] A test-host injected handled startup failure restores the prior work area
- [ ] The placement investigation start timestamp is recorded so B04 can enforce the single two-working-day limit across B02–B04

## Out of scope

Tray actions, persisted edge selection, Explorer recovery, display changes, and fullscreen behavior belong to B03 and B04. CPU and RAM do not appear yet.
