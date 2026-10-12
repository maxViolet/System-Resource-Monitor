# B01 — Solution and enforced Core boundary

| | |
|---|---|
| **Area** | Core + Desktop |
| **Depends on** | — |
| **Unblocks** | B02 |
| **Sources** | [Implementation plan — Technology and layout](../implementation-plan.md#technology-and-layout), [ADR 0002](../adr/0002-appbar-first-cpu-ram-release.md) |

## Why

Every later brick needs a runnable .NET 10 solution and an enforced separation between platform-independent policy and Windows behavior. Creating only the required projects prevents speculative architecture while preserving the seam needed for fault injection.

## What done looks like

`SystemResourceMonitor.slnx` restores, builds, and tests on Windows 11 x64. Core targets plain `net10.0`; Desktop targets `net10.0-windows`; the test host starts independently and is ready to compose production lifecycle components introduced by later bricks.

## Context

Create the two production projects, Core and Desktop, plus Core tests, Desktop tests, and `SystemResourceMonitor.Desktop.TestHost`. Enable nullable reference types and warnings as errors, pin the SDK in `global.json`, and commit package lock files.

Desktop references Core; Core has no Desktop dependency. Add `Microsoft.CodeAnalysis.BannedApiAnalyzers` to Core, include `BannedSymbols.txt`, and make `RS0030` an error. Ban the file, directory, process, registry, and interop symbols named in the implementation plan. Keep WPF, Win32, settings I/O, and composition in Desktop.

## Acceptance criteria

- [ ] The locked restore, Release build, and test commands from the implementation plan succeed for `win-x64`
- [ ] An automated architecture check enumerates project references, permits Desktop → Core, and fails if Core references Desktop
- [ ] Temporary negative probes for every banned API family fail Core compilation with `RS0030`; the probes are removed afterward
- [ ] The test-host executable starts and exits independently of the published Desktop artifact
- [ ] No provider, GPU, temperature, repository, mediator, or generic result abstraction is introduced

## Out of scope

A window, AppBar registration, tray icon, settings, and lifecycle behavior belong to B02 and B03. System metric collection begins in B05.
