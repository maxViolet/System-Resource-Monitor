# B015 — Implement the shared bar redesign

| | |
|---|---|
| **Area** | Desktop + acceptance |
| **Depends on** | B014 done |
| **Unblocks** | B019; optionally B018 |
| **Sources** | [B014 current-bar baseline](B14-current-bar-baseline.md), [Product plan — Shared bar design](../plan.md#shared-bar-design), [Implementation plan — Shared bar redesign](../implementation-plan.md#shared-bar-redesign), [First-release acceptance gates](../implementation-plan.md#first-release-acceptance-gates), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

The approved shared design covers the present resource and limit sections as well as the later task and tray phases. The current WPF bar arranges its sections in one left-aligned `StackPanel`; B015 replaces that structure with explicit regions for the later phases without changing the first-release measurement or provider contracts.

## What done looks like

The published bar keeps its current appearance while its WPF layout has three regions: fixed-width system metric and qualified-limit sections on the left, a flexible empty centre, and an empty right-hand host. B019 fills the centre and adds the clock; B020 extends the right-hand host. The existing native tray icon retains position and exit actions. Section text, unavailable states, tooltips, AppBar reservation, fullscreen behavior, and pointer focus behavior continue to meet their accepted contracts.

## Context

Use the [HTML prototype](../../prototype/task-strip-design.html) as a visual reference, not as measured WPF or Windows evidence. B015 changes the WPF layout structure, so the prototype does not depict a new B015 visual state. Compare against the [B014 measured baseline](B14-current-bar-baseline.md), including its recorded failures and discrepancies. A B014 failure that also blocks B015 acceptance must be resolved by its named owner before B015 is marked `done`; layout parity alone does not waive a resource gate. Preserve `#1E1E1E` background, `#E6E6E6` primary text, and 12-DIP Segoe UI. Keep each qualified section's fixed width and omit unqualified provider sections. Record actual rendered bounds at each supported DPI with the widest documented values and unavailable states. Exercise the centre and right hosts with test-host-only placeholder content; the published app leaves them empty until B019 and B020.

If the real WPF width cannot contain the current fixed sections on a target display, record the measured limit and resolve it through the documentation and ADR process before clipping a section or changing its contract. Rerun affected first-release desktop, rendering, and resource gates on the exact published artifact.

## Acceptance criteria

- [ ] The published WPF bar retains the current visual state at 100%, 150%, and 200% scaling on Windows 11; a dated report records redacted bar crops, rendered bounds, monitor width, DPI, and source commit/artifact identity
- [ ] The WPF layout has separate fixed left, flexible centre, and right-hand regions; test-host-only content can fill the centre and right without moving or resizing the fixed left sections, while neither placeholder appears in the published app
- [ ] At both top and bottom edges, the bar remains 24 DIP high, reserves the correct work area, and retains the accepted fullscreen and Explorer-recovery behavior
- [ ] CPU, RAM, optional GPU, and every qualified provider section keep their documented fixed widths, text, tooltips, unavailable states, and order; unqualified providers have no section
- [ ] Widest documented values and unavailable states fit without clipped fixed sections at the measured supported widths; unsupported narrower widths are recorded rather than silently clipped
- [ ] Pointer interaction does not activate the visible bar; its separate native tray icon still switches edge and exits
- [ ] Targeted layout/format tests and affected B007 rendering, desktop, CPU, and memory gates pass with the redesigned bar enabled; automated, Windows integration, and manual/resource evidence remain distinct

## Out of scope

Task enumeration, task buttons, keyboard task access, overflow, and the clock belong to B019. The in-bar tray, shell indicators, and hiding the Windows taskbar belong to B020. B015 does not change metric collection, provider acquisition, Core/Desktop ownership, section display semantics, or the first-release acceptance thresholds.
