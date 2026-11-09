# Side task: accept Claude 2.1.319 context advice

Status: planned. Owner: B013. Recorded: 2026-11-09.

## Sources and scope

- [B013 brief](../B13-claude-limits.md) and [package](README.md).
- [Product plan: deliver subscription limits](../../plan.md#3-deliver-subscription-limits).
- [Implementation plan: provider adapters](../../implementation-plan.md#provider-adapters).

Restore compatibility with the official client's local usage advice without changing subscription-limit semantics or weakening rejection of unknown formats. Read the owning package and its linked sources before implementation. This task does not close B012 or B013 acceptance gates.

## Symptom and evidence

The user reported that the running bar no longer displayed Claude limits. A live invocation of the official Claude Code 2.1.319 `/usage` command returned a successful local-command JSON envelope with zero turns, API duration, and cost. It reported 7% session usage and 3% weekly usage at the observation time; these are historical observations, not expected values for later checks.

The local-insights section included this new advice shape:

```text
42%  See what fills your context · run /context
```

In the source tree beside the running application, `ClaudeUsageReader.InsightAdviceLine` does not recognize this advice. `IsLocalInsightLine` rejection therefore causes the reader to reject the entire snapshot, even though the subscription windows are present.

The running executable was under `%TEMP%\srm-run\src\system-resources\SystemResourceMonitor.Desktop\bin\Release\net10.0-windows\win-x64\publish`. The inspected source was under `%TEMP%\srm-run`; correspondence between those sources and the running binary was not verified. Reproduce against the intended implementation checkout and identify the tested artifact before claiming the live failure is fully proven or fixed. The current roadmap checkout predates that inspected implementation.

## Implementation work

1. Identify the intended implementation revision and read its current Claude qualification contract and applicable ADRs.
2. Add a sanitized fixture containing recognized subscription windows and the `/context` advice in the qualified local-insights section. Prove the existing reader rejects it.
3. Extend the bounded advice grammar to recognize the observed text. Keep insight percentages separate from subscription windows and retain all envelope, size, reset-time, and unknown-format checks.
4. Add regression coverage for the new advice, unchanged limit selection, and malformed or unknown advice rejection. Retain earlier supported advice forms.
5. Build and verify the published application against the official client in the same observation window.

## Acceptance criteria

- [ ] The sanitized reproduction fails before the parser change and passes afterward.
- [ ] Recognized `/context` advice is accepted in the qualified local-insights section; its percentage never changes the short or binding weekly limit.
- [ ] Unknown advice, malformed percentages, invalid envelopes, and unqualified percentage rows remain rejected.
- [ ] Existing supported local-insights fixtures still pass.
- [ ] Targeted tests and applicable restore, build, solution tests, publish, and whitespace checks follow [automated verification and handoff](../../implementation-plan.md#automated-verification-and-handoff); blocked checks are explicitly unverified.
- [ ] A Windows 11 same-window comparison confirms displayed values and reset times against official `/usage`, with client version, observation time, and tested artifact identity recorded in a dated report under `docs/acceptance/`.

## Out of scope

Changes to polling, freshness, display formatting, authentication, client trust rules, other providers, or the product contract. Do not read credential files, call undocumented endpoints, ignore arbitrary insight text, or retain raw account output. Any later evidence report or contract change follows the repository's documentation approval rule.
