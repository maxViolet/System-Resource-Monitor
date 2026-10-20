# Show Claude limits in the Codex-style compact format

Status: accepted, 2026-10-20. Applies to [subscription limits](../plan.md#3-deliver-subscription-limits), [B012](../bricks/B12-claude-source.md), and [B013](../bricks/B13-claude-limits.md).

## Context

The product plan previously specified `short%/weekly%` for Claude while B011 used a labelled 5-hour value, an active countdown, a weekly value, and a visible monthly field. The user wants the providers to use the same compact grammar. Claude may expose more than one weekly window and may or may not expose a genuine monthly subscription window, so visual parity cannot discard qualified windows or turn a missing value into zero.

B012 has not yet qualified a source. The display contract therefore cannot assume that Claude's short window is exactly five hours or that a monthly window exists.

## Decision

- When B012 proves a 300-minute short window, render Claude as `Claude: 5H - 76% in HH MM | W 33% | month: 82%`, replacing the example values with current qualified remaining limits.
- `W` is the binding weekly window: the qualified weekly window with the lowest remaining limit. Keep every qualified weekly window and exact reset time in the tooltip.
- Below 100% short-window remaining, calculate `HH MM` from its qualified reset time and round up to the next whole minute so a positive duration never displays `00 00`. At 100%, show `5H - 100%` without a countdown.
- Show a qualified monthly remaining value as `month: N%` and its exact reset time in the tooltip. If B012 qualifies no genuine monthly subscription window, keep `month: --` visible and explain that it is unqualified, not zero.
- An unavailable Claude snapshot renders `Claude: 5H - -- | W -- | month: --`. Stale and unavailable explanations remain distinct from the unqualified monthly placeholder.
- B012 must identify the short-window duration, every weekly window needed for binding selection, and any genuine monthly window. If the accepted source does not prove a 300-minute short window, B013 cannot use the `5H` label and this display decision must be revisited instead of presenting a false duration.

## Alternatives

- **Retain `short%/weekly%`:** rejected because it keeps different provider grammars and omits the requested visible countdown and monthly state.
- **Omit `month`:** rejected because a stable field makes a qualified monthly value visible and an unqualified one explicit.
- **Always show `month: --`:** rejected because it would hide a trustworthy monthly window if B012 qualifies one.
- **Show every weekly window in the bar:** rejected because the bar must remain compact; the binding weekly value communicates the limiting window and the tooltip preserves the complete snapshot.
- **Assume Claude's short window is five hours before qualification:** rejected because labels must come from the accepted source contract.

## Consequences

B012 must reject a source that omits an actual short or weekly window and must record enough metadata for binding-weekly selection and optional monthly display. B013 needs display coverage for active countdown, 100%, qualified monthly, unqualified monthly, stale, unavailable, tooltip, and DPI/no-clipping states. Claude and Codex share a visual grammar but retain independent source contracts, failure classification, polling, and qualification evidence.