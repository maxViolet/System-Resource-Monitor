# B013 — Claude remaining limits

| | |
|---|---|
| **Area** | Core + Desktop |
| **Depends on** | B006; B012 accepted |
| **Unblocks** | B007 |
| **Sources** | [Product plan — Deliver subscription limits](../plan.md#3-deliver-subscription-limits), [Implementation plan — Provider adapters](../implementation-plan.md#provider-adapters), [ADR 0017](../adr/0017-claude-limit-display-in-codex-style.md), [B012 qualification brick and dated report](B12-claude-source.md) |

## Why

Once the source is qualified, the app can expose Claude limits without coupling parsing, execution, freshness, or UI failure handling to Codex or system metrics.

## What done looks like

The Claude section shows `Claude: 5H - 76% in HH MM | W 33% | month: 82%` with actual qualified values. `W` is the binding weekly window. A qualified monthly window supplies its value; otherwise the visible `month: --` is unqualified, not zero. At 100% short-window remaining the countdown is omitted. The tooltip shows every qualified limit window and exact reset time and gives a safe explanation for stale, unavailable, and unqualified fields. Polling occurs once per minute without consuming usage. Sign-in failure is unavailable on the first failed poll; network failure may retain explicitly stale data only until three minutes after the last success, then renders `Claude: 5H - -- | W -- | month: --` without waiting for another poll.

## Context

Build only against B012's accepted client version and contract. Keep parsing separate from source acquisition and use sanitized fixtures. For an executable source, apply the [provider adapter](../implementation-plan.md#provider-adapters) controls: trusted resolution (never the current directory or an untrusted `PATH` hit), canonical-path, reparse, and publisher checks, `UseShellExecute=false` with fixed arguments, concurrent stdout/stderr reads with byte limits, bounded parsing, and a request timeout. For an accepted local-file source, use the qualified canonical path and non-credential format without process execution. Never log raw account output. Unknown versions or formats are unavailable.

ADR 0017 requires B012 to prove a 300-minute short window before the `5H` label can be implemented. Below 100% remaining, derive `HH MM` from its reset time and round up to the next whole minute so a positive duration never displays `00 00`. Keep every weekly window in the snapshot and tooltip even though the bar shows only the binding weekly value. A missing monthly window does not make the qualified short or weekly fields unavailable.

If the source can genuinely block, run it in a child process. On timeout or shutdown, terminate the process tree and confirm exit inside the remaining deadline before releasing owned resources. A source that cannot satisfy this rule is not enabled.

## Acceptance criteria

- [ ] Parser tests cover every qualified window, reset time, rounding rule, malformed/unknown output, and binding-weekly selection
- [ ] Display tests cover the ADR 0017 active format, binding-weekly selection, 100% state without a countdown, countdown boundaries, qualified monthly value, unqualified `month: --`, stale and unavailable strings, all-window tooltip, and no clipping
- [ ] Polls do not overlap; Claude failure never delays CPU, RAM, GPU, or Codex acquisition/expiry
- [ ] Fake-time tests prove first-poll sign-in unavailability and independent three-minute network expiry without a later poll
- [ ] For an executable source, timeout and shutdown tests prove process-tree termination and no late publication; for a local-file source, read/path failures produce unavailable without blocking other work
- [ ] Manual same-window checks compare every displayed value and reset time with the official client, verify the safe tooltip explanation on access loss, and record client version, account state, observation times, sanitized comparisons without credentials or raw account output, and pass/fail
- [ ] The ten-poll non-consumption check is repeated with the built adapter
- [ ] Raw account output, credentials, and sanitized fixtures are absent from logs and the published payload as applicable

## Out of scope

Codex is independent in B010/B011. Reading browser or credential state and calling undocumented endpoints remain prohibited.
