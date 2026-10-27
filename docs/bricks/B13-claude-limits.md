# B013 — Claude limits

| | |
|---|---|
| **Area** | Core + Desktop |
| **Depends on** | B006; B012 accepted |
| **Unblocks** | B007 |
| **Sources** | [Product plan — Deliver subscription limits](../plan.md#3-deliver-subscription-limits), [Implementation plan — Provider adapters](../implementation-plan.md#provider-adapters), [ADR 0017](../adr/0017-claude-limit-display-in-codex-style.md), [ADR 0021](../adr/0021-used-limit-display.md), [ADR 0023](../adr/0023-weekly-limit-countdown-display.md), [ADR 0024](../adr/0024-weekly-countdown-units.md), [ADR 0018](../adr/0018-claude-usage-client-output.md), [ADR 0019](../adr/0019-claude-child-image-load-policy.md), [ADR 0022](../adr/0022-claude-code-minimum-version.md), [B012 qualification brick and dated reports](B12-claude-source.md) |

## Why

Once the source is qualified, the app can expose Claude limits without coupling parsing, execution, freshness, or UI failure handling to Codex or system metrics.

## What done looks like

The Claude section shows `Claude: 5h 24% in HH:mm | W 18% in Dd Hh` with the actual used limits, as defined in ADR 0021, ADR 0023, and ADR 0024. `W` is the binding weekly window. The bar has no monthly field; a genuine monthly window appears only in the tooltip. At 0% short-window used the countdown is omitted. The tooltip shows every observed limit window and its minute-precision reset time, or explains an omitted reset at 0% used. Polling occurs once per minute without consuming usage. Sign-in failure and unknown format are unavailable on the first failed poll; only a verified network failure may retain explicitly stale data until three minutes after the last success, then renders `Claude: 5h -- | W --` without waiting for another poll.

## Context

Build only against B012's selected source, [ADR 0018](../adr/0018-claude-usage-client-output.md), [ADR 0019](../adr/0019-claude-child-image-load-policy.md), and the minimum client version in [ADR 0022](../adr/0022-claude-code-minimum-version.md). Keep parsing separate from source acquisition and use sanitized fixtures. Start the verified executable directly without a shell, using fixed argument tokens, a kill-on-close Windows job, and the required image-loading policy. Resolve only the fixed native install path; check its canonical target, reparse points, trusted owner, version, and publisher signature before each poll. Drain stdout/stderr concurrently with byte limits and a request timeout. Never log raw account output. Older versions and unknown formats are unavailable.

Anthropic documents the 300-minute session window used by `5h`. Above 0% used, derive `HH:mm` from the client-displayed minute-precision reset and round up to the next whole minute so a positive duration never displays `00:00`. A 0%-used window may omit reset text; the tooltip says so, and a 0%-used short window has no countdown. Keep every weekly window in the snapshot and tooltip even though the bar shows only the binding weekly value. A missing monthly window does not make the qualified short or weekly fields unavailable.

If the source can genuinely block, run it in a child process. On timeout or shutdown, terminate the process tree and confirm exit inside the remaining deadline before releasing owned resources. A source that cannot satisfy this rule is not enabled.

## Acceptance criteria

- [ ] Parser tests cover every observed window, active reset text, 0%-used missing reset, displayed integer used share, malformed/unknown output, and binding-weekly selection
- [ ] Display tests cover the ADR 0021, ADR 0023, and ADR 0024 active format, binding-weekly selection, 0% short and weekly states without countdowns, weekly hour rounding and reset boundaries, unknown weekly reset, a qualified monthly window in the tooltip only, stale and unavailable strings, all-window tooltip, and no clipping
- [ ] Polls do not overlap; Claude failure never delays CPU, RAM, GPU, or Codex acquisition/expiry
- [ ] Fake-time tests prove first-poll sign-in unavailability and independent three-minute network expiry without a later poll
- [ ] For an executable source, timeout and shutdown tests prove process-tree termination and no late publication; for a local-file source, read/path failures produce unavailable without blocking other work
- [ ] Manual same-window checks compare every displayed value and client-reported reset time with the official client, verify the missing reset explanation at 0% and safe tooltip explanation on access loss, and record client version, account state, observation times, sanitized comparisons without credentials or raw account output, and pass/fail
- [ ] The ten-poll non-consumption check is repeated with the built adapter
- [ ] Raw account output, credentials, and sanitized fixtures are absent from logs and the published payload as applicable

## Out of scope

Codex is independent in B010/B011. Reading browser or credential state and calling undocumented endpoints remain prohibited.
