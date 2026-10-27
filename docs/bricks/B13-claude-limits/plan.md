# B013 implementation plan for coding agents

Status: in progress. [PR #41](https://github.com/maxViolet/System-Resource-Monitor/pull/41) implemented the adapter, parser, monitor, and bar section. This plan closes the remaining gaps in [B013 — Claude limits](../B13-claude-limits.md). Read the brief, [subscription product plan](../../plan.md#3-deliver-subscription-limits), [provider adapter requirements](../../implementation-plan.md#provider-adapters), [ADR 0017](../../adr/0017-claude-limit-display-in-codex-style.md), [ADR 0021](../../adr/0021-used-limit-display.md), [ADR 0023](../../adr/0023-weekly-limit-countdown-display.md), [ADR 0024](../../adr/0024-weekly-countdown-units.md), [ADR 0018](../../adr/0018-claude-usage-client-output.md), [ADR 0019](../../adr/0019-claude-child-image-load-policy.md), [ADR 0020](../../adr/0020-qualify-claude-code-2-1-306.md), [ADR 0022](../../adr/0022-claude-code-minimum-version.md), and the dated B012 reports for [2.1.304](../../research/providers/claude-2026-10-21.md) and [2.1.306](../../research/providers/claude-2.1.306-2026-10-21.md) before implementation. The brief's acceptance criteria and out-of-scope section are binding.

## Ordered work and gates

### 1. Freeze the contract

- Accept signed file versions `2.1.304.0` and later ([ADR 0022](../../adr/0022-claude-code-minimum-version.md)), the fixed ADR 0018 arguments, and the ADR 0019 child image-loading policy. Monthly and other new rows remain unknown format; exercise the qualified-monthly display only with constructed snapshots.
- `ClaudeUsageSource` does not produce `NetworkFailed` until a pinned client gives an explicit, verified network response; enabling it requires separate qualification. Prove the stale path at monitor level with a fake source.
- A reset that has already passed renders `in 00:00`, matching `CodexSectionFormat` ([ADR 0021](../../adr/0021-used-limit-display.md)).

**Gate:** no change to the row grammar or failure classification.

### 2. Close the code and test gaps

- Monitor (`ClaudeLimitMonitorTests`, following `CodexLimitMonitorStateTests` and `CodexLimitMonitorPollingTests`): the second poll occurs at one minute and polls never overlap; unknown format is unavailable on the first poll; repeated network failures do not extend the three-minute deadline; a network failure without a prior success is unavailable; stale followed by sign-out or unknown format is unavailable at once; expiry through `RunAsync` occurs without another poll.
- Parser (`ClaudeUsageReaderTests`): invalid JSON, oversized input, and 0, 100, and 101 used-share boundaries. Reject a `Current session` reset more than 300 minutes ahead, using `ClaudeContract.ShortWindowMinutes` plus the observed one-minute display variation.
- Display (`ClaudeSectionFormatTests`): short countdown at an exact minute, 1 second → `00:01`, 61 minutes 1 second → `01:02`, the `99:59` cap, a passed reset, and `UntilCountdownChanges`. Cover weekly hour rounding, day rollover, `W 0%` without a countdown even when its reset is unknown, the weekly reset boundary, and timer updates between polls ([ADR 0023](../../adr/0023-weekly-limit-countdown-display.md), [ADR 0024](../../adr/0024-weekly-countdown-units.md)). Cover the text for every `ClaudeReadFailed` reason, including expired `NetworkFailed`. The tooltip states minute precision, explains the missing reset at 0% used, and lists a qualified monthly window. Prove no clipping for the active, 0%, qualified-monthly, stale, and unavailable states at 1.0, 1.5, and 2.0 scaling, following `CodexSectionWidthTests`.
- Source (`ClaudeUsageSourceTests`): test the stderr byte limit independently. A launch under the enforced image policy either runs or fails as `ReadFailed` with no unprotected retry. Shutdown termination uses the time remaining in the shutdown wait instead of a fresh `ShutdownCoordinator.WaitDeadline`; change the Claude path only and report Codex's identical behaviour without changing it.
- Independence (test host): a blocked Claude source does not delay CPU, RAM, GPU, or Codex acquisition or Codex expiry.

**Gate:** targeted Release tests pass, then the repository handoff commands pass.

### 3. Verify the built adapter and published artifact

- Publish from the final commit and record artifact hashes, client version and signature, account tier, and time zone without account identifiers.
- Run ten built-adapter polls one minute apart. Before starting, confirm no other Claude process is running. For every poll, record zero model turns, zero API duration, and zero cost from the envelope; before and after the run, count transcripts and sessions by file name; confirm unchanged used shares. Minute-level reset variation is recorded, not treated as usage. If the absence of other account work cannot be confirmed, the gate remains unverified.
- On the final artifact, compare every displayed value and reset minute with official `/usage` output in the same verification window. Record the missing-reset explanation at 0% used when such a window is observable; otherwise record it as unverified.
- Exercise sign-out with an empty `CLAUDE_CONFIG_DIR` and check the unavailable bar and safe tooltip. Exercise another access loss, such as revoked or expired access, only if it does not change the real account's login; otherwise record it as unverified rather than covered by the sign-out check.
- Block the child's network, for example with `HTTPS_PROXY` set to an unused local port, and record the classification. The expected result is unavailable at once, not stale; record an explicit network error without enabling it.
- Check the section visually at 100%, 150%, and 200% scaling.
- Shut down during an in-flight poll and confirm that no `claude.exe` child remains.
- Inspect the published folder and every file the app writes: no `claude.exe`, test host, fixture or envelope text, raw account output, or credentials.
- Put automated, Windows integration, and manual desktop evidence in separate dated reports: `docs/acceptance/<date>-B13-automated.md`, `<date>-B13-windows-integration.md`, and `<date>-B13-manual-desktop.md`.

**Gate:** every B013 acceptance criterion has passing evidence on the named artifact. Unrun gates remain unverified, and B013 stays `in progress`.

## Boundary

Monthly-window qualification, network-failure classification, other plan tiers, client versions, or locales, Codex changes, and B012's release non-consumption checkbox are outside this plan. App-only resource gates belong to B007; record Claude child private bytes as an observation only.
