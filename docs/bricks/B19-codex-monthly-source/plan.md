# B019 implementation plan for coding agents

Status: planned. This package implements [B019 — Qualify a Codex monthly limit source](../B19-codex-monthly-source.md). The [product plan — Codex monthly limit](../../plan.md#codex-monthly-limit), [implementation plan — Codex monthly limit](../../implementation-plan.md#codex-monthly-limit), [provider adapter requirements](../../implementation-plan.md#provider-adapters), [ADR 0015](../../adr/0015-codex-limit-display-from-qualified-windows.md), [B010 report](../../research/providers/codex-2026-10-19.md), and [CONTEXT language](../../../CONTEXT.md#language) remain authoritative.

## Outcome and handoff

Close B019 as `accepted` or `rejected` with one dated, sanitized report at `docs/research/providers/codex-monthly-<YYYY-MM-DD>.md`. B019 adds no production code, tests, fixtures, or interfaces under `src/` or `tests/`. An accepted report is B020's source contract. A rejected report makes B020 `not applicable` and leaves `month: --` unchanged.

## Rules for every step

- Allowed evidence: stdout, stderr, and exit codes from official Codex client binaries, plus locally written non-credential files. Forbidden: credential files, Windows Credential Manager, browser state, direct HTTP calls to OpenAI or ChatGPT endpoints, traffic interception, and session files containing conversation content.
- Never infer monthly usage from the weekly bucket, subscription calendar, billing date, or a series of observations.
- Never sign the real account out. Exercise signed-out behavior only through an isolated empty client home supported by the qualified client.
- Keep raw output and ad hoc probes under `%TEMP%\b19-codex-monthly\`; delete them at closeout. Commit only sanitized schemas and evidence.
- During non-consumption checks, stop other Codex work and record that condition.

## Work order

### 1. Establish the baseline

- Confirm B007 is `done` and B010 is `accepted`; set B019 to `in progress`.
- Record the installed official clients, versions, canonical paths, installation metadata, Authenticode status, and whether B011 currently accepts each version.
- Reproduce the B010 result that the qualified `codex-cli 0.159.0` aggregate response contains only 300- and 10080-minute windows. This establishes absence, not a rejected monthly track.

**Gate:** the report identifies the exact baseline and does not reinterpret the weekly window as monthly.

### 2. Inventory allowed candidates

Evaluate every allowed candidate before rejection:

1. Documented non-interactive output from the current official client.
2. Documented non-interactive output from a newer signed official client, without replacing the installed qualified version until its provenance and compatibility are recorded.
3. Official interactive status output only when a documented automation-safe, non-consuming form exists.
4. Locally written non-credential files that contain a current monthly value and reset without conversation content.
5. Additional candidates found in official client help, schemas, release notes, or documentation.

Record credential files, browser state, session transcripts, direct endpoints, and traffic interception as excluded rather than failed candidates.

**Gate:** each candidate has a source-boundary decision and named pass/fail evidence.

### 3. Qualify the first viable candidate

- Prove two consecutive fresh polls return the monthly value and reset time without model work.
- Establish whether the window is rolling, calendar-based, fixed-duration, or provider-named; record its identifier, units, timezone, precision, and reset behavior.
- Compare the same poll with the official client display. If the source returns used share, determine the official rounding before computing remaining share.
- Exercise signed-out, network-failure, absent-monthly, and malformed/unknown responses without touching the live login.
- Run ten polls at B011's one-minute interval with no other Codex activity. Verify unchanged limits away from a reset and no new sessions or equivalent usage.
- Record whether the monthly read can be added to B011's existing request and process, requires a replacement qualified client version, or requires an independent source. Recheck the 5-hour and weekly contract when the client or request changes.

**Gate:** one candidate passes every B019 criterion, or every allowed candidate has a recorded failed gate.

### 4. Write the report and decision inputs

Use these sections: outcome; environment; candidate inventory; accepted contract when any; monthly semantics; same-window reconciliation; failure classification; non-consumption; security boundary; compatibility with B011; evidence by criterion; unverified items; B020 handoff.

If acceptance leaves meaningful alternatives for source composition, version migration, freshness, or display semantics, list them for the new ADR required at the start of B020. Do not select a production design inside the research report.

**Gate:** the report contains no credentials, account identifiers, raw account output, or invented monthly values.

### 5. Close

- Set B019 to `accepted` or `rejected` in the roadmap. If rejected, set B020 to `not applicable`.
- Link the report from the package README.
- Run `git diff --check`; no .NET validation applies because B019 changes documentation and research evidence only.

## Boundary for B020

B020 owns the production contract, source composition, parsing, polling and freshness, replacement of `month: --`, desktop evidence, and affected resource gates. It must record a new ADR before code when the accepted monthly contract changes ADR 0015's display or source decision.
