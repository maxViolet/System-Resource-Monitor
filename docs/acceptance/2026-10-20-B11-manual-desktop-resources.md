# B011 manual desktop and resource gates — 2026-10-20

Brick: [B011 — Codex remaining limits](../bricks/B11-codex-limits.md). Companion reports: [automated](2026-10-20-B11-automated.md), [Windows integration](2026-10-20-B11-windows-integration.md).

Status: open. B011 is not `done`.

| Gate | Result | Notes |
|---|---|---|
| Same-window comparison of every displayed value and reset time with the official client | **unverified** | The TUI `/status` was not opened. The adapter's values came from the official client's app-server, but no reading in the same verification window compared them with the client's own display. |
| Bar shows the Codex section at 100 %, 150 %, and 200 % without clipping | **unverified on the desktop** | During the run the bar was lowered behind a fullscreen foreground window (the terminal), so a screenshot showed no bar. `CodexSectionWidthTests` checks the widths programmatically. |
| Tooltip explains access loss safely | **partial** | On the real client, an empty `CODEX_HOME` produced `SignedOut`, and an unreachable proxy produced `NetworkFailed` (integration report). The tooltip texts are checked in `CodexSectionFormatTests`. Hovering the bar was not observed. |
| Ten-poll non-consumption with the built adapter | pass | Integration report. |
| App plus child private bytes ≤ 150 MiB at every sampled Codex poll on the published artifact | **fail** | 175.0 MiB maximum, 2 of 4 polls over the limit (integration report). |
| 30-minute idle run and eight-hour run with Codex polling (memory, growth, CPU, responsiveness) | **unverified** | Not run. |
| Shutdown of the published app during an in-flight Codex client | **unverified** | Covered by automated tests only. |
| Access revoked on a live account | **unverified** | Not tested safely; inherited from B010. |

## Decision needed

The combined memory budget of ADR 0015 fails with the qualified client: the short-lived `codex app-server` child alone reaches about 100 MiB of private bytes. Changing the budget, the source, or the process model is a design choice with alternatives, so it needs a new ADR that supersedes the relevant part of ADR 0015. Until then, B011 cannot be `done`, and the first release cannot ship with Codex enabled.
