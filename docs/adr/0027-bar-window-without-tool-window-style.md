# Create the bar window without WS_EX_TOOLWINDOW

Status: accepted, 2026-11-15. Amends the window-style line of the [B002 package plan](../bricks/B02-empty-appbar-lifecycle/plan.md); no earlier ADR is superseded.

## Decision

Create the bar window with `WS_EX_NOACTIVATE` and WPF `ShowInTaskbar=false`, without `WS_EX_TOOLWINDOW`. `ShowInTaskbar=false` gives the window a hidden owner, which keeps the bar out of the taskbar and out of Alt+Tab. The bar still never activates: `WS_EX_NOACTIVATE`, `ShowActivated=false`, the `WM_MOUSEACTIVATE` handler, and `SWP_NOACTIVATE` placement remain.

## Reason

On Windows 11, a per-monitor-V2 window with `WS_EX_TOOLWINDOW` keeps its start DPI when the display scale changes while it runs: `GetDpiForWindow` does not change and the window receives no `WM_DPICHANGED`. The bar then keeps its old height and text size, and its AppBar thickness, computed from the window DPI, stays wrong until a restart. The [B014 Windows integration report](../acceptance/2026-11-12-B14-windows-integration.md#live-scale-change) recorded this failure for a 200%-to-125% change.

Evidence from the B015 test host (`--dpi-watch`) on 2026-11-15, one 2560 × 1600 monitor, scale changed live between 125% and 200%:

- An ordinary window and a window with only `WS_EX_NOACTIVATE` followed the monitor DPI (120 → 192 → 120).
- A window with only `WS_EX_TOOLWINDOW`, and the bar with both styles, stayed at DPI 120.
- Removing and re-adding `WS_EX_TOOLWINDOW` after the change did not update the DPI.
- The bar without `WS_EX_TOOLWINDOW` followed both directions: 30 → 48 → 30 px from a 125% start and 48 → 30 px from a 200% start, with the reservation and work area matching.
- The user confirmed on the same day that the bar without `WS_EX_TOOLWINDOW` appears neither in Alt+Tab nor on the taskbar.

## Alternatives

- **Recreate the HWND when its DPI differs from the monitor DPI.** This keeps the tool-window style but requires `ABM_REMOVE` and a new registration, causes a visible flicker, and adds races with the tray icon and fullscreen reconciliation.
- **Scale the content manually.** A `LayoutTransform` and a thickness derived from the monitor DPI would hide the symptom, but the window would keep a wrong DPI for Windows, WPF rendering, and every later DPI-dependent call.

## Consequences

The test host asserts that the bar has `WS_EX_NOACTIVATE` and lacks `WS_EX_TOOLWINDOW`. A live scale change in both directions remains a Windows 11 acceptance scenario for B015; automated checks do not replace it. Other windows of the app, such as the tray menu owner, may keep `WS_EX_TOOLWINDOW` because they do not render DPI-dependent content.
