# B014 Windows integration — 2026-11-12

Brick: [B014 — Record the current bar baseline](../bricks/B14-current-bar-baseline.md). Companion reports: [automated](2026-11-12-B14-automated.md), [manual desktop and resources](2026-11-12-B14-manual-desktop-resources.md).

Status: the current bar renders unclipped with a matching reservation at both edges and at 100 %, 125 %, 150 %, and 200 % when started at that scale. A live scale change while the bar runs **fails**: the bar keeps the old scale's geometry (see [Live scale change](#live-scale-change)).

## Identity

| Field | Value |
|---|---|
| Source commit | `70b6e0f9113f1c30c2592a00ce748a58ba6b1c44`, clean working tree |
| Published executable | `SystemResourceMonitor.Desktop.exe` `f75a91bfd6612570b0172021808d9b42a1afebb269a3e68139176f41be3beb04`; loaded production assemblies as listed in the [automated report](2026-11-12-B14-automated.md#published-artifact) |
| Test host | `SystemResourceMonitor.Desktop.TestHost.exe` `9d1ea2d8ce858977534c0da91d638e6efecd371391386feef347db80720d3d86`, Release, same commit, separate folder |
| Windows | Windows 11 Pro 26H2, build 26300.9974, x64 |
| Hardware | Laptop, Intel Core Ultra 7 155H, 22 logical processors, 31.4 GiB RAM; NVIDIA GeForce RTX 4070 Laptop GPU and integrated Intel Arc |
| Monitor | One built-in 2560 × 1600 px panel, primary; native scale 125 %. No second monitor was available |
| Windows taskbar | Bottom, not auto-hidden, centred icons; 32 DIP high (32, 40, 48, and 64 px at 100–200 %) |
| Providers | Codex client `codex-cli 0.159.0` and Claude Code `2.1.322` installed, signed in, and enabled; no account identifiers recorded |
| Settings | Edge `Top` before and after the runs |

Before the runs, a bar built from another worktree was running and holding the session guard. With the user's approval, it was closed through `WM_CLOSE` and restarted after the runs.

## Instrumentation

- A throwaway per-monitor-DPI-aware probe (outside the repository) read the bar's `HWND` rectangle, its monitor's DPI (`GetDpiForWindow`), monitor and work-area rectangles, the extended style (`WS_EX_TOPMOST`, `WS_EX_NOACTIVATE`, `WS_EX_TOOLWINDOW`), and every section's UI Automation bounding rectangle in physical pixels, text, and help text (the WPF tooltip string). Tooltips were read without hovering.
- Tray actions used real input: the probe opened the hidden-icons overflow, right-clicked the bar's tray button, and chose menu items with the keyboard.
- Scale changes were made by the user in Settings > Display. The bar was started fresh after each change unless stated otherwise.
- Redacted crops are in [`2026-11-12-B14-bar-crops/`](2026-11-12-B14-bar-crops/). Codex and Claude sections are filled grey because they show account usage; CPU, RAM, and GPU are unredacted.

## Measured bar geometry

The bar spans the whole monitor width at every scale and is 24 DIP high. "Reserved" means that the work area begins (top edge) or ends (bottom edge) exactly at the bar's outer edge.

| Scale | Monitor in DIP | Top: bar px / work area | Bottom: bar px / work area | Reserved | `topmost`, `noactivate`, `toolwindow` |
|---|---|---|---|---|---|
| 100 % | 2560 × 1600 | `(0,0,2560,24)` / `(0,24,2560,1568)` | `(0,1544,2560,1568)` / `(0,0,2560,1544)` | yes, both | yes |
| 125 % | 2048 × 1280 | `(0,0,2560,30)` / `(0,30,2560,1560)` | `(0,1530,2560,1560)` / `(0,0,2560,1530)` | yes, both | yes |
| 150 % | 1706.7 × 1066.7 | `(0,0,2560,36)` / `(0,36,2560,1552)` | `(0,1516,2560,1552)` / `(0,0,2560,1516)` | yes, both | yes |
| 200 % | 1280 × 800 | `(0,0,2560,48)` / `(0,48,2560,1536)` | `(0,1488,2560,1536)` / `(0,0,2560,1488)` | yes, both | yes |

At the bottom edge the bar sits directly above the Windows taskbar.

### Section bounds

Horizontal device-pixel bounds `left–right (width)` of each section's text element. They are identical at both edges. Each section has an 8-DIP left margin, so the first section starts at 8 DIP.

| Scale | CPU | RAM | GPU | Codex | Claude | Right end of last section |
|---|---|---|---|---|---|---|
| 100 % | 8–63 (55 px, 55.00 DIP) | 71–130 (59 px, 59.00 DIP) | 138–194 (56 px, 56.00 DIP) | 202–473 (271 px, 271.00 DIP) | 481–755 (274 px, 274.00 DIP) | 755.0 DIP |
| 125 % | 10–79 (69 px, 55.20 DIP) | 89–162 (73 px, 58.40 DIP) | 172–242 (70 px, 56.00 DIP) | 252–591 (339 px, 271.20 DIP) | 601–944 (343 px, 274.40 DIP) | 755.2 DIP |
| 150 % | 12–95 (83 px, 55.33 DIP) | 107–195 (88 px, 58.67 DIP) | 207–291 (84 px, 56.00 DIP) | 303–710 (407 px, 271.33 DIP) | 722–1133 (411 px, 274.00 DIP) | 755.3 DIP |
| 200 % | 16–126 (110 px, 55.00 DIP) | 142–259 (117 px, 58.50 DIP) | 275–387 (112 px, 56.00 DIP) | 403–945 (542 px, 271.00 DIP) | 961–1509 (548 px, 274.00 DIP) | 754.5 DIP |

Vertically, each text element is 16 DIP high and centred: top edge `T=4..20`, `5..25`, `6..30`, and `8..40` px at 100–200 %. Section widths differ by at most one device pixel between scales because each width is the widest candidate rounded up to whole device pixels.

### Supported widths

The sections need about 755.3 DIP from the bar's left edge. This monitor offers 1280–2560 DIP at the measured scales, so all sections were visible and unclipped in every run. A primary monitor narrower than 756 DIP (for example 1024 px at 150 %, or 1366 px at 200 %) would clip the Claude section at the bar's right edge, because the single left-aligned row has no overflow handling. That case was not observed on hardware; it is derived from the measured bounds and recorded as **unsupported**, not accepted.

### Widest values and states

`SystemResourceMonitor.Desktop.TestHost --metric-display` composes the production bar with scripted sources and shows CPU, RAM, and GPU at 0 %, 100 %, and every unavailable reason (`NotYetSampled`, `Expired`, `Invalid`, `ReadFailed`, `Uncorrelated`, `NoDiscreteAdapter`, `AmbiguousAdapter`), checking that each section keeps its fixed width, the unconstrained text fits, and the reservation matches the rendered bar. It passed at every scale:

| Scale | Fixed widths CPU / RAM / GPU (DIP) | Widest text natural width (DIP) | Smallest remaining space (DIP) | Result |
|---|---|---|---|---|
| 100 % | 55.00 / 59.00 / 56.00 | `CPU 100%` 54.91, `RAM 100%` 58.21, `GPU 100%` 55.71 | 0.09 / 0.79 / 0.29 | pass |
| 125 % | 55.20 / 58.40 / 56.00 | same | 0.29 / 0.19 / 0.29 | pass |
| 150 % | 55.33 / 58.67 / 56.00 | same | 0.42 / 0.46 / 0.29 | pass |
| 200 % | 55.00 / 58.50 / 56.00 | same | 0.09 / 0.29 / 0.29 | pass |

On the published bar, the provider sections showed active 5-hour and weekly countdowns for both providers at every scale. The widest provider candidates (stale labels, `W 100% in Dd Hh`, unavailable text) are checked by the width tests at 1.0, 1.5, and 2.0 pixels per DIP (automated report); they were not forced on the published bar. `--claude-independence` showed the `Codex (stale):` label and the Codex unavailable text on a real test-host bar with synthetic data.

### Tooltips

Read through UI Automation on the published bar:

- RAM: `RAM <used> / <total> GiB used` with one decimal, for example 27.3 / 31.4 GiB. Present at every scale.
- GPU: the selected discrete adapter's name.
- CPU: no tooltip while a reading is available.
- Codex: the 5-hour and weekly used limit with reset date and time; on one launch, `The Codex usage service could not be reached.` while the bar showed `Codex: 5h -- | W --`.
- Claude: the session and weekly windows with reset times marked `(minute precision)`, plus every other observed weekly window.

## Pointer focus

At every scale the probe clicked inside the CPU section with real mouse input. The foreground window was the same before and after the click, and the bar never became foreground (`WS_EX_NOACTIVATE` set). **Pass.**

## Native tray actions

The bar's tray icon was in the hidden-icons overflow; `Shell_NotifyIconGetRect` returned the overflow chevron. Opening the overflow and right-clicking the icon opened the menu next to the icon, with the bar's process holding foreground while the menu was open (ADR 0008).

| Action | Result |
|---|---|
| Position > Bottom, then Top, at 100 %, 125 %, 150 %, and 200 % | The bar moved and was reserved at the new edge each time. At 125 %, `settings.json` was read after each action and held `Bottom`, then `Top`. The menu closed and foreground returned to the previous window. |
| Menu opened after each Explorer restart | Opened normally after the first two restarts. |
| Exit, after three Explorer restarts | The process exited; the work area returned to `(0,0,2560,1560)`, the Windows-only value. No Codex or Claude child process remained. |

**Pass** for tray position and Exit on one monitor.

## Fullscreen and z-order

The test host's `--fullscreen-reconciliation` mode passed: lowering 99 ms after a controlled fullscreen window became foreground, restoring 252 ms after switching to a normal window, lowering and restoring again when the still-running fullscreen window returned and left, no focus theft, an unchanged work area and bar rectangle, and its maximized-window step.

On the published bar, a borderless window covering the monitor in physical pixels was made foreground, then Alt+Tab switched between it and a maximized normal window ten times:

| Measure | Value |
|---|---|
| Cycles | 10 enter/leave cycles, each with Alt+Tab |
| Lowering after the fullscreen window became foreground | 30–120 ms |
| Restoring topmost after Alt+Tab to the maximized window | 68–103 ms |
| Bar rectangle and work area | Unchanged in all 10 cycles |
| Focus theft | None |
| Ordinary maximized window | Bar stayed topmost |

Video players and games with exclusive fullscreen were not run. Fullscreen on a second monitor was not possible on one monitor.

## Explorer restart

| Scenario | Result |
|---|---|
| `explorer.exe` terminated and restarted with no fullscreen window | After 12 s the bar was reserved at the top with `(0,30,2560,1560)`, topmost, and its tray icon was back and opened its menu. |
| Explorer restarted while a borderless fullscreen window was foreground | Before the restart the bar was lowered (not topmost). 12 s after the restart, the bar was still lowered and the reservation still matched. After the fullscreen window closed, the bar was topmost and reserved again at the first check (2.5 s later, limited by the probe's start time; the cycle runs above show restoration within about 0.1 s). The tray icon opened its menu, and Exit worked. |

**Pass** on one monitor.

## Live scale change

The published bar was started at 200 %, then the user changed the scale to 125 % while it ran:

| Time after the change | Monitor DPI | Bar DPI (`GetDpiForWindow`) | Bar height | Work area |
|---|---|---|---|---|
| about 5 s | 120 | 192 | 48 px (24 DIP at 200 %, 38.4 DIP at 125 %) | `(0,48,2560,1560)` |
| about 20 s | 120 | 192 | 48 px | `(0,48,2560,1560)` |
| about 50 s | 120 | 192 | 48 px | `(0,48,2560,1560)` |

The reservation matched the 48-px window, but the bar kept the 200 % geometry and text size and never adopted the new scale. Closing the bar and starting it again at 125 % gave the expected 30-px bar ([crop](2026-11-12-B14-bar-crops/live-200-to-125-stale.png) of the stale state). The DPI-change part of the desktop and rendering gates **fails**. The opposite direction and display-topology changes were not run.

## Test-host gates

All runs used the test host's own guard and isolated settings, with no other bar on the monitor, at 125 %.

| Mode | Result | Measurements |
|---|---|---|
| `--smoke` | pass | — |
| `--edge-switches` | pass | — |
| `--fullscreen-reconciliation` | pass | lower 99 ms, restore 252 ms |
| `--fail-after-setpos` | pass | work area restored before the wait |
| `--bounded-shutdown` | pass | wait 2,020 ms at the 2,000-ms deadline; pending work recorded; late result not published |
| `--faulted-metric-run` | pass | wait 9 ms |
| `--metric-freshness cpu` / `ram` / `gpu` | pass | expiry 4 / 2 / 3 ms after the six-second deadline; the other sections kept updating with a largest gap of 2,005 ms |
| `--claude-independence` | pass | Codex expiry 2 ms after its deadline; CPU, RAM, and GPU largest gap 2,002 ms; wait 22 ms |
| `--settings-read-failure`, `--settings-backup-failure`, `--settings-write-failure` | pass | — |
| `--shutdown-during-settings-load` | pass | — |
