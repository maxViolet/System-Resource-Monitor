# B011 implementation plan for coding agents

Status: in progress. This package implements [B011 — Codex limits](../B11-codex-limits.md). Read the [B010 accepted report](../../research/providers/codex-2026-10-19.md), [subscription product plan](../../plan.md#3-deliver-subscription-limits), [provider adapter requirements](../../implementation-plan.md#provider-adapters), [ADR 0004](../../adr/0004-subscription-limits-in-first-release.md), [ADR 0015](../../adr/0015-codex-limit-display-from-qualified-windows.md), [ADR 0021](../../adr/0021-used-limit-display.md), [ADR 0023](../../adr/0023-weekly-limit-countdown-display.md), [ADR 0016](../../adr/0016-codex-client-resolution.md), and [CONTEXT language](../../../CONTEXT.md#language) before implementation. The brief's acceptance criteria and out-of-scope section are binding.

## Ordered work and gates

### 1. Freeze source and release contract

- Confirm B006 is `done` and B010 is `accepted`. Use only the signed standalone `codex-cli 0.159.0` and the three JSON-RPC messages in the accepted report. Resolve the installer's entry point through its junctions to the pinned release as [ADR 0016](../../adr/0016-codex-client-resolution.md) defines, canonicalize it, reject reparse points in the canonical path and unsafe writable paths, verify publisher and exact version, and reuse its existing Codex login. Do not read login files or call the usage endpoint directly.
- Record the accepted `codex` bucket, 300- and 10080-minute durations, UTC Unix reset times, rounding rule, signed-out error, network error, and the app-server experimental constraint. Reject any other bucket, version, method, or response shape as unavailable. Keep raw account output out of logs, fixtures, and published files.

**Gate:** a source-contract test rejects each unqualified version, bucket, missing field, and unknown error; no production path reads credentials or uses an undocumented endpoint.

### 2. Implement the bounded adapter

- Keep process acquisition in Desktop and parsing, limit state, and policy in AI Core. Start a fresh child once per minute with `UseShellExecute=false`, fixed arguments, and a neutral working directory. Send only `initialize`, `initialized`, and `account/rateLimits/read` with `excludeResetCreditDetails: true`.
- Drain stdout and stderr concurrently under byte limits; accept only the response with the matching request ID. Bound JSON depth, line length, and message count. The request timeout is 10 seconds from client start to the rate-limit response, about four times the measured 2.3-second slow case. After the response, the client gets 5 seconds to exit once its stdin closes; then its tree is terminated. On shutdown, cancel the request immediately rather than waiting for that timeout. Do not overlap polls. On timeout, cancellation, or shutdown, kill the process tree and confirm exit within the remaining two-second shutdown deadline; never publish a late result.
- Do not let Codex acquisition, failure, or expiry delay CPU, RAM, GPU, or Claude. A first signed-out or unknown-format failure is unavailable; a network failure may retain explicitly stale qualified values only until three minutes after the last success. Use an independent timer to expire them at the exact boundary.

**Gate:** deterministic parser, timeout, no-overlap, failure-classification, fake-time expiry, and process-tree tests pass; injected Codex failures leave the other collectors updating.

### 3. Present qualified values

- Render the aggregate bucket as `Codex: 5h 24% in HH:mm | W 67% in DD HH` using the actual `usedPercent` values ([ADR 0021](../../adr/0021-used-limit-display.md), [ADR 0023](../../adr/0023-weekly-limit-countdown-display.md)), only after validating the accepted 0–100 input. Derive `HH:mm` from the 300-minute `resetsAt` and the current time when 5-hour used is above 0%; round up to the next whole minute so a positive remainder never displays `00:00`. At 0%, render `5h 0%` without `in HH:mm`, even if `resetsAt` exists. Derive weekly `DD HH` from the 10080-minute `resetsAt`, round a positive remainder up to a whole hour, and update it between polls. At 0% weekly used show `W 0%` without a countdown; at or after reset omit the weekly countdown. Above 0% used, if the reset is missing or more than seven days plus one minute ahead, show `W --`.
- Show no monthly field; B010 did not qualify a monthly window. An unavailable snapshot renders `Codex: 5h -- | W --`. Show the exact local reset times for qualified 5-hour and weekly windows in the tooltip; explain an out-of-range weekly reset instead of showing it as exact. Keep clear stale or unavailable explanations where needed. Do not invent a monthly or per-model percentage. Size the section for the longer weekly countdown, 0%, 100%, active countdown, stale, and unavailable states at 100%, 150%, and 200% scaling.

**Gate:** view-model and rendering checks prove the format, both countdown boundaries, weekly hour rounding and day rollover, 0% short-window behavior, tooltip meaning, unqualified month, and no clipping; an unknown source never produces a percentage.

### 4. Verify the built adapter and published artifact

- Run targeted Release tests, then the repository handoff commands. Publish the app from the same commit used for the report. Verify the signed client version and existing account state. Compare every displayed qualified used limit and reset time with the official client inside one verification window. Exercise access loss safely and check the bar and tooltip. Repeat ten polls one minute apart with no other Codex work; confirm no consumption and no new session.
- On the exact published artifact, record app and child process IDs and private bytes at least every 100 ms across the 30-minute idle run and every Codex poll in the eight-hour run. The sum must never exceed 150 MiB. Count child lifetimes and report any missed lifetime as unverified. Keep the separate app-only 150 MiB and growth gates. Record CPU cost, responsiveness, timeout, and shutdown observations against the same artifact; B007 still owns its full release gates.
- Put automated, Windows integration, and manual desktop/resource evidence in separate dated `docs/acceptance/` reports with commit, artifact hashes, client version, sample counts, observed maxima, and pass/fail. Sanitize all account data. Do not mark B011 `done` from automated checks alone.

**Gate:** every B011 acceptance criterion has passing evidence on the named artifact; unrun desktop or resource checks remain unverified.

## Boundary

B010's qualification report remains historical evidence. B012 qualifies Claude independently; B013 implements it only after acceptance. B019/B020 own any later Codex monthly qualification and delivery. A bundled Codex client or model split needs separate qualification and a separate decision.
