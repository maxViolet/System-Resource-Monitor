# Draw system-metric labels in a secondary colour

Status: accepted, 2026-11-17. Amends the colour sentence of the [product plan's shared bar design](../plan.md#shared-bar-design) for B015.2; no earlier ADR is superseded.

## Decision

Draw the `CPU`, `RAM`, and `GPU` labels in `#9FACBC` and the rest of each system-metric section, the value or the unavailable dash, in the primary `#E6E6E6`. The background, font, 12-DIP size, 24-DIP height, section text, tooltips, and fixed widths do not change. The Codex and Claude sections stay entirely in `#E6E6E6`.

## Reason

The [HTML prototype](../../prototype/task-strip-design.html) is the approved mockup. Its "Current" state differs from the measured [B014 bar](../acceptance/2026-11-12-B14-windows-integration.md) only in this colour: `.metric b { color: #9facbc; }` for the three labels, while the provider sections have no separate label. The other differences the prototype shows, namely task buttons, overflow, dividers, the clock, the in-bar tray, and shell indicators, belong to B019 and B020 under [ADR 0008](0008-replace-taskbar-buttons-in-phases.md).

A colour change does not change the text that WPF measures, so the fixed section widths, the minimum supported width, and the B015.1 geometry stay valid.

## Alternatives

- **Keep one colour.** This keeps the B014 appearance but leaves the mockup's only left-section difference unimplemented.
- **Also dim the provider labels (`Codex:`, `Claude:`).** The mockup does not do this, and the provider texts mix several labels (`5h`, `W`) with values, so it would be a new design rather than the mockup.
- **Add the dividers now.** In the mockup they separate the left sections from tasks and the tray; with empty centre and right regions they would separate nothing.

## Consequences

Each system-metric `TextBlock` holds two runs: the label and the rest. `TextBlock.Text` still returns the full section text, and the tooltip stays on the `TextBlock`, which is the run's parent. The `--metric-display` test-host mode checks both colours and the full text in every display state; `--bar-regions` accepts a hit on a run of a section.
