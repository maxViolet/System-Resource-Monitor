# Resource packages within Core and Desktop

Status: accepted, 2026-10-13. Supersedes only the source-directory layout in [ADR 0002](0002-appbar-first-cpu-ram-release.md); the two-project dependency decision remains in force.

## Decision

Within each existing production project, group feature-specific source under `ai-resources/` and `system-resources/`. The former owns qualified Codex and Claude subscription limit behavior; the latter owns system metric behavior. Core keeps platform-independent values, policy, and source contracts. Desktop keeps concrete source adapters and feature-specific presentation. These directories do not create additional assemblies or project references.

Keep shared AppBar, tray, settings, and `App.xaml.cs` composition outside these packages. Tests remain in the existing test projects. Directory names are not C# namespace segments; use valid namespaces in future C# files.

This changes ADR 0002's proposed `Desktop/Adapters/<source>/` layout for feature-specific adapters. It does not change the Core/Desktop seam or authorize new source behavior before its brick is accepted.

## Alternatives considered

- New projects for AI and system behavior would enforce more compile-time seams, but would expand the five-project B01 foundation before either feature needs a separate assembly.
- Layer-only directories such as `Metrics/`, `Monitoring/`, and `Adapters/` keep implementation types together, but scatter one feature's policy and adapters across unrelated directory trees.

The two package directories make the two work tracks discoverable while preserving the existing project boundary.
