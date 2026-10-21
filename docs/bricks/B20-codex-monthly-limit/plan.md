# B020 implementation plan for coding agents

Status: planned and conditional on B019 acceptance. This package implements [B020 — Codex monthly limit](../B20-codex-monthly-limit.md). Read the accepted B019 report, [product plan — Codex monthly limit](../../plan.md#codex-monthly-limit), [implementation plan — Codex monthly limit](../../implementation-plan.md#codex-monthly-limit), [provider adapter requirements](../../implementation-plan.md#provider-adapters), [ADR 0015](../../adr/0015-codex-limit-display-from-qualified-windows.md), [ADR 0021](../../adr/0021-used-limit-display.md), the full [B011 package](../B11-codex-limits/README.md), and [CONTEXT language](../../../CONTEXT.md#language). The brief's acceptance criteria and out-of-scope section are binding.

## Ordered work and gates

### 1. Freeze the accepted contract and decision

- Confirm B011 is `done` and B019 is `accepted`; otherwise do not implement B020.
- Copy no raw account output into the repository. Convert only the sanitized accepted shape into fixtures.
- Create a new ADR that supersedes ADR 0021's monthly placeholder decision. Record whether monthly data shares B011's poll, replaces the qualified client version, or uses an independent source; define display text, reset meaning, freshness, partial failure, and migration behavior.
- If a newer client is required, requalify trusted resolution, Authenticode identity, installation layout, and the existing 5-hour and weekly contract before changing production code.

**Gate:** the ADR and tests can name one exact source contract, with no inferred values or unresolved source alternatives.

### 2. Extend the model and bounded acquisition

- Represent monthly semantics explicitly; do not overload the 10080-minute weekly window or infer a duration.
- Parse only the accepted identifier, shape, precision, reset format, and client version. Preserve B011's limits on bytes, lines, messages, depth, timeout, and process-tree ownership.
- Share acquisition only when B019 and the ADR prove one request returns all windows under the same failure boundary. Otherwise poll the monthly source independently and preserve valid 5-hour and weekly state when monthly acquisition fails.
- Display the monthly used limit under the accepted rounding rule, as ADR 0021 requires. Missing monthly data is unavailable, never zero.

**Gate:** deterministic parser, state, no-overlap, partial-failure, timeout/path-failure, cancellation, and expiry tests pass without weakening B011 coverage.

### 3. Replace the placeholder presentation

- Add the ADR-defined qualified monthly used limit to the bar only while it is current or explicitly stale under the accepted policy.
- Keep the exact monthly reset and window meaning in the tooltip. Preserve the existing 5-hour countdown and weekly value.
- Cover 0%, 100%, unavailable, stale, reset boundaries, and the longest accepted text at 100%, 150%, and 200% scaling.

**Gate:** rendering checks prove every state fits and no unknown or absent monthly source produces a percentage.

### 4. Verify the built adapter and artifact

- Run targeted tests, then the repository restore, build, full test, publish, and `git diff --check` commands.
- Compare monthly, weekly, and 5-hour values and reset times with the official client in one verification window. Explain the monthly semantics in the evidence.
- Repeat ten one-minute polls with no other Codex work and verify unchanged usage plus no new sessions.
- Rerun affected desktop, shutdown, responsiveness, CPU, app-only memory, combined app-plus-child memory, 30-minute idle, and eight-hour gates on the exact published artifact.
- Record automated, Windows integration, and manual/resource evidence separately under `docs/acceptance/`, including commit, hashes, client version, sample counts, maxima, and pass/fail.

**Gate:** every B020 criterion has passing evidence; unrun manual or resource checks remain unverified and B020 remains `in progress`.

## Boundary

B019 owns source qualification. B020 owns only the accepted monthly production path and its effect on the existing Codex section. Per-model data and any source outside B019 require another qualification brick and decision.
