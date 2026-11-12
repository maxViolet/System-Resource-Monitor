# B014 — Record the current bar baseline

| | |
|---|---|
| **Area** | Desktop + acceptance evidence |
| **Depends on** | B007 |
| **Unblocks** | B015 |
| **Sources** | [Product plan — Shared bar design](../plan.md#shared-bar-design), [Implementation plan — Current bar baseline](../implementation-plan.md#current-bar-baseline), [First-release acceptance gates](../implementation-plan.md#first-release-acceptance-gates), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

The current published WPF bar needs a measured baseline before B015 changes its layout. B007 has no complete set of acceptance reports in this repository. The prototype's Current view illustrates the intended appearance but its sample values and CSS dimensions cannot establish WPF bounds or Windows behavior.

## What done looks like

A dated, sanitized baseline identifies the exact published app and separate test host built from one source commit. It records the current bar's rendered sections and supported widths, the outcome of each applicable Windows 11 desktop and resource gate, and every failure or unverified scenario. Each non-pass gate has a named open owner before B014 closes. B015 can compare its redesigned bar against those observations. Completing this evidence brick does not turn a failed first-release gate into a pass.

## Context

Measure the actual left-aligned WPF bar at both edges and 100%, 150%, and 200% scaling. Record the bar's 24-DIP height, section order and device-pixel bounds, widest documented text, 0% and 100% states, active countdowns, stale and unavailable states, accepted-provider clients present and absent, RAM used/total GiB tooltip, native tray actions, focus behavior, AppBar reservation, fullscreen and Explorer recovery. Record unsupported monitor widths without accepting clipping. Compare the visual result with the [HTML prototype](../../prototype/task-strip-design.html) only as an illustration; record differences rather than treating its pixel widths or example values as requirements.

Run the applicable [first-release acceptance gates](../implementation-plan.md#first-release-acceptance-gates) on the named artifacts, including the 30-minute and eight-hour resource runs with accepted providers enabled. The [B011 resource report](../acceptance/2026-10-20-B11-manual-desktop-resources.md) already records a combined Codex peak above the 150-MiB budget; this historical result is not a new B014 measurement or a waived threshold. A new failure stays `fail`, and an interrupted or unavailable run stays `unverified`.

The current code may show an unavailable provider section even when its source is unqualified. Record the observed behavior; B015 or a separate FIX task owns correction. B014 does not change production code or declare B008, B011, or B013 complete.

## Acceptance criteria

- [ ] Separate dated automated, Windows integration, and manual/resource reports identify the source commit, published executable and loaded production-assembly SHA-256 values, separate Release test-host identity, Windows build, monitor geometry, DPI, taskbar configuration, and logical processor count
- [ ] Real WPF section bounds, widest documented values, tooltips, supported widths, and clipping outcomes are recorded at both edges and 100%, 150%, and 200% scaling; the prototype is labeled visual reference only
- [ ] Each applicable first-release desktop, rendering, provider, shutdown, artifact-hygiene, CPU, and memory gate has a named artifact and an explicit `pass`, `fail`, or `unverified` result with measurements or a reason; missing B007 reports are not called reruns
- [ ] Fullscreen, Explorer restart, primary-monitor change, pointer focus, native tray position and Exit actions, and work-area restoration have Windows 11 observations; unrun scenarios remain `unverified`
- [ ] Accepted-provider clients present and absent, optional GPU, and every observed discrepancy from the documented current-bar contract are recorded without closing another brick's criteria
- [ ] Every `fail` or `unverified` first-release gate is assigned to a named open brick or follow-up task before B014 is marked `done`; the known Codex combined-memory gap remains with B011 until resolved
- [ ] Committed evidence contains only aggregate, sanitized results and redacted bar crops; raw account output, window titles, usernames, absolute user paths, and unredacted screenshots stay out of the repository

## Out of scope

Implementing the three-region layout and correcting provider-section omission belong to B015 or a separate FIX task. B014 does not alter metric collection, provider acquisition, the Core/Desktop boundary, AppBar policy, first-release thresholds, or any unfinished B008, B011, or B013 acceptance criterion. Task buttons and clock belong to B019; in-bar tray, shell indicators, and Windows taskbar hiding belong to B020.
