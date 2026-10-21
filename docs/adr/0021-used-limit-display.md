# Show used limits in one compact format for both providers

Status: accepted, 2026-10-21. Supersedes the display decisions of [ADR 0015](0015-codex-limit-display-from-qualified-windows.md) (the bar text, the 100% no-countdown rule, and the visible `month: --` field) and of [ADR 0017](0017-claude-limit-display-in-codex-style.md) (the bar text, the remaining-share values, the countdown rule, and the `month` field), and the display clause of [ADR 0020](0020-qualify-claude-code-2-1-306.md) (remaining shares and `month: --`). Their source, client, process, binding-weekly, monthly-qualification, and freshness decisions remain in force. Applies to [subscription limits](../plan.md#3-deliver-subscription-limits), [B011](../bricks/B11-codex-limits.md), [B013](../bricks/B13-claude-limits.md), and [B020](../bricks/B20-codex-monthly-limit.md).

## Context

ADR 0015 and ADR 0017 show each provider as `5H - 76% in HH MM | W 33% | month: --`, where every percentage is the remaining limit. The user wants the bar to show how much of each window is already used, with a clock-style countdown and no monthly field. Both accepted sources already report integer used shares: Codex `usedPercent` and Claude `/usage` `N% used`. The remaining limit is derived from them as `100 − used`, so the used limit can be shown without a new rounding rule. Neither accepted source qualifies a monthly window.

## Decision

- Render each qualified provider as `<Provider>: 5h 24% in HH:mm | W 67%`, replacing the example values with current used limits. `<Provider>` is `Codex` or `Claude`. A stale snapshot keeps the `(stale)` label suffix, for example `Claude (stale):`.
- Every percentage on the bar and in the tooltip is the used limit, the integer used share the official client reports. Tooltip lines read `N% used`.
- `W` is the binding weekly window. Its definition does not change: the weekly window with the highest used limit is the window with the lowest remaining limit.
- `HH:mm` is the short-window countdown. It is shown only while the short window is used above 0%. Round a positive duration up to the next whole minute, so it never displays `00:00`. A passed reset displays `00:00`. Cap the text at `99:59`. At `5h 0%`, show no countdown, because the reset time of an unused window slides with each poll or is not reported.
- The bar has no monthly field for either provider. An unqualified monthly window is not shown and its tooltip gets no placeholder line. A qualified Claude monthly window stays in the limit snapshot and appears in the tooltip with its used limit and reset time.
- An unavailable snapshot renders `<Provider>: 5h -- | W --` with the existing safe tooltip explanation.

## Alternatives

- **Keep remaining limits:** rejected because the user wants to see consumed capacity.
- **Show used limits only for Claude:** rejected because two meanings of `%` side by side on one bar invite misreading.
- **Keep a visible monthly field (`month: --` or `month: TODO`):** rejected by the user; no accepted source supplies a monthly value, so the field only takes width.
- **Bracketed labels (`[5h]`, `[W]`):** rejected by the user as visual noise.
- **Show the reset clock time instead of a countdown:** rejected because the countdown answers how long the remaining capacity must last; exact reset times stay in the tooltip.

## Consequences

The Desktop section formatters, their width candidates, and their tests change; Core snapshots and parsers do not, because both already hold the used share. Manual account checks compare the displayed used limits directly with the official client's used values. The published-app evidence recorded before this decision shows the superseded format and remains dated history. B020 must record a new ADR that adds a monthly field to the bar in the used-limit form if B019 qualifies a Codex monthly source.
