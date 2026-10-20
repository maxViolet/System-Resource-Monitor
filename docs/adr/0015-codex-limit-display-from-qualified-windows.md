# Codex limit display from qualified windows

Status: accepted, 2026-10-20. Applies to [subscription limits](../plan.md#3-deliver-subscription-limits) and [B011](../bricks/B11-codex-limits.md).

## Context

B010 qualified one `codex` bucket with a 300-minute window and a 10080-minute window on the signed standalone `codex-cli 0.159.0`. It did not qualify a monthly window or per-model buckets. While the 5-hour window is unused, its `resetsAt` moves forward with each poll, so showing that value as a reset countdown would imply a running window that has not started. The user wants the remaining share and reset countdown visible in the bar, plus a monthly field.

## Decision

- Use the existing Codex login through the installed signed client. B011 starts a fresh, version-checked `codex app-server --listen stdio://` child for each one-minute poll and reads only the qualified `account/rateLimits/read` response. Keep the app-server experimental status and version pin visible in B011 acceptance.
- Show the aggregate qualified bucket as `Codex: 5H - 76% in HH MM | W 33% | month: --`, replacing the example values with current readings. Compute remaining as `100 - usedPercent`; calculate `HH MM` from `resetsAt` only when the 5-hour remaining share is below 100%. At 100%, show `5H - 100%` without a countdown. Show exact qualified reset times in the tooltip. The weekly value is the qualified 10080-minute window.
- `month: --` means no qualified monthly source. It stays visible so the field cannot be mistaken for zero or omitted by accident. A monthly percentage or per-model split requires a separate source qualification and an updated display decision. Do not infer either from the weekly bucket or the subscription's calendar.
- Preserve independent provider freshness: signed-out and unknown-version/format failures become unavailable on the first failed poll; network failure may show explicitly stale qualified values only until three minutes after the last success. An unavailable Codex snapshot renders `Codex: 5H - -- | W -- | month: --` with a safe explanation in the tooltip. A missing monthly field never makes the qualified 5-hour or weekly values unavailable.
- B011 must measure the published app plus its Codex child at every poll. Their combined private bytes must remain at or below 150 MiB, including the short child lifetime. The existing app-only ceiling remains in force.

## Alternatives

- Show only `short%/weekly%` and put reset times in a tooltip: rejected because the requested 5-hour countdown needs to be visible on the bar.
- Derive a monthly percentage from observed weekly use or omit the monthly field: rejected because neither communicates that the accepted source lacks a monthly window.
- Keep one app-server process alive between polls: deferred because B010 measured independent short-lived processes; a persistent child has different memory, lifecycle, and non-consumption behavior and needs fresh qualification.

## Consequences

B011 has a wider fixed-width section and must test 100%, active, unavailable, and stale states without clipping. Its resource evidence must capture brief child-process peaks. Bundling the client remains a separate later decision.
