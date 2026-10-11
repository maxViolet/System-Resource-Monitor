# .NET WPF desktop application with clean architecture boundaries

Status: accepted, 2026-10-11.

## Context

The MVP is Windows 11-only, needs a native HWND for the AppBar API, runs continuously with low UI complexity, and integrates with Win32 metrics, processes, the registry or Task Scheduler, and monitor notifications. Subscription and hardware sources remain uncertain, so their adapters must be replaceable without coupling them to the window.

## Decision

Build an x64 WPF application on .NET 10. Use four production projects:

- `Domain` owns metric and limit values and their invariants;
- `Application` owns polling, freshness, dashboard state, and ports for external sources;
- `Infrastructure` implements Windows APIs, provider clients, settings, and autostart;
- `Desktop` owns WPF, AppBar lifecycle, tray interaction, and the composition root.

Dependencies point inward. `Domain` has no project dependencies; `Application` depends on `Domain`; `Infrastructure` depends on `Application` and `Domain`; `Desktop` composes all projects but keeps concrete Infrastructure use in startup. The application is published as a self-contained `win-x64` folder for the MVP.

## Consequences

WPF supplies a mature HWND and direct Win32 message interop without adding a Windows App SDK deployment dependency. Windows-specific code stays in outer layers, while source parsing and freshness policy can be tested without a window or native hardware. Four projects add some structure, but avoid a project per feature and avoid speculative repositories, mediators, or mapping layers.

The product is intentionally Windows-only. WPF styling is sufficient for the thin bar but is less modern than WinUI. The Desktop project references Infrastructure at the composition root; that is the only permitted inward-to-outer wiring point.

## Rejected alternatives

- **WinUI 3:** modern controls are not valuable for this small bar, while Windows App SDK deployment and HWND/AppBar integration add risk.
- **Native C++/Win32:** gives maximum control but increases implementation and memory-safety cost for settings, provider processes, and presentation without an MVP benefit.
- **Avalonia:** cross-platform rendering is unnecessary for a Windows-only AppBar and still requires Windows-specific interop.
- **One project:** initially smaller, but it would mix WPF, Win32, provider parsing, polling policy, and domain state exactly where replaceable adapters and deterministic tests are needed.
