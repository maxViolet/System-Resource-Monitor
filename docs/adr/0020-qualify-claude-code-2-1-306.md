# Qualify Claude Code 2.1.306 for the `/usage` source

Status: accepted as a version allowlist extension, 2026-10-21; the version-specific non-consumption gate remains open. Extends the version list in [ADR 0018](0018-claude-usage-client-output.md); its source, format, security, and failure rules remain in force. [ADR 0019](0019-claude-child-image-load-policy.md) also applies to this version.

## Context

The native client at the fixed production path updated from 2.1.304 to 2.1.306. The existing exact-version check correctly made the Claude section unavailable. The signed 2.1.306 client still supplies numeric subscription limits through the same official `/usage` command. The [dated version report](../research/providers/claude-2.1.306-2026-10-21.md) records the observed response and the validation boundary.

## Decision

- Accept file versions `2.1.304.0` and `2.1.306.0` at the ADR 0018 fixed path. Continue to reject every other version before execution. Keep the canonical path, owner, signature, file lease, and ADR 0019 child image-loading policy checks on every poll.
- Parse 2.1.306 through the unchanged bounded ADR 0018 envelope and row contract. A new or malformed row remains unavailable. The bar derives remaining shares from the displayed integer used shares and leaves `month: --` unqualified when the client reports no monthly window.
- Repeat the version-specific B012 non-consumption and B013 published-app gates. Record any gate that cannot be isolated or exercised as unverified rather than treating an earlier-version result as proof.

## Alternatives

- Keep only 2.1.304: preserves the old pin but leaves the updated official client unable to show its reported limits.
- Accept every later 2.1.x version: avoids frequent updates but could silently accept a changed client format or execution behavior.
- Package the older client: adds distribution and update ownership and does not validate the installed native client.

## Consequences

The current signed 2.1.306 installation can supply the same two observed subscription windows. Future client versions still fail closed until separately qualified. The observed ten-poll series was contaminated by other account activity, so B012 non-consumption acceptance for 2.1.306 remains open. This decision does not qualify a monthly window, other plan tiers, locales, or the remaining desktop and resource gates.
