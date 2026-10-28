# Ignore qualified local insights after Claude subscription limits

Status: accepted, 2026-10-27. Narrows the changed-row rejection in [ADR 0022](0022-claude-code-minimum-version.md) for the identified local insights block in Claude Code 2.1.311. The [ADR 0018](0018-claude-usage-client-output.md) subscription rows, non-consuming JSON envelope, client trust checks, and unknown-format failure policy remain in force. Applies to [B012](../bricks/B12-claude-source.md) and [B013](../bricks/B13-claude-limits.md).

## Context

Claude Code 2.1.311 appends a local attribution section to `/usage` after the subscription windows. It explicitly describes its percentages as approximate shares of sessions on this machine that overlap. These percentages describe local activity, not subscription limits. The existing fail-closed parser rejects any percentage outside a recognized `Current ...` window, making the Claude section unavailable even though the qualified window rows and zero-work envelope are unchanged. The [dated observation](../research/providers/claude-2.1.311-2026-10-27.md) records the sanitized shape.

## Decision

- Accept the local insights block only after the required subscription rows, with either observed heading `What's contributing to your limits usage?` or `What's contributing to your usage?`, followed by the notice `Approximate · sessions on this machine only · shares overlap`.
- Ignore only the observed bounded line grammar inside that block: `Last Nh/Nd · N requests · N sessions` with optional thousands separators and singular forms; the `Start fresh`, `Turn off skills`, `Compact before you step away`, and `Run /model on a cheaper model` advice rows; and `Top skills` or `Top subagents` lists with integer shares from 0 through 100. Names and slash commands may contain ASCII letters, digits, `_`, `.`, `:`, and `-`; skill and subagent names are never displayed or stored in the snapshot.
- Reject a missing notice, an unrecognized line or percentage, a new subscription window, or a changed JSON envelope as unknown format. Do not use local attribution percentages to choose the binding weekly window or infer a monthly limit.
- Keep the existing one-minute polling, trust checks, and non-consumption gates. A later client that changes the insights grammar requires another qualification.

## Alternatives

- Continue rejecting every new percentage-bearing row: safe but leaves the known 2.1.311 subscription limits unavailable.
- Ignore every row after the subscription windows: rejected because a new monthly or model-specific limit could be silently missed.
- Parse local insight shares as subscription limits: rejected because the client labels them approximate and local to this machine.

## Consequences

The Claude adapter can show qualified subscription limits on the observed 2.1.311 output. Parser tests cover the accepted insights block and reject unknown percentage-bearing rows. The controlled ten-poll non-consumption and published-app manual gates remain separate release evidence.
