# Use ManagedShell for tasks and the tray

Status: accepted, 2026-10-15. Applies to [ADR 0008](0008-replace-taskbar-buttons-in-phases.md).

## Decision

Desktop takes a dependency on the [ManagedShell](https://www.nuget.org/packages/ManagedShell) NuGet package (Apache-2.0; version `0.0.374`, published 2026-09-18, compatible with `net10.0-windows`) for window-task enumeration in phase A and for the tray in phase B.

- Only Desktop references ManagedShell. Neither Core project references it, and no ManagedShell type crosses a Core port.
- Use only its task services in phase A and its tray services in phase B. Do not use its AppBar, fullscreen, or shell-replacement features: the AppBar, fullscreen reconciliation, and shell recovery from B02–B04 remain the bar's own implementation. A ManagedShell feature that registers a second AppBar or changes the work area must not be enabled.
- Pin the exact version in `packages.lock.json` and keep `RestoreLockedMode`. The package is pre-1.0, so treat an upgrade as a change that reruns the affected B17 and B18 checks.
- Include the Apache-2.0 license and notice in any published folder that contains ManagedShell.
- That the tray services recover every icon of applications already running when the bar starts is not yet proven on Windows 11. B18 proves it in a feasibility gate before the Windows taskbar is hidden; a failed gate rejects phase B and leaves this dependency used for tasks only.

## Reason and alternatives

Enumerating tasks the way the Windows taskbar does needs window filtering, DWM cloaking checks, UWP frame resolution, icons, and shell-hook events. The phase B tray needs a stand-in `Shell_TrayWnd` that receives `Shell_NotifyIcon` calls and forwards them to Explorer. ManagedShell already implements both and is used by RetroBar and Cairo Desktop on Windows 11.

- **Own Win32 implementation:** no dependency and the same interop style as B02, but about 3–5 days for tasks and about one more week, with risk, for the tray.
- **Own implementation using ManagedShell's source as reference:** no dependency, but the same time as the own implementation, and fixes must be carried over by hand.
- **ManagedShell task and tray services, own AppBar (chosen):** the shortest path to phase B, at the cost of an external pre-1.0 dependency that must not take over the bar's placement.
