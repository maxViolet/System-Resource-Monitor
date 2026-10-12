# B07 — First-release evidence and self-contained publication

| | |
|---|---|
| **Area** | Release |
| **Depends on** | B06 |
| **Unblocks** | B08, B09, B10, B11, B16 |
| **Sources** | [Product plan — Release rule](../plan.md#release-rule), [First-release acceptance gates](../implementation-plan.md#first-release-acceptance-gates), [Automated verification and handoff](../implementation-plan.md#automated-verification-and-handoff) |

## Why

Implementation and tests do not establish desktop correctness or resource cost. This brick creates the two named artifacts, runs every release-blocking gate, and leaves reproducible evidence for the first personal release.

## What done looks like

A clean self-contained Windows 11 x64 folder starts without a separately installed .NET runtime, elevation, or provider authorization and exposes only the accepted CPU/RAM behavior. Dated reports identify the common source commit and SHA-256 of the published executable and test-host executable, and every first-release gate passes against its required artifact.

## Context

Build the published Desktop folder and separate Release test host from one commit. Keep test-host injection code, fixtures, secrets, development configuration, and diagnostic hooks out of the published folder. Document extraction, launch, top/bottom position, exit, settings failures, and unavailable readings. Record the commit; SHA-256 of both executables and their loaded production assemblies; PC/CPU, Windows build, logical processor count, display/DPI/taskbar configuration; instrumentation, timestamps, raw observations, and pass/fail.

Use separate automated, Windows integration, and manual desktop reports under `docs/acceptance/`. During the eight-hour run, keep monitoring active and record interruptions and workload changes; missing samples invalidate the run.

## Acceptance criteria

- [ ] Locked restore, Release build, tests, self-contained publish, and `git diff --check` succeed using the commands in the implementation plan
- [ ] Reports record all artifact and environment identity fields above and clearly bind every gate to the correct artifact
- [ ] CPU accuracy, RAM accuracy, native-read responsiveness, freshness/independence, normal shutdown, fault shutdown, and every B04 desktop scenario pass
- [ ] Thirty-minute idle CPU cost is `<= 0.5%` by the plan's whole-machine-normalized formula
- [ ] Private bytes never exceed 150 MiB in the 30-minute and eight-hour runs
- [ ] After a 30-minute warm-up, last-window minus first-window mean private bytes is `<= 10 MiB` across a complete eight-hour run
- [ ] The published folder runs on the target Windows 11 x64 PC without a separately installed .NET runtime or UAC and contains none of the prohibited test/development material
- [ ] With CPU/RAM monitoring active, injected settings read, backup, and write failures do not prevent monitoring; failed persistence shows the restart-only message
- [ ] User documentation covers extraction, launch, position, exit, settings-error behavior, and unavailable readings

## Out of scope

GPU, installation/autostart, subscription limits, temperatures, and public distribution are independent later tracks. Failing one of those later tracks cannot revoke this accepted release.
