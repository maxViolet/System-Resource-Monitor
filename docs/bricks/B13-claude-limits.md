# B013 — Claude remaining limits

| | |
|---|---|
| **Area** | Core + Desktop |
| **Depends on** | B006; B011 accepted |
| **Unblocks** | B007 |
| **Sources** | [Product plan — Deliver subscription limits](../plan.md#3-deliver-subscription-limits), [Implementation plan — Provider adapters](../implementation-plan.md#provider-adapters), [B011 qualification brick and dated report](B11-claude-source.md) |

## Why

Once the source is qualified, the app can expose Claude limits without coupling parsing, execution, freshness, or UI failure handling to Codex or system metrics.

## What done looks like

The Claude section shows `short%/weekly%`, where weekly is the binding weekly window. Its tooltip shows every limit window and reset time and gives a safe explanation when unavailable. Polling occurs once per minute without consuming usage. Sign-in failure is unavailable on the first failed poll; network failure may retain explicitly stale data only until three minutes after the last success, then displays `--/--` without waiting for another poll.

## Context

Build only against B011's accepted client version and contract. Keep parsing separate from source acquisition and use sanitized fixtures. For an executable source, apply the [provider adapter](../implementation-plan.md#provider-adapters) controls: trusted resolution (never the current directory or an untrusted `PATH` hit), canonical-path, reparse, and publisher checks, `UseShellExecute=false` with fixed arguments, concurrent stdout/stderr reads with byte limits, bounded parsing, and a request timeout. For an accepted local-file source, use the qualified canonical path and non-credential format without process execution. Never log raw account output. Unknown versions or formats are unavailable.

If the source can genuinely block, run it in a child process. On timeout or shutdown, terminate the process tree and confirm exit inside the remaining deadline before releasing owned resources. A source that cannot satisfy this rule is not enabled.

## Acceptance criteria

- [ ] Parser tests cover every qualified window, reset time, rounding rule, malformed/unknown output, and binding-weekly selection
- [ ] Polls do not overlap; Claude failure never delays CPU, RAM, GPU, or Codex acquisition/expiry
- [ ] Fake-time tests prove first-poll sign-in unavailability and independent three-minute network expiry without a later poll
- [ ] For an executable source, timeout and shutdown tests prove process-tree termination and no late publication; for a local-file source, read/path failures produce unavailable without blocking other work
- [ ] Manual same-window checks compare every displayed value and reset time with the official client, verify the safe tooltip explanation on access loss, and record client version, account state, observation times, sanitized comparisons without credentials or raw account output, and pass/fail
- [ ] The ten-poll non-consumption check is repeated with the built adapter
- [ ] Raw account output, credentials, and sanitized fixtures are absent from logs and the published payload as applicable

## Out of scope

Codex is independent in B010/B012. Reading browser or credential state and calling undocumented endpoints remain prohibited.
