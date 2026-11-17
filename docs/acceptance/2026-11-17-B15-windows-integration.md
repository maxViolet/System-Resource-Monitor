# B015 Windows integration — 2026-11-17

Brick: [B015 — Redesign the bar](../bricks/B15-bar-redesign.md). Companion reports: [automated](2026-11-17-B15-automated.md), [manual desktop and resources](2026-11-17-B15-manual-desktop-resources.md).

Status: the redesigned published bar keeps the B014 geometry pixel for pixel at 100 %, 125 %, 150 %, and 200 % and at both edges, with a matching reservation. A live scale change in both directions now **passes**; it failed in B014.

## Identity

| Field | Value |
|---|---|
| Final artifact | Source commit `2e7efb6d7321584f757040eb642f2902508947b1`; `SystemResourceMonitor.Desktop.exe` `cde70736dc1e49028075fdc73d82349333e64468b85be22048faf43716ae0e6e`; loaded assemblies in the [automated report](2026-11-17-B15-automated.md#published-artifact) |
| Earlier artifact | Source commit `c13b520` (merge of PR #63), `SystemResourceMonitor.Desktop.exe` `8eaf606957f6da0cec9a16dac4f4d88b76d3ea0e5d6b5f7a4a0b44b496912ada`, `SystemResourceMonitor.Desktop.dll` `e6065459a6d91c48ea960a23bfc6c85c394fdafd19be75612327dadf09105c3b`. Used only for the Settings-driven scale change below. The later commits change only fullscreen classification (PR #65) |
| Windows | Windows 11 Pro 26H2, build 26300.9974, x64 |
| Hardware | Laptop, Intel Core Ultra 7 155H, 22 logical processors, 31.4 GiB RAM; NVIDIA GeForce RTX 4070 Laptop GPU and integrated Intel Arc |
| Monitor | One built-in 2560 × 1600 px panel, primary; native scale 125 %. No second monitor was available |
| Windows taskbar | Bottom, not auto-hidden; 32, 40, 48, and 64 px at 100–200 % |
| Providers | Codex client `codex-cli 0.159.0` and Claude Code `2.1.326` installed, signed in, and enabled; no account identifiers recorded |
| Settings | Edge `Top` before and after the runs |

## Instrumentation

- Throwaway per-monitor-DPI-aware PowerShell probes outside the repository started the published executable, read the bar's `HWND` rectangle, `GetDpiForWindow`, the extended style, the work area before, during, and after each run, and every section's UI Automation bounding rectangle and help text. They closed the bar with `WM_CLOSE` and checked the exit code.
- For fresh starts and scripted live changes, the probes changed the scale with the undocumented `SystemParametersInfo(SPI_SETLOGICALDPIOVERRIDE)`, relative to the panel's recommended 150 %, and wrote `settings.json` to select the edge before each start. Settings were restored afterwards.
- A watcher logged the bar's rectangle, DPI, work area, and `WS_EX_TOPMOST` every 0.5 s during the manual session.
- Redacted crops of the final artifact are in [`2026-11-17-B15-bar-crops/`](2026-11-17-B15-bar-crops/). The Codex and Claude sections are filled grey because they show account usage; CPU, RAM, and GPU are unredacted. Each crop ends 16 DIP after the last section, so the empty centre and right regions are only partly shown.
- A full `dotnet test` run from another worktree started during the first scripted live runs. Its test host registered a second AppBar at the top and doubled the reserved height. Those runs were discarded and repeated after that process exited.

## Measured bar geometry

Fresh start of the final artifact at each scale and edge, after 20 s. In all eight runs: `WS_EX_TOPMOST` and `WS_EX_NOACTIVATE` set, `WS_EX_TOOLWINDOW` absent, the work area during the run equals the Windows-only work area minus the bar's height at its edge, the work area after exit equals the one before start, and the exit code is 0.

| Scale | DPI | Top window | Bottom window | Height |
|---|---|---|---|---|
| 100 % | 96 | `(0, 0, 2560, 24)` | `(0, 1544, 2560, 1568)` | 24 px = 24 DIP |
| 125 % | 120 | `(0, 0, 2560, 30)` | `(0, 1530, 2560, 1560)` | 30 px = 24 DIP |
| 150 % | 144 | `(0, 0, 2560, 36)` | `(0, 1516, 2560, 1552)` | 36 px = 24 DIP |
| 200 % | 192 | `(0, 0, 2560, 48)` | `(0, 1488, 2560, 1536)` | 48 px = 24 DIP |

### Section bounds

Horizontal device-pixel bounds `left–right (width)` of each section's text element, identical at both edges:

| Scale | CPU | RAM | GPU | Codex | Claude |
|---|---|---|---|---|---|
| 100 % | 8–63 (55) | 71–130 (59) | 138–194 (56) | 202–473 (271) | 481–755 (274) |
| 125 % | 10–79 (69) | 89–162 (73) | 172–242 (70) | 252–591 (339) | 601–944 (343) |
| 150 % | 12–95 (83) | 107–195 (88) | 207–291 (84) | 303–710 (407) | 722–1133 (411) |
| 200 % | 16–126 (110) | 142–259 (117) | 275–387 (112) | 403–945 (542) | 961–1509 (548) |

Every value equals the [B014 section bounds](2026-11-12-B14-windows-integration.md#section-bounds). The redesign keeps the current visual state. The same eight runs on `c13b520` gave the same values.

### Supported widths

The sections still end at about 755 DIP from the bar's left edge, as in [B014](2026-11-12-B14-windows-integration.md#supported-widths). This monitor offers 1280–2560 DIP at the measured scales, so every section was visible and unclipped. A primary monitor narrower than about 756 DIP is unsupported; no such monitor was available, so the clipping or omission of the right-most sections on it is unverified.

## Tooltips

Read through UI Automation on the final artifact:

- RAM: `RAM <used> / <total> GiB used` with one decimal (`20.2 / 31.4 GiB used`).
- GPU: the selected discrete adapter's name.
- CPU: no tooltip while a reading is available, as [specified](../plan.md) and as in B014.
- Codex and Claude: present; content not recorded.

The user hovered RAM, GPU, Codex, and Claude in the published app and saw their tooltips.

## Live scale change

### Scripted, final artifact

| Start | Change | Bar after the change | Work area after the change |
|---|---|---|---|
| 125 %, Top | 125 % → 200 % | DPI 192, `(0, 0, 2560, 48)`, sections as the 200 % fresh start | `(0, 48, 2560, 1536)` |
| 125 %, Top | 200 % → 125 % | DPI 120, `(0, 0, 2560, 30)`, sections as the 125 % fresh start | `(0, 30, 2560, 1560)` |
| 200 %, Bottom | 200 % → 125 % | DPI 120, `(0, 1530, 2560, 1560)`, sections as the 125 % fresh start | `(0, 0, 2560, 1530)` |

Each run exited with code 0. After exit at 125 %, the work area was `(0, 0, 2560, 1560)`, the Windows-only value.

### Through Settings, earlier artifact

The user changed Settings > Display > Scale 125 % → 200 % → 125 % twice while the `c13b520` bar ran at the top edge. The watcher logged the same sequence each time: the monitor reached 200 %, then within about 1 s the bar's DPI went 120 → 192 and its window 30 → 48 px, and the work area moved to `(0, 48, 2560, 1536)`. On the way back the bar returned to DPI 120 and 30 px with the work area at `(0, 30, 2560, 1560)`. The user saw the height and text follow without a gap.

After the changes, a maximized Windows Terminal window kept a height that matched the 200 % taskbar. The work area itself was correct. The same check without the bar was not run, so this observation is not attributed to the bar.

### Test host

`--dpi-watch 90` with a Settings-driven change (PR #63 review) and with scripted changes passed: HWND, monitor, WPF, and AppBar thickness agreed at DPI 120 (30 px) and 192 (48 px), with the reservation and work area bordering each other at each settled DPI. `--dpi-compare 90` reproduced [ADR 0027](../adr/0027-bar-window-without-tool-window-style.md): an ordinary and a `WS_EX_NOACTIVATE` window followed 120 → 192 → 120, and a `WS_EX_TOOLWINDOW` window stayed at 120.

## Regions in the published app

The centre and right regions are empty in the published app; no placeholder appears in any crop or UI Automation tree (five text elements, all in the left region). The placeholder checks run only in the test host (`--bar-regions`, [automated report](2026-11-17-B15-automated.md#what-the-suite-covers-for-b015)).
