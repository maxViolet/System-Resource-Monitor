# Group resource projects by package

Status: accepted, 2026-10-13. Supersedes the directory layout in [ADR 0006](0006-resource-packages-within-projects.md) and the two-project count and combined Core ownership in [ADR 0002](0002-appbar-first-cpu-ram-release.md). The Core/Desktop dependency direction and AppBar choice remain in force.

## Decision

Use top-level `src/system-resources/` and `src/ai-resources/` packages. Move the existing `SystemResourceMonitor.Core` and `SystemResourceMonitor.Desktop` projects together into `system-resources/`. The existing Core project is the system metric Core. Add a separate plain `net10.0` `SystemResourceMonitor.AIResources.Core` project under `ai-resources/` for future qualified subscription limit policy and contracts.

The Desktop application remains the single WPF process and composition root. It references the system Core now and may reference accepted AI projects as their bricks are implemented. System metric state belongs to the system Core, AI limit policy to the AI Core, and combined display state to Desktop. Concrete AI source adapters belong under `ai-resources/` when qualified; do not put them in the system package. The new AI Core has the same direct-use banned-API enforcement as the system Core. Test projects remain under `tests/`.

No AI provider interface, reader, or UI behavior is introduced by this layout change. Further AI projects require an accepted source and a concrete implementation need.

Commands that target the Desktop project now use `src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj`; earlier ADRs and B001 evidence retain the paths that were valid when recorded.

## Alternatives considered

- Nested `ai-resources/` and `system-resources/` directories inside each Core/Desktop project leave the shared Desktop and Core projects outside either top-level package and mix the two work tracks at the project level.
- Two complete Core/Desktop project pairs would create a second WPF application or a premature AI adapter assembly before any provider source qualifies.

The chosen layout makes system ownership explicit now and leaves the AI package ready without creating unused desktop behavior.
