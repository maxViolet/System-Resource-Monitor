# B008 desktop and resource gates — 2026-10-16

Brick: [B008 — GPU adapter selection, correlation, and display](../bricks/B08-gpu.md). Related evidence: [automated verification](2026-10-16-B08-automated.md) and [Windows integration](2026-10-16-B08-windows-integration.md).

Status: **partial**. The real-AppBar test host passes at 100% scaling, including freshness and shutdown. The published app has not completed the manual and long-duration gates below. No unattended test or build result is presented as a manual observation.

## Real desktop checks completed

The test host registered a real AppBar on Windows 11, outside the sandbox. Its `--metric-display 1` mode passed at 100% scaling. The primary display was 1920×1200; the work area changed from `(0, 0, 1920, 1168)` to `(0, 24, 1920, 1168)` with the bar and returned to its baseline after removal. The bar height was 24 DIP. Fixed section widths were CPU 55 DIP, RAM 59 DIP, and GPU 56 DIP. The mode checked 0%, 100%, and the supported unavailable states: natural text fit inside every fixed section at the observed DPI, and the strip matched the window rectangle. It did not prove visibility or non-activation of a tooltip through a human hover.

Before the review fixes, the full Desktop test project passed 133/133 on ten consecutive runs with real AppBar registration. Those runs include `--metric-freshness cpu`, `ram`, and `gpu`, plus normal and injected-fault shutdown modes. After the fixes, the latest solution run passed 134/134 Desktop tests; a preceding run had one foreground-focus failure in the unchanged fullscreen scenario, and its isolated rerun passed 10/10. The passing runs verify the six-second expiry, independent updating of the other two sections, work-area restoration before the collector wait, and bounded exit for the test-host artifact. These results apply to the test host and build output used by the tests; they do not substitute for a published-app manual run.

## B008 and affected B007 gates still open

| Gate | Required evidence | Current result |
|---|---|
| GPU tooltip | Hover available and each unavailable state; explanation appears without activating the bar. | **Unverified manually.** Formatter and `--metric-display` state tests pass, but no hover/non-activation observation was made. An available state cannot be reached with the current uncorrelated native counters. |
| Published-app GPU display | Actual published artifact shows `GPU —` and the selected-adapter correlation explanation on this PC. | **Unverified on the published artifact.** The native source and test-host paths show `Uncorrelated`; no final published-app visual observation was recorded. |
| CPU and RAM independence | Block/fail each of CPU, RAM, and GPU while the others continue; expiry within 0.5 s of the six-second deadline. | **Pass in automated real-AppBar test-host modes.** Human visual observation was not recorded. |
| App CPU cost | Thirty-minute idle published-app run; whole-machine-normalized process CPU `<= 0.5%` using process CPU-time delta / wall-time delta / 22 logical processors × 100. | **Unverified.** |
| App memory ceiling | Private bytes every two seconds during the 30-minute idle and complete eight-hour runs; every sample `<= 150 MiB`. | **Unverified.** |
| Memory growth | Thirty-minute warm-up, then eight hours of two-second samples; last 30-minute mean minus first 30-minute mean `<= 10 MiB`. | **Unverified.** |
| Native-read responsiveness | Thirty minutes of production CPU, RAM, and GPU reads; no reader has two consecutive calls over two seconds. | **Unverified for 30 minutes.** A ten-second smoke check passed; see the integration report. |
| Published-app shutdown and responsiveness | Normal exit and fault scenarios restore the work area before collector wait; no leftovers. | **Unverified on the published artifact.** Test-host and build-output lifecycle tests pass. |
| DPI and rendering | Published app at 100%, 150%, and 200% scaling; fixed, unclipped sections and matching reservation; hover tooltips. | **Partial.** Test-host 100% programmatic checks pass. Published-app, 150%, 200%, and manual hover checks are unverified. |
| Artifact hygiene | Self-contained folder runs without installed .NET, elevation, or provider authorization; excludes tests, secrets, and diagnostic hooks. | **Partial.** Publication succeeded and 247 file names had no test/fixture pattern. Independent runtime and full content audit are unverified. |

Per the B008 plan, CPU/RAM accuracy, provider limits, settings failures, and earlier desktop placement scenarios are not rerun solely because a third metric section was added. GPU adds a separate polling loop and section; the affected freshness, shutdown, rendering, native responsiveness, CPU-cost, and memory gates above are the B007 reruns required for B008. The prior B007 acceptance reports are absent, so no earlier manual result is silently inherited.

## Decision status

The [integration report](2026-10-16-B08-windows-integration.md) shows why positive GPU correlation is unavailable on this PC. The source safely displays unavailable, but the remaining criterion 4 and 6 checks prevent marking B008 `rejected` yet. A final rejection report would record the completed applicable gates and mark positive correlation and accuracy criteria not applicable. Until then the roadmap keeps B008 `planned`.
