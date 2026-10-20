# B012 implementation plan for coding agents

Status: planned. This package implements [B012 — Qualify a Claude limit source](../B12-claude-source.md). The [product plan — Deliver subscription limits](../../plan.md#3-deliver-subscription-limits), [implementation plan — Slice 2](../../implementation-plan.md#slice-2-subscription-limits), [Provider adapters](../../implementation-plan.md#provider-adapters), [ADR 0004](../../adr/0004-subscription-limits-in-first-release.md), [ADR 0017](../../adr/0017-claude-limit-display-in-codex-style.md), and [CONTEXT — Language](../../../CONTEXT.md#language) remain authoritative.

## Outcome and handoff

Close B012 as `accepted` or `rejected` with one dated, sanitized report at `docs/research/providers/claude-<YYYY-MM-DD>.md`. B012 adds no code, tests, fixtures, or interfaces under `src/` or `tests/`. An accepted report is B013's contract: qualified client and version, invocation or canonical local-file path, response schema, complete limit-window set, reset-time format, used/remaining rounding, failure classification, freshness, latency, and constraints. A rejected report makes B013 `not applicable`.

The accepted contract must give B013 enough evidence to render ADR 0017 without inference: a proven 300-minute short window and reset time, every weekly window needed to select the binding weekly value, and a genuine monthly window and reset time when one exists. No qualified monthly window is a valid accepted outcome and renders `month: --`; missing any actual short or weekly window fails source completeness.

Acceptance requires one candidate to pass every brick gate. A documented `/usage` screen is reference evidence, not automatically an automatable source. The official status-line contract is a candidate, not a presumed acceptance: Claude Code 2.1.304 documents only `five_hour` and `seven_day`, supplies them only after a session's first API response, and permits each field to be absent. Those limits may be incomplete when `/usage` exposes a model-specific weekly window.

## Rules for every step

- Allowed evidence: stdout, stderr, exit code, and documented status-line input from the official Claude Code client, plus locally written files proven to contain no credentials. Forbidden: `.credentials.json` or any other credential file, OS credential stores, browser state, direct HTTP calls to Anthropic or Claude endpoints, packet capture, proxy interception, and undocumented endpoints.
- Never sign the real account out. Exercise signed-out behavior with a new empty `CLAUDE_CONFIG_DIR`; first prove `claude auth status` reports signed out in that directory. Do not run login or copy credentials into it.
- Raw client output stays in `%TEMP%\b12-claude\`, outside the repository, and is deleted at closeout. Commit only sanitized excerpts. Replace e-mail addresses, account, organization, user, workspace, session, prompt, and transcript IDs and absolute user paths (use `%USERPROFILE%`). The plan tier may remain because it determines the window set.
- Use a neutral trusted working directory outside the repository. Disable project/user customization where the candidate permits it without disabling the candidate itself. Record every fixed argument, environment override, settings file, input message, and timeout.
- Any probe script or status-line writer is ad hoc under the temp directory and is not committed. It may store only the candidate's limit fields, reset times, client version, and an observation timestamp. It must reject or omit every unrelated status-line field, especially session, prompt, transcript, workspace, repository, cost, and account data.
- During poll and reconciliation runs, no other Claude work may run on this account from Claude Code, Claude Desktop, claude.ai, mobile, IDE extensions, other worktrees, background agents, or scheduled tasks. Record this isolation. If another surface cannot be stopped, postpone the run rather than attributing shared usage to the candidate.
- Do not infer an undocumented contract from one successful output. A candidate is stable only when its command/file and fields are documented by Anthropic for the qualified version, or when an official client-generated non-credential file has a versioned contract sufficient for B013. An undocumented cache path or bundled implementation detail fails the stable-source gate.

## Work order

### 1. Verify prerequisite and inventory the client

- Confirm B004 is `done` in the roadmap before research starts. Create the branch required by `AGENTS.md` and set B012 to `in progress` only in the implementation change, not in this planning change.
- Record Windows build, local date/timezone, account plan tier, and every installed official Claude Code client. The planning baseline is native `%USERPROFILE%\.local\bin\claude.exe`, version `2.1.304`; inventory the versions present when qualification runs rather than assuming that baseline remains installed.
- For each client, record `--version`, canonical path, reparse-point status, Authenticode signature/publisher status, and authoritative installation metadata. Do not resolve from the current directory or accept the first untrusted `PATH` hit.
- Save the official documentation URLs and retrieved dates for the CLI, commands, status-line schema, usage behavior, authentication isolation, and release notes that apply to the tested version. Record whether a candidate is explicitly supported in non-interactive mode.

**Gate:** the tested binary, version, provenance, signature status, account plan tier, official contract sources, and B004 prerequisite are recorded.

### 2. Complete the candidate inventory before rejecting any candidate

Evaluate candidates in this order. Stop at the first candidate that passes every gate. Mark later candidates `not evaluated — accepted candidate exists`. Reject B012 only after every allowed candidate has a named failed gate and no documented candidate remains.

| ID | Candidate | Kind | Known risk to prove |
|---|---|---|---|
| C1 | `claude -p "/usage"` (or another officially documented non-interactive `/usage` form found for the tested version), with tools disabled, no persistence where supported, and structured/text output captured | Official client output | The docs describe `/usage` as an in-session dialog and do not promise this exact headless form. It may start a model turn, consume usage, create a session, omit the dialog data, or return an unstable rendering. |
| C2 | Official status-line JSON narrowed by a configured writer to an atomic local non-credential snapshot, then polled from that file | Official client output plus local non-credential file | Rate-limit fields appear only after the first API response, may be stale/absent, and the documented schema has only five-hour and seven-day windows. The writer configuration may be too intrusive for B013, and reading the file alone may not distinguish sign-in from network failure. |
| C3 | `/usage` from an interactive client in screen-reader/classic rendering, driven by redirected input or a pseudo-terminal only if Anthropic documents the form as automatable | Official client output | Terminal automation is likely interactive rather than a stable non-interactive poll, and rendered text may change by version, plan, terminal size, or localization. It may create a session even without a model turn. |
| C4 | An official-client-written usage snapshot or cache discovered in official documentation or release/source material and proven non-credential | Local non-credential file | No stable path or schema is currently documented. An internal cache can be stale, omit failure state, contain identifiers, or change without notice. Do not inspect a candidate file until its non-credential purpose is established. |
| C5+ | Any other limit-reporting command or file documented in `claude --help`, official docs, or release notes for the qualified version | As documented | Add it to the table, with its contract and risks, before evaluation. |

Explicitly exclude the credential file, auth/keychain stores, session transcripts, browser state, debug logs containing account traffic, direct endpoint calls, and network interception. Local session history is not a candidate: it contains conversation data, updates only through work, and cannot establish current account-wide limits or failure classification.

**Gate:** the report contains the complete allowed candidate inventory, exclusions, and official source links before the first candidate is finally rejected.

### 3. Establish the authoritative window reference

- In one controlled interactive Claude Code session, open `/usage` without sending a model prompt solely to inventory the account's current limit rows. Record sanitized labels, used/remaining presentation, every reset time, and whether rows are account-wide, model-specific, or spend-related. Do not treat session-cost/context fields as subscription limit windows.
- Repeat the inventory after a client restart to distinguish durable account windows from session-only UI state. Compare against Anthropic's documentation for the tested plan and version.
- Define the required complete set for this account and qualified version: the short window, the account-wide weekly window, every model-specific weekly window, and any genuine monthly subscription window actually exposed by the official client. Spend/usage-credit limits are constraints, not substitutes for a monthly subscription limit unless Anthropic explicitly identifies the field as that limit.
- Give each required window a stable source identifier, official UI label, duration if supplied, model scope, and reset-time semantics. Confirm whether the short window is exactly 300 minutes; ADR 0017's `5H` label cannot be implemented otherwise. If a candidate exposes generic five-hour/seven-day fields while `/usage` exposes an additional model-specific weekly or monthly window, that candidate fails completeness.

**Gate:** the report defines the actual complete window set that every candidate must return; no candidate is accepted by comparing only the two fields expected in advance.

### 4. Qualify each candidate against one common contract

Run steps 4a–4f for C1, then repeat them for each remaining candidate until one passes.

**a. Contract and sanitization.** Record the official command/file documentation and exact tested version. Define the success schema: required window identifier/name, used or remaining percentage, reset time and unit/timezone, observation timestamp, and any nullable/optional fields. Define byte, line/record, nesting, and timeout bounds for B013. For C2, configure the writer to validate and atomically replace a dedicated temp snapshot containing only an allowlist of limit fields; prove no session/transcript/account/workspace fields are copied. For C4, prove the file is non-credential before reading its contents. Unknown versions, extra required windows, missing required fields, non-finite/out-of-range values, duplicate windows, invalid times, and schema changes are unknown format, never partial success.

**b. Two consecutive polls (criterion 2).** Poll through the exact production-candidate mechanism twice, each as an independent read/invocation with fixed arguments and a neutral working directory. Do not send a model prompt. Record start/end UTC, latency, exit code, stderr byte count, file identity/mtime where applicable, and sanitized parsed fields. Both polls must return every required window and reset time. For a file candidate, show that each read is non-interactive and define how the official client refreshes the file; rereading one old snapshot does not prove two source polls.

**c. Failure classification and signed-out behavior (criteria 3 and 6).** With an empty isolated `CLAUDE_CONFIG_DIR`, run `claude auth status`, prove it is signed out, then exercise the candidate and record its structured error, exit code, file state, and latency. Exercise network failure first with the machine offline and, as a secondary reproducible check, with `HTTPS_PROXY=http://127.0.0.1:9` for the child only. Restore networking afterward. Define unknown format as every success/error shape outside the pinned contract. Sign-in failure and network failure must differ by a documented structured field or version-pinned exact output; missing/stale file alone is not sufficient unless the accepted file contract records a trustworthy reason. Record access-revoked behavior as unverified unless it can be exercised without changing the real account.

**d. Non-consumption and session side effects (criterion 4).** Take an official `/usage` reference immediately before and after the run, away from all reset boundaries. Run ten candidate polls 60 seconds apart, matching B013's interval, with no other account work. Every used/remaining value and fixed active-window reset time must stay unchanged within the client's documented precision. A not-yet-started rolling window may move only if the official client documents that behavior; record drift separately. Compare session/transcript directory names and counts before and after without opening transcript contents. No candidate poll may create a session, prompt, model response, transcript, background agent, or equivalent usage. For C2, include any session needed to produce/refresh the status-line snapshot in this accounting; an already-open producer does not exempt its API response from the gate.

**e. Reconciliation and rounding (criterion 5).** Within one two-minute verification window, capture a cropped/redacted `/usage` reference and one candidate poll. For every required window, compare reset times and the official displayed remaining value. If the candidate supplies used share, test the client's observed/displayed rule for round, floor, and ceiling, then compute `remaining = 100 - roundedUsed`. Verify the rule with a non-integer sample that distinguishes the alternatives or official version-matched source/documentation that states the formula. Integer-only samples and example formatting are insufficient to prove rounding. If the candidate supplies remaining directly, record its precision and prove the displayed rounding the same way. A rounding rule that cannot be proven fails the criterion.

**f. Security and stability boundary (criterion 7).** Show that the probe starts only the verified official client and reads its pipes, or reads only the accepted canonical non-credential file. Inventory the probe's own file opens and child processes without capturing network payloads; it must not open credentials, transcripts, browser files, or arbitrary settings. Record that any authenticated network request is made internally by the official client through its existing login. The invoked command/file and fields must be documented for the qualified version; discovering an endpoint or reverse-engineering an internal cache does not make it allowed. If acceptance requires a new security or product-boundary choice, stop and propose an ADR rather than accepting the candidate.

**Gate:** the candidate has an explicit pass/fail result for every B012 criterion, including complete windows, two polls, three-way failure classification, ten-poll non-consumption, rounding, signed-out behavior, and the no-credential/no-undocumented-endpoint boundary.

### 5. Decide acceptance or rejection

- Accept the first candidate that passes all gates. Pin the exact client version and contract. Record every installation, configuration, active-session, network, plan-tier, locale, freshness, and latency constraint that B013 must enforce or expose.
- Reject a failed candidate with the smallest decisive evidence plus the full criterion matrix; do not continue risky probes after a decisive failure when they cannot restore eligibility. For example, a model turn/session created by every poll fails non-consumption, and an undocumented-only path fails stability.
- Reject B012 only when C1–C5+ are exhausted. One failed candidate never rejects an untested allowed candidate. Do not weaken the source boundary, omit a required model-specific window, infer a reset time, or accept stale values to force an implementation track.

**Gate:** either one source passes every criterion or every allowed candidate has a named, evidenced failure.

### 6. Write the qualification report

Write `docs/research/providers/claude-<YYYY-MM-DD>.md` in this order:

- **Outcome:** accepted candidate and version, or rejected track and exhausted candidates.
- **Environment:** Windows build, date/timezone, sanitized plan tier, installed official clients, versions, signatures, and installation metadata.
- **Official window reference:** every actual limit-window label, type, duration, reset semantics, and model scope; explicit confirmation whether the short window is 300 minutes; every weekly input to binding selection; and whether a genuine monthly subscription window exists.
- **Candidates:** one table mapping each candidate to every gate, with not-evaluated reasons where applicable.
- **Accepted contract** when accepted: canonical invocation/file path, configuration prerequisite, exact schema and sanitized example, windows, reset format, rounding rule, refresh/freshness behavior, failure-classification table, latency, version pin, and constraints.
- **Evidence for criteria 1–8:** explicit pass/fail/not-applicable result and evidence for each brick criterion.
- **Unverified items:** other plan tiers, client versions, locale variants, access revocation, and any unexercised condition.
- **Handoff to B013:** identify the short window and prove its duration, enumerate all weekly windows used to select the binding weekly value, identify a qualified monthly window or explicitly mark it unqualified, and provide the reset/rounding facts needed by ADR 0017. The sanitized report example is not a parser fixture.

The report must distinguish the source's observation time from the report timestamp and from reset times. It must contain no raw account output, credentials, identifiers, absolute user paths, transcript data, or undocumented endpoint names.

**Gate:** every acceptance criterion maps to evidence, examples are sanitized, and the report supports the outcome without relying on private raw data.

### 7. Close and clean up

- Delete the temp harness, status-line writer/config, raw captures, isolated `CLAUDE_CONFIG_DIR`, and candidate snapshots. Restore any user status-line setting exactly; verify no qualification process or background session remains.
- Link the dated report from the package README. Set B012 to `accepted` or `rejected` in the roadmap progress table. If rejected, set B013 to `not applicable`; if accepted, leave B013 eligible but do not implement it in B012.
- Run `git diff --check`. The .NET restore/build/test/publish commands do not apply because B012 changes only research documentation and no production surface.

**Gate:** only the plan/package updates, sanitized qualification report, and required roadmap state changes remain; temporary output and account data are absent from the repository.

## Boundary for B013 and B010/B011

B013 owns production types and parsing, trusted executable/file resolution, process isolation and tree termination where applicable, byte and parser bounds, polling, expiry, the ADR 0017 display and tooltip states, and repeating the ten-poll check with the built adapter. B012 does not add a provider abstraction or UI placeholder. Codex qualification and implementation remain independent in B010/B011; do not share a harness, source contract, or report merely because both providers use the same compact display grammar. Record any further design choice with meaningful alternatives in a new ADR.