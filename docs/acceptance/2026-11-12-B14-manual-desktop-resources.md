# B014 manual desktop and resource gates — 2026-11-12

Brick: [B014 — Record the current bar baseline](../bricks/B14-current-bar-baseline.md). Companion reports: [automated](2026-11-12-B14-automated.md), [Windows integration](2026-11-12-B14-windows-integration.md).

Status: open. B014 is `in progress`, not `done`: one gate fails and several are unverified. Every non-pass gate now has a named open owner (below). This baseline does not close any B008, B011, or B013 criterion, and it does not turn a failed first-release gate into a pass. B007 has no acceptance reports in this repository, so no result below is a rerun of a B007 measurement.

## Identity

| Field | Value |
|---|---|
| Source commit | `70b6e0f9113f1c30c2592a00ce748a58ba6b1c44`, clean working tree |
| Published executable | `SystemResourceMonitor.Desktop.exe` `f75a91bfd6612570b0172021808d9b42a1afebb269a3e68139176f41be3beb04`; loaded `SystemResourceMonitor.Desktop.dll` `811fa044…0c5f`, `SystemResourceMonitor.Core.dll` `cd91758a…353d`, `SystemResourceMonitor.AIResources.Core.dll` `9c0c58f2…88d4` (full values in the [automated report](2026-11-12-B14-automated.md#published-artifact)) |
| Test host | `SystemResourceMonitor.Desktop.TestHost.exe` `9d1ea2d8ce858977534c0da91d638e6efecd371391386feef347db80720d3d86`, Release, same commit |
| Windows | Windows 11 Pro 26H2, build 26300.9974, x64 |
| Hardware | Laptop, Intel Core Ultra 7 155H, 22 logical processors, 31.4 GiB RAM; NVIDIA GeForce RTX 4070 Laptop GPU and integrated Intel Arc |
| Monitor and DPI | One built-in 2560 × 1600 px primary panel; runs at 100 %, 125 % (native), 150 %, and 200 % |
| Windows taskbar | Bottom, not auto-hidden, centred icons, 32 DIP high |
| Providers enabled | Codex (`codex-cli 0.159.0`) and Claude Code (`2.1.322`), both installed and signed in |

## Scope of this run

The user approved closing the running bar, Explorer restarts, and scale changes, and changed the scale in Settings when asked. The user chose not to run the 30-minute idle and eight-hour resource runs, and chose to record human-only checks (Task Manager readings, official-client comparison, real video and game fullscreen) as unverified rather than perform them. One monitor was available.

## First-release gates

| Gate | Artifact | Result | Measurements or reason |
|---|---|---|---|
| CPU accuracy | Published app | **unverified** | Task Manager was not observed. Supplementary scripted comparison only: 30 bar readings through UI Automation against the `\Processor Information(_Total)\% Processor Utility` performance counter over the same 60-s window. Normal background load: bar mean 14.37 %, counter 14.03 % (0.34 points). Sustained load from 22 busy processes, run 1: bar 68.57 %, counter 79.89 % (11.32 points). Run 2: bar 43.93 %, utility 30.07 % (13.86 points), `% Processor Time` 43.78 % (0.15 points). The bar follows time-based CPU load; the frequency-scaled utility counter differs by more than the 5-point budget under load on this laptop. |
| RAM accuracy | Published app | **unverified** | Task Manager was not observed. Supplementary: 30/30 pairs within 2 points of `1 − Available Bytes / total` in both regimes (largest difference 0.94 and 0.32 points); bar 86–88 %, tooltip `27.3 / 31.4 GiB used`. |
| App CPU cost | Published app | **unverified** | 30-minute idle run not performed (user decision). |
| App memory ceiling | Published app | **unverified** | 30-minute and eight-hour runs not performed (user decision). |
| Codex polling peak memory | Published app and Codex child | **unverified** | Not measured here. The [B011 report](2026-10-20-B11-manual-desktop-resources.md) records a 175.0-MiB combined peak against the 150-MiB limit; that historical failure is not a B014 measurement and is not waived. |
| Memory growth | Published app | **unverified** | Eight-hour run not performed (user decision). |
| Native-read responsiveness | Test host | pass | `--native-reads 1800`: 900 calls per reader, no failures, no call over 2 s. Longest call: `GetSystemTimes` 1,599.6 ms, `GlobalMemoryStatusEx` 3.7 ms, GPU read 360.0 ms. The run overlapped the scale changes, Explorer restarts, and CPU-load runs. The published app stayed responsive in every desktop run. |
| Freshness and independence | Test host | pass | Expiry 2–4 ms after the six-second deadline for CPU, RAM, and GPU; the other sections kept updating (largest gap 2,005 ms). |
| Provider freshness and independence | Test host | pass | `--claude-independence`: stale Codex label, Codex unavailable 2 ms after its three-minute deadline, CPU/RAM/GPU largest gap 2,002 ms, exit wait 22 ms. Fake-time and process-tree cases pass in the automated suite. |
| Provider limits | Published app | **unverified** | The official clients' own displays were not compared in the same window; the ten-poll non-consumption check was not repeated. Both sections showed active 5-hour and weekly countdowns; Codex showed its unavailable text with `The Codex usage service could not be reached.` on one launch. |
| Normal shutdown | Published app | pass | `WM_CLOSE` five times (exit in 481–850 ms) and tray Exit once: each process exited, and the work area returned to the Windows-only value every time; no Codex or Claude child remained after Exit. The restore-before-wait order is observed only in the test host. Shutdown during an in-flight provider child was not forced. |
| Fault shutdown | Test host | pass | `--fail-after-setpos`, `--bounded-shutdown` (wait 2,020 ms at the 2,000-ms deadline, late result not published), `--faulted-metric-run`. |
| Desktop behaviour | Published app | **fail** | Live scale change from 200 % to 125 % leaves the bar at 48 px with DPI 192 for at least 50 s (Windows integration report). Passed on one monitor: pointer focus, ten scripted borderless-fullscreen cycles with Alt+Tab (lower 30–120 ms, restore 68–103 ms), maximized window, Explorer restart with and without fullscreen, tray position and Exit after restarts, work-area restoration. Unverified: video and exclusive fullscreen, fullscreen on a second monitor, primary-monitor change (one monitor). |
| DPI and rendering | Published app | **fail** | Fresh starts at 100 %, 125 %, 150 %, and 200 %: reservation equals the rendered bar at both edges; CPU/RAM/GPU 0 %, 100 %, and unavailable states fit (test host); active countdowns fit; RAM tooltip shows used/total GiB; no monthly field. The live scale change above fails. Stale provider text was not forced on the published bar. |
| Artifact hygiene | Published app | **unverified** | Passed parts: no test host, fixtures, or development configuration in the folder; runtime loaded from the folder; no elevation. Unverified: this PC has the .NET SDK installed, and running without provider authorization (unavailable sections) was not tried. |

## Observed discrepancies

| # | Observation | Documented contract | Handling |
|---|---|---|---|
| 1 | After a live scale change the bar keeps the old DPI, height, and text size until restarted. | DPI and rendering gate; per-monitor V2 bar that reserves a 24-DIP strip. | Gate fails; owner below. |
| 2 | A primary monitor narrower than about 756 DIP would clip the Claude section; the single row has no width handling. | Shared bar design: no silent clipping; record unsupported widths. | Recorded as unsupported (derived, not observed). B015 measures its own supported widths. |
| 3 | The tray icon starts in the Windows hidden-icons overflow. | — | Recorded only; position and Exit work from the overflow. |
| 4 | CPU readings follow time-based load; the `% Processor Utility` counter differs by more than 5 points under load on this laptop. | CPU accuracy compares with Task Manager. | Gate unverified; owner below. Whether Task Manager shows the utility figure on this build was not checked. |
| 5 | Unqualified-provider sections could not be observed: both providers are qualified and installed. Client absence could not be produced without moving the user's installed clients. | Unqualified providers have no section; qualified unavailable readings use their documented text. | Unverified on the published bar; client-not-found text is covered by the format tests. |

## Prototype comparison

The [HTML prototype](../../prototype/task-strip-design.html) Current view is a visual reference only. Differences from the measured WPF bar:

- Labels: the prototype draws `CPU`, `RAM`, and `GPU` in a muted `#9FACBC` label colour; the WPF bar draws the whole section text in `#E6E6E6`.
- Widths: the prototype uses fixed CSS widths of 64, 68, 65, 300, and 300 px including 8-px side padding; the WPF sections are about 55, 58.4–59, 56, 271, and 274 DIP with an 8-DIP left margin, and the row ends at about 755 DIP instead of about 800 px.
- Values: the prototype's Codex sample is `5h 24% in 02:14 | W 67%`; the WPF bar shows the weekly countdown (`W xx% in Dd Hh`) as ADR 0023 requires.

None of these differences is a WPF requirement.

## Owners of non-pass gates

| Gate | Owner | Status of owner |
|---|---|---|
| Desktop behaviour and DPI and rendering: live scale change | B015 (its DPI and rendering gate must pass before `done`) | existing open brick; assigned by user decision on 2026-11-12 |
| Desktop behaviour: video and exclusive fullscreen, second-monitor fullscreen, primary-monitor change | B015 (its criterion to rerun affected desktop gates) | existing open brick |
| App CPU cost, app memory ceiling, memory growth | B015 (its criterion to rerun affected CPU and memory gates) | existing open brick |
| Codex polling peak memory | B011 | existing open brick (in progress); known failure stays with B011 |
| Provider limits | B011 (Codex) and B013 (Claude) | existing open bricks (in progress) |
| CPU accuracy and RAM accuracy against Task Manager | [B023](../bricks/B23-cpu-accuracy.md) | open brick (planned), created by user decision on 2026-11-12 |
| Artifact hygiene: no separate .NET, no provider authorization | B015 (rerun on its published artifact) | existing open brick |

## B014 acceptance criteria

| Criterion | Evidence | State |
|---|---|---|
| Separate dated reports with source commit, executable and loaded-assembly hashes, test-host identity, Windows build, monitor geometry, DPI, taskbar, logical processors | The three reports' identity sections | met |
| Section bounds, widest values, tooltips, supported widths, clipping at both edges and 100/150/200 %; prototype labelled visual reference | [Windows integration — geometry](2026-11-12-B14-windows-integration.md#measured-bar-geometry); [crops](2026-11-12-B14-bar-crops/); prototype comparison above | met; stale provider text measured by tests, not on the published bar |
| Each applicable gate has an artifact and `pass`, `fail`, or `unverified` | Gate table above | met |
| Fullscreen, Explorer restart, primary-monitor change, pointer focus, tray, work-area restoration observed; unrun scenarios `unverified` | Windows integration report; desktop row above | met; primary-monitor change unverified |
| Providers present and absent, optional GPU, discrepancies recorded | Discrepancies above; GPU section and tooltip in the integration report | present observed; absent unverified |
| Every `fail` or `unverified` gate has a named open owner before `done` | Owner table | met |
| Only sanitized aggregates and redacted crops committed | This change | met |

## Handoff to B015

- Baseline bounds: the section table in the Windows integration report; the row ends at 754.5–755.3 DIP; 24 DIP height at every scale.
- Unsupported widths: primary monitors narrower than about 756 DIP.
- Provider-section behaviour: both qualified sections always present; unqualified-provider omission not observable here.
- Open defect owned by B015: live scale change (discrepancy 1). Visual differences from the prototype as listed.
- B015's own rendering, desktop, CPU, and memory gates still apply; this baseline passes none of them on B015's behalf.
