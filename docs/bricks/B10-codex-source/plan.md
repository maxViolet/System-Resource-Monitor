# B010 implementation plan for coding agents

Status: accepted on 2026-10-19; see the [qualification report](../../research/providers/codex-2026-10-19.md). This package implements [B010 — Qualify a Codex limit source](../B10-codex-source.md). The [product plan — Deliver subscription limits](../../plan.md#3-deliver-subscription-limits), [implementation plan — Slice 2](../../implementation-plan.md#slice-2-subscription-limits), [Provider adapters](../../implementation-plan.md#provider-adapters), [ADR 0004](../../adr/0004-subscription-limits-in-first-release.md), and [CONTEXT — Language](../../../CONTEXT.md#language) remain authoritative.

## Outcome and handoff

Close B010 as `accepted` or `rejected` with one dated, sanitized report at `docs/research/providers/codex-<YYYY-MM-DD>.md`. B010 adds no code, tests, fixtures, or interfaces under `src/` or `tests/`. An accepted report is B011's contract: qualified client and version, invocation, response schema, limit-window set, reset-time format, used/remaining rounding, failure classification, latency, and constraints. A rejected report makes B011 `not applicable`.

## Rules for every step

- Allowed evidence: the stdout, stderr, and exit code of official Codex client binaries, and locally written non-credential files. Forbidden: `auth.json` or any other credential file, Windows Credential Manager or keyring entries, browser state, direct HTTP calls to OpenAI or ChatGPT endpoints, and interception or proxy capture of the client's traffic.
- Never sign the real account out. Signed-out behavior uses an isolated, empty `CODEX_HOME` (step 3c).
- Raw output stays in `%TEMP%\b10-codex\`, outside the repository, and is deleted at closeout. The report contains only sanitized excerpts. Replace e-mail addresses, account, user, workspace, and thread IDs, and absolute user paths (use `%USERPROFILE%`). The plan tier may stay, because it determines the limit-window set.
- The probe harness is an ad hoc PowerShell script in the temp directory and is not committed. The report reproduces its exact arguments and request messages.
- During polls, no other Codex activity may run. Close the Codex desktop app and IDE extensions, and stop Codex agents in other worktrees. Record this.

## Work order

### 1. Baseline and client inventory

- Confirm that B004 is `done` in the roadmap progress table, create the branch required by [`AGENTS.md`](../../../AGENTS.md), and set B010 to `in progress`.
- For every installed client (currently `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe` and the npm shim `%APPDATA%\npm\codex.cmd`), record:
  - the `--version` output;
  - the canonical path and reparse-point status;
  - the Authenticode signer (`Get-AuthenticodeSignature`);
  - the authoritative installation metadata (uninstall registry entry or global npm package).
- Choose the client to qualify. A script client (the npm shim) needs explicit user consent in B011 ([Provider adapters](../../implementation-plan.md#provider-adapters)); record this as a constraint.

**Gate:** the qualified client, its version, signature status, and installation metadata are recorded.

### 2. Candidate inventory

Evaluate the candidates in order. Stop at the first candidate that passes every gate, and record the rest as "not evaluated — accepted candidate exists". A rejection needs recorded failure evidence for every candidate against a named gate.

| ID | Candidate | Kind | Known risk |
|---|---|---|---|
| C1 | `codex app-server` over stdio JSON-RPC: `initialize` → `initialized` → `account/rateLimits/read` | Official client output | Must be in the client's published protocol schema and not gated behind `experimentalApi`; the client makes the network call with its own login |
| C2 | The interactive TUI `/status` command | Official client output | Needs a console session; fails "non-interactive" unless a documented non-interactive form exists |
| C3 | Rate-limit events from `codex exec --json` | Official client output | Runs a model turn, so it consumes usage; one run that shows a new session is enough evidence |
| C4 | Rate-limit records in the session files `%USERPROFILE%\.codex\sessions\**\rollout-*.jsonl` | Local non-credential file | Written only during sessions, so stale when Codex is idle; gives no sign-in or network failure signal; the files also contain conversation content |
| C5+ | Any other documented command found in `codex --help` or the official docs and release notes for the qualified version | — | Add before evaluating |

Excluded, with reasons recorded in the report: `auth.json`, direct calls to the usage endpoint behind the client, browser state, and traffic interception.

**Gate:** the inventory is complete before the first candidate is rejected.

### 3. Qualify C1

Repeat the same steps for the next candidate if C1 fails.

**a. Contract.** Generate the protocol schema from the qualified binary (for example `codex app-server generate-json-schema --out <temp>`; confirm the command exists at this version). Extract the request and response types of `account/rateLimits/read`: windows (`primary`/`secondary`, `rateLimitsByLimitId`), `usedPercent`, `windowDurationMins`, the unit and timezone of `resetsAt`, `planType`, credits, and the meaning of every nullable field. If the method is experimental or undocumented at the qualified version, C1 fails "stable invocation".

**b. Two consecutive polls (criterion 2).** Each poll is a fresh process with fixed arguments, a neutral working directory, and stdin closed after the response. Record the exit code, stderr byte count, and latency. Both polls must return every limit window and its reset time. Record each window's ID and duration, and identify the short window and every weekly window.

**c. Failure classification (criteria 3 and 6):**
- **Signed out:** run with `CODEX_HOME` pointing to a new empty temp directory. Record whether `initialize` or the read fails, and the exact error envelope (`code`, `data`, message shape).
- **Network failure:** run with the network disabled (airplane mode). A secondary check sets `HTTPS_PROXY=http://127.0.0.1:9` for the child process only. Record the envelope and the time to failure, including any hang, because B011 needs this for its timeout.
- **Unknown format:** define the classifier. Success requires the schema shape; any other success shape is "unknown format". Sign-in and network failures must differ in a structured field. If they differ only in message text, pin the exact strings to the qualified version and record that B011 treats any other text as unknown.
- Access revoked on a live account cannot be tested safely. Record it as unverified.

**d. Non-consumption (criterion 4).** Take a reference reading from the official client before and after the run, not during it. Then run ten C1 polls 60 seconds apart (the B011 polling interval) away from any reset time. Each window's `usedPercent` must stay the same. A started window's `resetsAt` must stay fixed; an unused 5-hour window may report a sliding `resetsAt` near now plus 300 minutes, so record that drift separately rather than treating it as usage. List the session files (names and count only) before and after: no new session may appear.

**e. Reconciliation (criterion 5).** Within one two-minute window, capture the TUI `/status` (cropped and redacted) and one C1 poll. For each window, compare the client's "% left" with `100 − used` under round, floor, and ceiling, and identify the rule the client uses. Round the used share first, then subtract it from 100. The rule counts as verified only with a non-integer `usedPercent` sample that tells the rules apart, or with the client source at the qualified tag that shows the formula. Integer-only samples are not enough: at `codex-cli` 0.159.0 the schema types `usedPercent` as `int32`, because the app-server rounds the client's internal floating-point share with `.round()`, so the samples cannot reveal the rule. Without either, criterion 5 stays unverified and B010 cannot be accepted. Convert each reset time from epoch to local time and compare it with the client's display. Separately from the ten-poll run, run one small Codex task with the user's consent, poll again, and confirm that the used share changes the same way `/status` shows. This proves the source is not a stale cache.

**f. Boundary (criterion 7).** Show that the mechanism only starts the client and reads its pipes: it reads no files and calls no endpoint itself. Record that the client uses its own saved login internally, and justify C1 as official client output under the [product plan](../../plan.md#3-deliver-subscription-limits). If the reviewer disagrees, stop and propose an ADR instead of accepting.

**Gate:** C1 passes or fails each criterion, with evidence.

### 4. Other candidates when C1 fails

Evaluate C2 to C5+ against the same gates. For C4, record staleness: during a quiet period, compare the timestamp of the latest rate-limit record with the current time, and show that the files give no sign-in or network failure signal.

**Gate:** either one candidate passes, or every candidate has a named failed gate (criterion 8).

### 5. Write the report

Sections in this order:
- **Outcome.**
- **Environment:** Windows build, installed clients, versions, signatures, date and timezone.
- **Candidates:** a table with the result of each candidate against each gate.
- **Accepted contract:** invocation and exact request messages; the response schema with a sanitized example; limit-window IDs, names, and durations; reset-time format; rounding rule; failure-classification table; latency; constraints (login, network, version pin, script client or executable).
- **Evidence for criteria 1–8:** pass or fail for each.
- **Unverified items.**
- **Handoff to B011.**

The sanitized examples in the report are not B011 fixtures.

**Gate:** every criterion maps to evidence, and the report contains no raw account output or identifiers.

### 6. Close

- Set B010 to `accepted` or `rejected` in the roadmap progress table. If rejected, set B011 to `not applicable`. Link the report from the package README.
- Run `git diff --check`. The `dotnet` handoff commands do not apply, because no code changes.

## Boundary for B011 and B012

B011 owns production parsing types, implementation of trusted resolution, process isolation and tree termination, polling, expiry, display, and repeating the ten-poll check with the built adapter. B012 qualifies Claude independently and shares no harness or report with B010. Record any new design choice with meaningful alternatives in a new ADR.
