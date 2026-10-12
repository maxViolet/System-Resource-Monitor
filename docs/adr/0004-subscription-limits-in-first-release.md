# Include subscription limits in the first release

Status: accepted, 2026-10-12. Qualifies the first-release scope rationale in [ADR 0002](0002-appbar-first-cpu-ram-release.md); its two-project architecture remains in force.

## Decision

The first personal release (v1) shows CPU load, RAM usage, and the remaining subscription limits of Codex and Claude.

- Codex and Claude source qualification starts after the placement gate passes and runs in parallel with CPU/RAM work.
- v1 cannot be accepted until both qualifications close as accepted or rejected.
- An accepted provider must be implemented and pass every provider gate before v1 is accepted; its adapter is then part of every first-release resource, shutdown, desktop, and artifact gate.
- A rejected provider has no section in v1. Its absence does not block v1, and no simulated or placeholder data is shown.

GPU, installation/autostart, and temperatures remain independent tracks after v1.

## Reason and alternatives

Remaining limits are a primary reason to keep the bar visible, so a CPU/RAM-only first release defers the main user value.

- **CPU/RAM-only v1, limits later:** smallest risk, but delivers the less important information first.
- **Require both providers for v1:** guarantees limits but makes v1 depend on whether each vendor exposes a qualifying non-credential, non-consuming source; the plan forbids weakening that source boundary to force acceptance.
- **Include each provider whose qualification is accepted (chosen):** delivers limits when an allowed source exists while keeping the security boundary and a finite release path.

The cost is a longer v1 critical path: qualification research, two adapters, manual account checks, and resource gates with provider polling enabled.
