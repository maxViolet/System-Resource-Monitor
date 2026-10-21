# B011 — Codex limits

| | |
|---|---|
| **Area** | Core + Desktop |
| **Depends on** | B006; B010 accepted |
| **Unblocks** | B007 |
| **Sources** | [Product plan — Deliver subscription limits](../plan.md#3-deliver-subscription-limits), [Implementation plan — Provider adapters](../implementation-plan.md#provider-adapters), [ADR 0015](../adr/0015-codex-limit-display-from-qualified-windows.md), [ADR 0021](../adr/0021-used-limit-display.md), [ADR 0016](../adr/0016-codex-client-resolution.md), [B010 qualification and dated report](B10-codex-source/README.md) |

## Why

Once the source is qualified, the app can expose Codex limits without coupling parsing, execution, freshness, or UI failure handling.

## What done looks like

The Codex section shows the qualified aggregate 5-hour and weekly used limits, and the active 5-hour countdown, as defined in ADR 0021. The bar has no monthly field. Its tooltip shows the exact qualified reset times and explains unavailable fields. Polling occurs once per minute without consuming usage. Sign-in failure is unavailable on the first failed poll; network failure may retain explicitly stale qualified values only until three minutes after the last success, then displays unavailable values without waiting for another poll.

## Context

Build only against B010's accepted signed standalone `codex-cli 0.159.0` and its [dated contract](../research/providers/codex-2026-10-19.md). Keep parsing separate from source acquisition and use sanitized fixtures. Apply the [provider adapter](../implementation-plan.md#provider-adapters) controls: trusted resolution (never the current directory or an untrusted `PATH` hit), canonical-path, reparse, and publisher checks, `UseShellExecute=false` with fixed arguments, concurrent stdout/stderr reads with byte limits, bounded parsing, and a request timeout. Never log raw account output. Unknown versions or formats are unavailable. Only the `codex` aggregate bucket, 300-minute window, and 10080-minute window are qualified; monthly and per-model values remain unqualified.

Resolve the client through the standalone installer's junctions to the pinned release only, as [ADR 0016](../adr/0016-codex-client-resolution.md) defines.

If the source can genuinely block, run it in a child process. On timeout or shutdown, terminate the process tree and confirm exit inside the remaining deadline before releasing owned resources. A source that cannot satisfy this rule is not enabled.

## Acceptance criteria

- [ ] Parser tests cover both qualified window durations and reset times, `usedPercent` within 0–100, malformed/unknown output, and rejection of unqualified buckets or versions
- [ ] Display tests cover the ADR 0021 format without a monthly field, the 0% 5-hour state without a countdown, active countdown boundaries, stale values, and unavailable values without clipping
- [ ] Polls do not overlap; Codex failure never delays CPU, RAM, GPU, or Claude acquisition/expiry
- [ ] Fake-time tests prove first-poll sign-in unavailability and independent three-minute network expiry without a later poll
- [ ] Timeout and shutdown tests prove process-tree termination, confirmed exit inside the deadline, and no late publication
- [ ] Manual same-window checks compare every displayed value and reset time with the official client, verify the safe tooltip explanation on access loss, and record client version, account state, observation times, sanitized comparisons without credentials or raw account output, and pass/fail
- [ ] The ten-poll non-consumption check is repeated with the built adapter
- [ ] On the identified published artifact, app plus child private bytes stay at or below 150 MiB at every sampled Codex poll; the report records sample interval, count, maximum, and any missed child lifetime
- [ ] Raw account output, credentials, and sanitized fixtures are absent from logs and the published payload as applicable

## Out of scope

Claude is independent in B012/B013. Monthly-source qualification and delivery belong to B019/B020; B011 shows no monthly field. Reading browser or credential state and calling undocumented endpoints remain prohibited.
