# Show the weekly reset countdown beside used limits

Status: accepted, 2026-10-27. Supersedes only the weekly bar-text clause of [ADR 0021](0021-used-limit-display.md). Its used-share meaning, short-window format, monthly omission, freshness, and tooltip decisions remain in force. Applies to [B011](../bricks/B11-codex-limits.md) and [B013](../bricks/B13-claude-limits.md).

## Context

Both providers show the used share of the binding weekly window, while its reset time appears only in the tooltip. Showing the time until reset next to that share makes the weekly value useful without opening the tooltip. Codex supplies a weekly reset time; Claude may omit one at 0% used.

## Decision

- Render the weekly field as `W 67% in DD HH`, using the binding weekly window's used share and qualified reset time. For example, `Codex: 5h 24% in 02:37 | W 67% in 01 12` and `Claude: 5h 24% in 02:37 | W 18% in 01 12` retain ADR 0021's short-window grammar and omit the monthly field.
- Calculate `DD HH` from the reset time and current time. Round a positive remainder up to the next whole hour, then split into days and hours. Each field has at least two digits and a space between fields: 1 day 12 hours 20 minutes displays `01 13`. Re-render when either the short or weekly countdown changes, including between provider polls.
- At 0% used, show `W 0%` without a countdown even when the reset is absent: the used share remains qualified, and an unused window may have a sliding reset. At or after the weekly reset time, keep the qualified used share but omit the countdown, for example `W 67%`; never show `in 00 00`. Above 0% used, when the binding weekly reset time is absent or untrustworthy, show `W --`. Reject a Codex weekly reset more than its qualified seven-day window plus one minute ahead; Claude's accepted parser already bounds resets to eight days. Explain an out-of-range reset in the tooltip instead of presenting it as exact. A fully unavailable provider retains ADR 0021's `<Provider>: 5h -- | W --` text. Existing stale labels and qualified tooltip reset times remain unchanged.

## Alternatives

- Keep the weekly reset only in the tooltip: rejected because the user wants the time until reset visible on the bar.
- Truncate partial hours: rejected because a positive remainder below one hour would show `00 00`.
- Show a positive used share without a known reset time: rejected because it would look like a complete active weekly display. At 0% used, the qualified share remains visible without a countdown.

## Consequences

Both fixed-width provider sections need wider candidates and rendering checks at supported DPI scales. Tests cover hour rounding, day rollover, missing reset, the reset boundary, stale values, and timer updates.
