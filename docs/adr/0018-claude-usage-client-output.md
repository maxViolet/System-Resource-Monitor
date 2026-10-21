# Read Claude limits through the official client's `/usage` output

Status: accepted, 2026-10-21. Supersedes the strict source-precision and failure-classification requirements for Claude in [the subscription plan](../plan.md#3-deliver-subscription-limits) and [B012](../bricks/B12-claude-source.md), and ADR 0017's expectation of an exact reset for every window. [ADR 0017](0017-claude-limit-display-in-codex-style.md) still governs the compact presentation.

## Context

The signed native Claude Code 2.1.304 client answers `-p "/usage"` locally with limit rows, zero model turns, zero API duration, and no new transcript in a ten-poll run. Its JSON envelope contains a text `result`, not a versioned limit schema. On an unused window (0% used), the reset may be absent. For active windows, the displayed reset varied by one minute in the controlled run. A signed-out invocation can return success without limit rows. The [2026-10-20 report](../research/providers/claude-2026-10-20.md) rejected this source under the earlier strict criteria.

## Decision

- Use only the verified Anthropic-signed native `%USERPROFILE%\.local\bin\claude.exe` at version 2.1.304, with fixed `--safe-mode --tools "" --no-session-persistence --print --output-format json "/usage"` arguments. Resolve without `PATH`, reject reparse redirection and untrusted owners, and hold a read lease that denies file writes and deletes while starting it. The observed native installation grants a local sandbox group Modify rights. For this Claude path, the verified signature, pinned version, fixed path, trusted owner, and lease replace the generic adapter's strict no-other-writer ACL rule. Codex's stricter ADR 0016 policy remains unchanged.
- Run the official `claude auth status --json` command before `/usage`. Its explicit `loggedIn: false` classifies sign-out. A successful login preflight followed by missing or changed usage rows is unknown format, not an assumed network error. Classify network failure only if a future pinned client observation gives an explicit, verified network error. Unknown format immediately makes Claude unavailable; stale display is reserved for positively identified network errors.
- Parse only the JSON envelope and recognized `Current session` and `Current week (...)` rows. Require the short window and at least one weekly window. A newly appearing monthly or other row is an unknown format until separately qualified. Accept a missing reset only when that window has 0% used. Keep all observed weekly rows and choose the lowest remaining share for the bar. Calculate remaining as `100 -` the integer used share printed by the client; do not infer hidden fractional precision.
- Treat reset values as minute-precision display times, with an observed one-minute variation. Show this precision in the tooltip. Do not claim second-level exactness. An unused short window shows 100% without a countdown. A missing weekly reset at 0% has no reset time in the tooltip.
- Poll every minute through an independently bounded child process. Reject unknown output and unsupported versions. Do not open credential or transcript contents, call an endpoint directly, or log raw client output.

## Alternatives

- Keep the earlier strict rejection: preserves exact reset and three-way classification requirements but leaves Claude limits absent despite an official non-consuming source.
- Use documented status-line JSON alone: gives structured five-hour and seven-day fields, but only after a model response, with independently optional windows and no independent idle refresh.
- Read Claude's credential cache or call its internal usage endpoint: rejected by the project source boundary.
- Reject the native install because its local sandbox group can write the directory: preserves the generic ACL rule but prevents this signed client from supplying limits. Copying the 293 MB client into an app-owned locked directory would require packaging and update ownership that this brick has not established. The chosen fixed-path signature and lease check permits the installed client with the stated local ACL risk.

## Consequences

The display can show Claude's reported percentages on the tested Pro account. Reset times are approximate to one displayed minute, and 0%-used windows can have no reset time. Network staleness is not entered until a distinct network response is verified; otherwise failure is immediately unavailable. Other plan tiers, client versions, locales, access revocation, and production artifact resource gates remain separate acceptance work. The source contract is pinned to one client version and can require updating when Claude Code changes its text.
The Claude executable resides in a directory writable by the local sandbox group. The fixed path, signature check, and held file lease narrow the risk; a future installer or ACL change requires requalification.
