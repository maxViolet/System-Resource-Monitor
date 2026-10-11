# Plan: System Resource Monitor

Terms are defined in [CONTEXT.md](../CONTEXT.md). Placement is recorded in [ADR 0001](adr/0001-separate-windows-appbar.md); architecture in [ADR 0003](adr/0003-appbar-first-cpu-ram-release.md). Delivery details and measurable gates are in the [implementation plan](implementation-plan.md).

## Product and first release

The system bar is useful independently of subscription limits. The first personal release targets Windows 11 x64 and runs without elevation from a self-contained published folder.

- A thin dark bar spans the primary monitor, at the top or above the Windows taskbar, reserving work area through AppBar.
- Fullscreen on the primary monitor hides the bar and releases its reservation; fullscreen on another monitor does not.
- Show only `CPU 12%` and `RAM 64%` initially, with fixed-width sections and a RAM used/total GiB tooltip.
- Refresh every 2 seconds. At 6 seconds without a successful reading, that reading becomes unavailable (`—`), independently of other readings.
- Tray actions switch top/bottom and exit. Settings failure never prevents monitoring; failed persistence is reported as applying only until restart.
- Exit releases AppBar before waiting for collectors. Waiting for collectors is bounded to 2 seconds.
- No GPU, temperatures, provider sections, installer, or autostart are required for this release.

## 1. Validate placement first

Build the two-project skeleton and empty AppBar before metrics or optional source research. Check start/exit, handled startup failure, repeated edge changes, Explorer restart, resolution/DPI changes, primary-monitor change, auto-hidden taskbar, and fullscreen video, exclusive game, and borderless game on real Windows 11. Verify restored work area and no focus stealing.

Time-box AppBar investigation to two working days. If blocking failures remain, stop dependent implementation and record a decision to use a non-reserving panel or suspend work. Neither fallback is automatic; a placement change requires revisiting ADR 0001.

## 2. Deliver CPU/RAM

Implement CPU and physical RAM measurements and independent freshness handling. Run every first-release gate in the implementation plan, including accuracy, CPU/memory budgets, eight-hour memory observation, and actual desktop behaviour. Exceeding a budget blocks release.

## 3. Add GPU after the first release

Select the discrete adapter, or integrated adapter when no discrete one exists. Correlate its counters reliably; otherwise show unavailable. Compare GPU load with Task Manager on the selected adapter at idle and under load. Record adapter identity, comparison method, sample count, and numeric tolerance before acceptance. GPU is not a prerequisite for CPU/RAM release.

## 4. Add installation and autostart

After GPU work, add install/uninstall scripts and an optional per-user `HKCU` Run entry. Do not elevate the bar. Verify install, sign out/in, disable autostart, exit, and uninstall, including process, files, registry, and work-area state. A blocked GPU investigation does not reopen the already accepted first-release gate.

## Independent research: subscription limits

Research is separate from the first-release critical path. Record dated results for Codex and Claude independently: client version, invocation/local file, response format, window names, reset times, used/remaining rounding, constraints, and sign-out behaviour.

Allowed sources are official client output or locally written non-credential files. Do not read tokens, cookies, credential files, or call undocumented endpoints. Changing this boundary requires a separate ADR.

Each provider passes only when two consecutive non-interactive polls return all windows and reset times, sign-in/network failures are distinguishable, and ten polls without other work leave limits unchanged and create no new sessions. Failed sources are not simulated or included as trusted data.

After a provider passes, show `short%/weekly%`, using the binding weekly window; show all windows and reset times in a tooltip. Poll every minute. Sign-in failure is unavailable on the first failed poll. Network failure may retain explicitly stale data only before three minutes since last success, then `--/--`; expiry must not depend on a subsequent successful poll. Repeat account and non-consuming-poll checks with the built adapter.

## Last extension: temperatures

Research only after the preceding delivery work. Record source, license, driver requirements, privileges, and Defender results. Reject a source requiring elevation of the bar or triggering antivirus warnings; absence of temperatures is acceptable. A privileged helper would require a separate future decision and is not authorized by this plan.

## Release rule

The first personal release requires stages 1 and 2 and all first-release gates in the implementation plan. GPU, installation/autostart, providers, and temperatures cannot block it. Tests alone never prove desktop acceptance. Public distribution is separate: verify a clean Windows 11 profile, no client authorization, no UAC requirement, Defender results, and artifact hygiene before publishing publicly.
