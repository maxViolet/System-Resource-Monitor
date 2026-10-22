# Qualify a Claude limit source package

- [Brick brief](../B12-claude-source.md): scope, dependencies, acceptance criteria, and exclusions.
- [Implementation plan](plan.md): ordered qualification steps, gates, and the ADR 0017 handoff to B013.
- Historical strict-gate outcome: [2026-10-20 report](../../research/providers/claude-2026-10-20.md).
- Current source decisions: [ADR 0018](../../adr/0018-claude-usage-client-output.md) and [2.1.304 follow-up](../../research/providers/claude-2026-10-21.md); [ADR 0020](../../adr/0020-qualify-claude-code-2-1-306.md) and [2.1.306 follow-up](../../research/providers/claude-2.1.306-2026-10-21.md); [ADR 0022](../../adr/0022-claude-code-minimum-version.md) for the minimum client version. B013 implements the official-client output contract and retains unverified manual gates explicitly.

The brick brief and its linked product plan, implementation plan, and ADRs remain authoritative. Package files may make the work executable for coding agents, but must not silently change scope or acceptance criteria.
