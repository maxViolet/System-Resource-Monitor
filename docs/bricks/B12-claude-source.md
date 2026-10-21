# B012 — Qualify a Claude limit source

| | |
|---|---|
| **Area** | Research |
| **Depends on** | B004 |
| **Unblocks** | B013 when accepted; B007 when closed |
| **Sources** | [Product plan — Deliver subscription limits](../plan.md#3-deliver-subscription-limits), [ADR 0004](../adr/0004-subscription-limits-in-first-release.md), [ADR 0017](../adr/0017-claude-limit-display-in-codex-style.md), [ADR 0018](../adr/0018-claude-usage-client-output.md), [ADR 0019](../adr/0019-claude-child-image-load-policy.md), [ADR 0020](../adr/0020-qualify-claude-code-2-1-306.md), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

Claude remaining limits may be shown only from an authorized, non-consuming, automatable source. Its source and failure behavior must be proven independently of Codex.

## What done looks like

A dated report selects one official Claude source under [ADR 0018](../adr/0018-claude-usage-client-output.md) or rejects the track after allowed candidates are exhausted. The selected source is the signed native Claude Code client's non-interactive `/usage` output. Version 2.1.304 is qualified; version 2.1.306 is enabled under [ADR 0020](../adr/0020-qualify-claude-code-2-1-306.md) with its non-consumption repeat still open. Its response contract includes observed windows and a nullable reset only at 0% used. [ADR 0019](../adr/0019-claude-child-image-load-policy.md) requires a child image-loading policy because the native install directory is writable by the local sandbox group.

## Context

Allowed evidence is official client output or a locally written non-credential file. Do not read tokens, cookies, credential files, browser state, or undocumented endpoints. Record client version, invocation or file path, response format, actual limit-window names including any model-specific weekly windows, reset times, used/remaining rounding, constraints, and signed-out behavior. For the ADR 0017 handoff, prove whether the short window is 300 minutes, enumerate every weekly window needed to select the binding weekly value, and identify a genuine monthly subscription window or explicitly mark it unqualified. Redact account output and never commit or log secrets.

## Acceptance criteria

- [x] The report is dated and records every context field above with sanitized examples
- [x] The report proves the short-window duration, enumerates every weekly input to binding selection, and identifies a qualified monthly window or records it as unqualified
- [x] Two consecutive non-interactive polls return the short and every weekly window; active windows include minute-precision reset text and 0%-used windows may omit it
- [x] `auth status --json` identifies sign-out; only an explicit verified client network response may classify network failure, while other missing or changed output is unknown format
- [x] For 2.1.304, ten polls with no other work left used shares unchanged and created no new sessions or equivalent usage; minute reset rendering may differ by one minute
- [ ] Repeat the controlled ten-poll no-consumption check for 2.1.306; the observed series had other account activity
- [x] Remaining values equal `100 −` the integer used share displayed by the official client; hidden fractional rounding is not inferred
- [x] Signed-out behavior is exercised and recorded
- [x] The accepted mechanism reads no credential material and calls no undocumented endpoint
- [ ] If no allowed candidate source passes every gate, the report marks Claude rejected and B013 becomes not applicable; one failed candidate does not reject an untested allowed candidate

## Out of scope

Parsing into production types, process isolation, polling, expiry, and display belong to B013. Codex qualification is independent in B010. The 2026-10-20 rejection is historical evidence under superseded strict gates.
