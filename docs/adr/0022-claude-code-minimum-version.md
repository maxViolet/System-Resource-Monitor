# Accept Claude Code 2.1.304 and later for the `/usage` source

Status: accepted, 2026-10-22. Supersedes the exact-version pin of [ADR 0018](0018-claude-usage-client-output.md) (version 2.1.304 only) and the version allowlist of [ADR 0020](0020-qualify-claude-code-2-1-306.md) (2.1.304 and 2.1.306). Their source, path, signature, owner, lease, argument, envelope, row, and failure rules remain in force, and [ADR 0019](0019-claude-child-image-load-policy.md) still applies to every accepted version. Applies to the [subscription limits](../plan.md#3-deliver-subscription-limits) stage, [B012](../bricks/B12-claude-source.md), and [B013](../bricks/B13-claude-limits.md).

## Context

The native client updates itself. On 2026-10-22 it moved from 2.1.306 to 2.1.307, and the exact-version check made the Claude section unavailable with "not qualified" until a new ADR, report, and release. Each update would repeat this, so the section is unavailable for most of the time a user runs the latest client.

The version pin was meant to catch two changes: a changed `/usage` text format and a `/usage` command that starts model work. The adapter already checks both on every poll. The parser accepts only the recognized rows and makes any other text unknown format. The envelope check requires `local_command: usage`, `num_turns: 0`, `duration_api_ms: 0`, and `total_cost_usd: 0`, so a poll that used the model or the API is rejected.

## Decision

- Accept every Anthropic-signed native client at the ADR 0018 fixed path whose file version parses and is 2.1.304.0 or later. Reject an older or unreadable version before the signature check and before execution.
- Keep the fixed path, canonical-path, owner, signature, file-lease, and ADR 0019 image-loading checks on every poll.
- Keep the ADR 0018 envelope and row contract unchanged for every version. A changed row, a new row, or an envelope that shows model or API work is unknown format, and the section is unavailable immediately.
- The unsupported-version tooltip names the minimum: "The installed Claude Code version is not supported. Requires 2.1.304 or later."
- The controlled ten-poll non-consumption observation stays an acceptance check for the B012 and B013 releases, not a condition for each later client version.

## Alternatives

- Qualify each new version by ADR, as ADR 0020 did: catches every change before display, but the section is unavailable after every client update until a release ships.
- Accept only later 2.1.x versions: reduces the risk from a larger client change, but a minor-version update would again make the section unavailable until a release ships.
- Stop polling after the first envelope that shows model or API work: limits a consuming client to one poll. Rejected for now as extra state for a case not yet observed; the first such poll is already unavailable and visible.

## Consequences

A client update no longer makes the Claude section unavailable when the `/usage` output is unchanged. A changed format shows as unknown format instead of an unsupported version. If a later client makes `/usage` start model or API work, the bar shows Claude unavailable from the first such poll, but each poll still runs the client once a minute and could use account limits until the user or a release reacts. Later versions are not observed before use; the dated B012 reports describe only the versions they name.
