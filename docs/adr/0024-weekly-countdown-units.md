# Show day and hour units in the weekly countdown

Status: accepted, 2026-10-27. Supersedes the weekly countdown text grammar in [ADR 0023](0023-weekly-limit-countdown-display.md). Its used-share meaning, rounding, reset bounds, missing-reset handling, timer updates, and tooltip decisions remain in force. Applies to [B011](../bricks/B11-codex-limits.md) and [B013](../bricks/B13-claude-limits.md).

## Context

The `DD HH` countdown in ADR 0023 makes readers infer which number means days and which means hours. The bar has room for short unit suffixes.

## Decision

- Render an active weekly countdown as `W xx% in Dd Hh`, using decimal days and hours without leading zeroes. For example, six days and 22 hours displays `W 12% in 6d 22h`; one day and zero hours displays `W 12% in 1d 0h`.
- Keep ADR 0023's whole-hour ceiling before splitting days and hours. A positive remainder below one hour displays `0d 1h`. At 0% used or at or after reset, omit the countdown; never show `in 0d 0h`.
- Apply the same text to Codex and Claude, including stale qualified snapshots. Keep the short-window `HH:mm` format and all provider availability rules.

## Alternatives

- Keep `DD HH`: rejected because the unlabeled numbers require interpretation.
- Pad the day and hour numbers: rejected because the bar can show the values without padding and the requested example uses `6d 22h`.

## Consequences

The shared formatter, both section width candidates, display tests, glossary, plan, and B011/B013 packages use the unit-suffixed form. Existing ADR 0023 remains historical context for the rest of the weekly countdown contract.
