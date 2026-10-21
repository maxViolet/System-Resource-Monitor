# B020 — Codex monthly limit

| | |
|---|---|
| **Area** | Core + Desktop |
| **Depends on** | B011 done; B019 accepted |
| **Unblocks** | — |
| **Sources** | [Product plan — Codex monthly limit](../plan.md#codex-monthly-limit), [Implementation plan — Codex monthly limit](../implementation-plan.md#codex-monthly-limit), [Provider adapters](../implementation-plan.md#provider-adapters), [ADR 0015](../adr/0015-codex-limit-display-from-qualified-windows.md), [ADR 0021](../adr/0021-used-limit-display.md), [B019 qualification brick](B19-codex-monthly-source.md) |

## Why

B011 intentionally renders `month: TODO` because no trustworthy monthly source was qualified. Once B019 accepts a source, the app can replace that placeholder without weakening the existing 5-hour and weekly contract.

## What done looks like

The Codex section shows the qualified monthly used limit and exposes its exact reset time and window meaning in the tooltip. The 5-hour and weekly values retain their accepted behavior. Monthly acquisition is non-consuming, bounded, independently fresh where its source is independent, and unavailable rather than inferred when its qualified data is absent.

## Context

Implement only B019's accepted client version, invocation or local non-credential file, response contract, monthly semantics, and rounding rule. Before code, create a new ADR that supersedes ADR 0015 where the monthly source changes source composition, client version, or freshness, and ADR 0021 where it changes display semantics. Never read credentials, call undocumented endpoints, or derive monthly usage from weekly data.

Reuse B011's trusted resolver and process lifecycle only when B019 proves the same client and poll contract. Otherwise keep source failures and freshness independent so a monthly failure does not erase valid 5-hour or weekly values. Unknown versions, absent monthly fields, and unknown formats are unavailable.

## Acceptance criteria

- [ ] A new ADR records the accepted monthly source, source composition, display semantics, failure behavior, and meaningful alternatives before implementation
- [ ] Parser and contract tests cover the qualified monthly value, reset time, semantics, rounding, absent field, malformed output, unknown version, and interaction with 5-hour and weekly values
- [ ] Polling tests prove no overlap or usage consumption and prove source-specific failure cannot delay or erase still-valid independent windows
- [ ] Timeout, cancellation, and shutdown tests cover every executable source; bounded path/read failures cover every local-file source
- [ ] Display and tooltip tests replace `month: TODO` only for a qualified current value and cover unavailable, stale, reset-boundary, and DPI-width cases
- [ ] Same-window manual checks compare every displayed value and reset time with the official client and explain the monthly window meaning
- [ ] The ten-poll non-consumption check is repeated with the built adapter, and no new sessions or equivalent usage are created
- [ ] Affected publish, shutdown, responsiveness, CPU, and memory gates are rerun on the exact artifact with all Codex windows enabled
- [ ] Raw account output, credentials, and sanitized fixtures are absent from logs and the published payload

## Out of scope

Per-model limits, billing balances, client bundling, browser automation, credential access, undocumented endpoints, and unrelated provider changes remain out of scope. If B019 is rejected, B020 is `not applicable` and B011 keeps `month: TODO`.
