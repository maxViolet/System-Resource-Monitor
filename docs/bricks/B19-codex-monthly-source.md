# B019 — Qualify a Codex monthly limit source

| | |
|---|---|
| **Area** | Research |
| **Depends on** | B007 done; B010 accepted |
| **Unblocks** | B020 when accepted |
| **Sources** | [Product plan — Codex monthly limit](../plan.md#codex-monthly-limit), [Implementation plan — Codex monthly limit](../implementation-plan.md#codex-monthly-limit), [ADR 0015](../adr/0015-codex-limit-display-from-qualified-windows.md), [B010 qualification report](../research/providers/codex-2026-10-19.md) |

## Why

B010 did not qualify a monthly Codex window. A percentage or reset time cannot replace `month: --` until an authorized, non-consuming source proves what “monthly” means and supplies the complete value.

## What done looks like

A dated qualification report at `docs/research/providers/codex-monthly-<YYYY-MM-DD>.md` either accepts one monthly source against every gate below or rejects the track after every allowed candidate is exhausted. Acceptance identifies the official client and version, a stable non-interactive invocation or locally written non-credential file, the window's exact semantics, remaining-value rule, reset time, failure contract, and compatibility with the existing qualified 5-hour and weekly windows.

## Context

Allowed evidence is official client output or a locally written non-credential file. Do not read tokens, cookies, credential files, browser state, or undocumented endpoints. Do not infer a monthly percentage from weekly usage, elapsed calendar time, billing dates, or subscription tier. Treat rolling, calendar, and provider-named windows as different contracts until official output establishes their meaning.

B010's `codex-cli 0.159.0` contract remains historical evidence, not proof that a later client has a monthly window. Qualify the exact client version and source independently. Raw account output stays outside the repository; reports contain only sanitized values and shapes.

## Acceptance criteria

- [ ] The dated report inventories every allowed candidate and records client version, source, window name, duration or calendar rule, reset semantics, value precision, and constraints
- [ ] Two consecutive non-interactive polls return the monthly remaining or used value and its reset time without starting model work
- [ ] The monthly value and reset time are reconciled with the official client in the same verification window, including the verified used-to-remaining rounding rule when applicable
- [ ] Ten polls with no other Codex work leave limits unchanged and create no new sessions or equivalent usage
- [ ] Sign-in, network, missing-monthly, and unknown-format outcomes are distinguishable, or the report rejects the candidate
- [ ] The accepted mechanism reads no credential material, calls no undocumented endpoint, and records whether it can share B011's trusted client and poll
- [ ] If a newer client is required, its Authenticode identity, installation metadata, canonical path behavior, and effect on the qualified 5-hour and weekly contract are recorded
- [ ] If no allowed candidate passes every gate, the report marks B019 `rejected` and B020 becomes `not applicable`; one failed candidate does not reject an untested allowed candidate

## Out of scope

Production parsing, polling, freshness, UI changes, and release/resource gates belong to B020. Per-model limits, billing balances, a bundled client, browser automation, credential access, and direct undocumented API calls remain out of scope.
