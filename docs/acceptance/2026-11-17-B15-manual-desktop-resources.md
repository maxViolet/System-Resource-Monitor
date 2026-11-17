# B015 manual desktop and resource gates — 2026-11-17

Brick: [B015 — Redesign the bar](../bricks/B15-bar-redesign.md). Companion reports: [automated](2026-11-17-B15-automated.md), [Windows integration](2026-11-17-B15-windows-integration.md).

Status: open. B015 stays `in progress`, not `done`. The live-scale defect that B014 handed over now passes, and a fullscreen defect found during this session is fixed (PR #65). The resource gates, provider limits, several desktop scenarios, and the published-app stale and unavailable states are unverified; each has a named owner below.

## Identity

| Field | Value |
|---|---|
| Final artifact | Source commit `2e7efb6d7321584f757040eb642f2902508947b1`; `SystemResourceMonitor.Desktop.exe` `cde70736dc1e49028075fdc73d82349333e64468b85be22048faf43716ae0e6e`; loaded `SystemResourceMonitor.Desktop.dll` `13264182…a03c`, `SystemResourceMonitor.Core.dll` `ab603cd8…8841`, `SystemResourceMonitor.AIResources.Core.dll` `692efa29…0dc7` (full values in the [automated report](2026-11-17-B15-automated.md#published-artifact)) |
| Manual-session artifact | Source commit `c13b520` (merge of PR #63); `SystemResourceMonitor.Desktop.exe` `8eaf606957f6da0cec9a16dac4f4d88b76d3ea0e5d6b5f7a4a0b44b496912ada` |
| Fullscreen-fix artifact | Branch `FIX-007-fullscreen-overhang` at `478ebc1`, published locally. The final commit adds only the style recheck and tests from the PR #65 review; it does not change which window styles count as frameless |
| Test host | `SystemResourceMonitor.Desktop.TestHost.exe` `3b95103f21d4418c8e1d3d9dc5efedb6c86c3b06bd05e5d164e46a8cb4a8de3c`, Release, final commit |
| Windows | Windows 11 Pro 26H2, build 26300.9974, x64 |
| Hardware | Laptop, Intel Core Ultra 7 155H, 22 logical processors, 31.4 GiB RAM; NVIDIA GeForce RTX 4070 Laptop GPU and integrated Intel Arc |
| Monitor and DPI | One built-in 2560 × 1600 px primary panel at 125 % (native); live changes to 200 % |
| Windows taskbar | Bottom, not auto-hidden, 32 DIP high |
| Providers enabled | Codex (`codex-cli 0.159.0`) and Claude Code (`2.1.326`), both installed and signed in |

## Scope of this run

The user ran the manual steps in the published app and reported each result; a 0.5-s watcher logged the bar's rectangle, DPI, work area, and `WS_EX_TOPMOST` alongside. Where the log cannot confirm a reported result, the table says so. The 30-minute idle and eight-hour resource runs were not performed. A second monitor, a resolution change, and a primary-monitor change were not available.

## Manual session

| # | Scenario | Artifact | Result | Evidence |
|---|---|---|---|---|
| 1 | Live scale change through Settings, 125 % → 200 % → 125 %, twice | `c13b520` | pass | User observation; watcher log in the [Windows integration report](2026-11-17-B15-windows-integration.md#through-settings-earlier-artifact) |
| 2 | Tooltips on RAM, GPU, Codex, Claude | `c13b520` | pass | User observation; CPU has no tooltip while available, by design |
| 3 | Clicking the bar while typing in Notepad keeps focus in Notepad | `c13b520` | pass | User observation |
| 4 | Tray → Position → Bottom, then Top | `c13b520` | pass | User observation; watcher: Bottom with work area `(0, 0, 2560, 1530)`, then Top with `(0, 30, 2560, 1560)` |
| 5 | A maximized window stops at the bar | `c13b520`; repeated on the fullscreen-fix artifact | pass | User observation; with the fix, a maximized Chrome window did not lower the bar |
| 6 | Explorer restart | `c13b520` | pass | User observation; watcher: the reservation disappeared and returned within 2.5 s with the bar at the top |
| 7 | Video fullscreen and Alt+Tab | `c13b520` and the fullscreen-fix artifact | pass, count partly unverified | User reported 10 YouTube and 3 Twitch cycles; the watcher, which samples every 0.5 s and did not record the foreground window, logged 7 lowerings for the YouTube runs |
| 8 | Game fullscreen (Slay the Spire 2) and Alt+Tab | `c13b520` | **fail** | The bar stayed on top. The game window was `(0, 0, 2562, 1600)`, a frameless popup (`0x96000000`), 2 px wider than the monitor; the classifier required exact equality. The defect dates from B002 |
| 8a | Same, after the fix | Fullscreen-fix artifact | pass, count partly unverified | User reported 10 cycles; the watcher logged lowerings and restorations, 8 for 13 reported game and video cycles. The game's fullscreen setting produced a borderless popup; exclusive fullscreen was not exercised |
| 8b | Same, final artifact | Final | pass, 8 of 10 cycles log-confirmed | The watcher recorded the foreground process: the bar was lowered in each of 8 periods with the game in front and restored each time another window came to the front. One 0.5-s sample showed the bar still on top just after the game came to the front, within the 500-ms reconciliation interval. The user reported 10 cycles |
| 9 | Taskbar auto-hide on, then off | `c13b520` | pass | User observation; watcher: work area bottom 1600, then 1560, with the bar's strip kept |
| 10 | Tray → Exit | `c13b520` | pass | User observation; watcher: process gone and work area `(0, 0, 2560, 1560)` |

## First-release gates

| Gate | Artifact | Result | Measurements or reason |
|---|---|---|---|
| CPU accuracy | Published app | **unverified** | Task Manager was not compared. Owned by B023. |
| RAM accuracy | Published app | **unverified** | Task Manager was not compared. |
| App CPU cost | Published app | **unverified** | The 30-minute idle run was not performed. |
| App memory ceiling | Published app | **unverified** | The 30-minute and eight-hour runs were not performed. |
| Codex polling peak memory | Published app and Codex child | **unverified** | Not measured. The [B011 report](2026-10-20-B11-manual-desktop-resources.md) records a 175.0-MiB combined peak against the 150-MiB limit; that failure stays with B011 and is not waived. |
| Memory growth | Published app | **unverified** | The eight-hour run was not performed. |
| Native-read responsiveness | Test host | **unverified** | The suite runs `--native-reads 10` (10 s, passed); the 30-minute run on the final test host was not performed. |
| Freshness and independence | Test host | pass | `--metric-freshness cpu`, `ram`, and `gpu` in the [automated suite](2026-11-17-B15-automated.md): the blocked reading expires within 500 ms of its six-second deadline while the other sections keep updating (no gap over 2,500 ms). Individual timings were not recorded. Fake-time boundaries are covered by the Core tests. |
| Provider freshness and independence | Test host | pass | `--claude-independence` in the automated suite: Codex expires within 750 ms of its deadline while a Claude poll is blocked, CPU, RAM, and GPU keep updating, and shutdown stays within its deadline. Fake-time provider cases pass in the Core tests. Individual timings were not recorded. |
| Provider limits | Published app | **unverified** | The official clients' displays were not compared with the bar in the same window, and the ten-poll non-consumption check was not run. Both provider sections showed values and tooltips. |
| Normal shutdown | Published app | pass | Sixteen fresh-start runs and four scripted live runs (`WM_CLOSE`) exited with code 0 and restored the work area; tray Exit restored it. |
| Fault shutdown | Test host | pass | `--fail-after-setpos`, `--bounded-shutdown`, `--faulted-metric-run` in the [automated suite](2026-11-17-B15-automated.md). |
| Desktop behaviour | Published app | **unverified** | Passed on one monitor: live scale change both ways, pointer focus, tray edge changes and Exit, maximized window, Explorer restart, auto-hide taskbar, video and borderless-game fullscreen with Alt+Tab after PR #65 (8 game cycles log-confirmed on the final artifact). Unverified: exclusive fullscreen; ten log-confirmed cycles per mode; Explorer restart during fullscreen; second-monitor fullscreen; resolution and primary-monitor changes. |
| DPI and rendering | Published app | **unverified** | Passed on the published app: fresh starts at 100 %, 125 %, 150 %, and 200 % at both edges match B014 pixel for pixel with a matching reservation; live changes 125 % → 200 % → 125 % and 200 % → 125 % follow without a restart; active short and weekly countdowns fit; the RAM tooltip shows used/total GiB ([Windows integration](2026-11-17-B15-windows-integration.md)). Unverified on the published app: 0 %, 100 %, stale, and unavailable states, which were shown only in the test host (`--metric-display`) and the width tests. |
| Artifact hygiene | Published app | **unverified** | No test host, placeholder, fixture, or diagnostic entry point in the folder; a clean profile without the .NET SDK and without provider authorization was not tried. |

## Observed discrepancies

| # | Observation | Documented contract | Handling |
|---|---|---|---|
| 1 | Slay the Spire 2's fullscreen window overhangs the monitor by 2 px, so the bar stayed on top. | Lower only for active fullscreen on the primary monitor ([ADR 0003](../adr/0003-keep-appbar-registered-during-fullscreen.md)). | Fixed in PR #65: a frameless window that covers the monitor with at most 1 % overhang per side counts as fullscreen. |
| 2 | After live scale changes, a maximized Windows Terminal window kept a height that matched the 200 % taskbar while the work area was already correct. | — | Recorded only; not reproduced without the bar, so not attributed. |
| 3 | A full `dotnet test` run from another worktree registered a second AppBar during two scripted live runs. | — | Those runs were discarded and repeated on a clean desktop. |
| 4 | Locking the PC lowered the bar while the lock screen (`LockApp`) was in front, and it returned on unlock. | Lower only for active fullscreen on the primary monitor. | Recorded only; the lock screen is a full-monitor foreground window. |
| 5 | The CPU section has no tooltip while a reading is available. | [Plan](../plan.md): tooltips for RAM used/total and for unavailable readings only. | As specified; unchanged from B014. |

## Owners of non-pass gates

| Gate | Owner | Status of owner |
|---|---|---|
| App CPU cost, app memory ceiling, memory growth, native-read responsiveness | B015 (its criterion to rerun the affected CPU and memory gates) | this brick, open |
| DPI and rendering: 0 %, 100 %, stale, and unavailable states in the published app | B015 | this brick, open |
| Desktop behaviour: exclusive fullscreen, log-confirmed cycle counts, Explorer restart during fullscreen, second monitor, resolution and primary-monitor changes | B015 | this brick, open |
| Codex polling peak memory | [B011](../bricks/B11-codex-limits.md) | open; known failure stays with B011 |
| Provider limits | [B011](../bricks/B11-codex-limits.md) (Codex) and [B013](../bricks/B13-claude-limits.md) (Claude) | open |
| CPU accuracy and RAM accuracy against Task Manager | [B023](../bricks/B23-cpu-accuracy.md) | open (planned) |
| Artifact hygiene on a clean profile | [B018](../bricks/B18-public-distribution.md) | open (planned) |

## B015 acceptance criteria

| Criterion | Evidence | State |
|---|---|---|
| Published bar keeps the current visual state at 100 %, 150 %, and 200 %; a dated report records redacted crops, rendered bounds, monitor width, DPI, and artifact identity | [Windows integration — geometry](2026-11-17-B15-windows-integration.md#measured-bar-geometry); [crops](2026-11-17-B15-bar-crops/) | pass |
| Fixed left, flexible centre, and right regions; test-host placeholders fill centre and right without moving the left sections; no placeholder in the published app | `--bar-regions` ([automated](2026-11-17-B15-automated.md#what-the-suite-covers-for-b015)); [published regions](2026-11-17-B15-windows-integration.md#regions-in-the-published-app) | pass |
| Live scale change both ways without a restart; fresh starts at 125 % keep the visual state | [Live scale change](2026-11-17-B15-windows-integration.md#live-scale-change) | pass |
| 24 DIP at both edges, correct work area, accepted fullscreen and Explorer-recovery behaviour | Geometry table; manual steps 4, 6, 8a, 8b | **unverified**: height, work area, borderless-game and video fullscreen, and Explorer restart pass; exclusive fullscreen, Explorer restart during fullscreen, and ten log-confirmed cycles per mode are not yet evidenced |
| Sections keep fixed widths, text, tooltips, unavailable states, and order; unqualified providers have no section | Section bounds; tooltips; `--provider-omission` and format tests | **unverified**: order, widths, text, and tooltips pass in the published app; unavailable states and unqualified-provider omission are evidenced in the test host only |
| Widest values and unavailable states fit at the measured widths; narrower widths recorded | `--metric-display`, width tests; [supported widths](2026-11-17-B15-windows-integration.md#supported-widths) | pass at the measured widths (test host for widest and unavailable values); narrower monitors are recorded as unsupported, not observed |
| Pointer interaction does not activate the bar; tray switches edge and exits | Manual steps 3, 4, 10 | pass |
| Targeted tests and affected B007 rendering, desktop, CPU, and memory gates pass; evidence kept distinct | Three separate reports; gate table above | **unverified**: targeted tests pass; CPU, memory, desktop, and DPI-and-rendering gates are unverified |
