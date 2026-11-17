# Qualify Claude 2.1.325 local insights and explain read failures

Status: accepted, 2026-11-15. Supersedes [ADR 0025](0025-claude-local-usage-insights.md) only for the additional observed local-insights rows and the safe failure explanations. The subscription-row, envelope, source, and freshness contracts remain in force.

## Context

The signed Claude Code 2.1.325 client returned valid short and weekly limit rows in a zero-work `/usage` envelope, but appended two local-insights rows outside the ADR 0025 grammar. The parser rejected the entire snapshot. The adapter also mapped auth-check failures and failed `/usage` commands to `UnknownFormat`, so the tooltip could not distinguish them from a changed response format. See the [dated sanitized observation](../research/providers/claude-2.1.325-2026-11-15.md).

## Decision

- After the required subscription rows, local-insights heading, and exact notice, accept the advice `N%  Review sessions active over many hours · whole sessions with requests in N or more separate hours` and a `Top plugins: name N%` row with one entry per row. Bound the advice count to three digits and the plugin name to 1–128 ASCII letters, digits, `_`, `.`, `:`, or `-`; every share remains an integer from 0 through 100. Ignore these local attribution shares when selecting subscription limits. Keep rejecting unknown rows, altered advice, unqualified percentages, and changed envelopes. Do not broaden `Top skills` without a separate observation.
- Keep the immediate unavailable `Claude: 5h -- | W --` display for every non-network failure. In the existing tooltip, distinguish failed sign-in checks, failed `/usage` commands, unsupported response format, and client start/read failures. Timeout, sign-out, trust, and version reasons retain their existing explanations. Do not retain or log raw client output, names, credentials, or a format fingerprint.
- Keep polling, client trust, version floor, authentication source, and network-staleness rules unchanged. Verify the built adapter and published app separately.

## Alternatives

- Ignore all text after the subscription windows: rejected because a new subscription limit could be missed.
- Treat every nonzero command exit as unknown format: rejected because the user cannot identify which stage failed.
- Log or persist raw `/usage` output for diagnosis: rejected because it can contain account-specific data.

## Consequences

The observed 2.1.325 local insights can coexist with qualified subscription windows. A later unknown row still makes that poll unavailable. The tooltip gives a safe stage explanation, while the bar never displays a stale value for these failures. Parser tests and a one-poll adapter observation do not close the Windows 11 same-window published-app or controlled non-consumption gates.
