# B11 — Qualify a Claude limit source

| | |
|---|---|
| **Area** | Research |
| **Depends on** | B07 |
| **Unblocks** | B13 when accepted; B14 |
| **Sources** | [Product plan — Subscription limits](../plan.md#subscription-limits), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

Claude remaining limits may be shown only from an authorized, non-consuming, automatable source. Its source and failure behavior must be proven independently of Codex.

## What done looks like

A dated qualification report at `docs/research/providers/claude-<YYYY-MM-DD>.md` either accepts one Claude source against every gate below or rejects the track after allowed candidate sources are exhausted. Acceptance identifies a stable non-interactive invocation or local non-credential file and a complete response contract for all limit windows and reset times.

## Context

Allowed evidence is official client output or a locally written non-credential file. Do not read tokens, cookies, credential files, browser state, or undocumented endpoints. Record client version, invocation or file path, response format, actual limit-window names including any model-specific weekly windows, reset times, used/remaining rounding, constraints, and signed-out behavior. Redact account output and never commit or log secrets.

## Acceptance criteria

- [ ] The report is dated and records every context field above with sanitized examples
- [ ] Two consecutive non-interactive polls return all limit windows and reset times
- [ ] Sign-in failure and network failure are distinguishable from each other and from unknown format
- [ ] Ten polls with no other work leave limits unchanged and create no new sessions or equivalent usage
- [ ] Remaining values are reconciled with official client output; if only the used share exists, `100 − used` follows verified client rounding
- [ ] Signed-out behavior is exercised and recorded
- [ ] The accepted mechanism reads no credential material and calls no undocumented endpoint
- [ ] If no allowed candidate source passes every gate, the report marks Claude rejected and B13 becomes not applicable; one failed candidate does not reject an untested allowed candidate

## Out of scope

Parsing into production types, process isolation, polling, expiry, and display belong to B13. Codex qualification is independent in B10.
