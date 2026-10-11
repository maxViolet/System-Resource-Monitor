# Plan: System Resource Monitor

Terms are defined in [CONTEXT.md](../CONTEXT.md). Placement is recorded in [ADR 0001](adr/0001-separate-windows-appbar.md); architecture in [ADR 0002](adr/0002-appbar-first-cpu-ram-release.md). Delivery details and measurable gates are in the [implementation plan](implementation-plan.md).

## Product and first release

The system bar is useful independently of subscription limits. The first personal release targets Windows 11 x64 and runs without elevation from a self-contained published folder.

- A thin dark bar follows the provided mockup and spans the primary monitor, at the top or above the Windows taskbar, reserving work area through AppBar.
- Fullscreen on the primary monitor hides the bar and releases its reservation; the bar returns and reserves its strip after fullscreen exits. Fullscreen on another monitor does not hide it.
- Show only `CPU 12%` and `RAM 64%` initially, with fixed-width sections and a RAM used/total GiB tooltip.
- Refresh every 2 seconds. At 6 seconds without a successful reading, that reading becomes unavailable (`—`), independently of other readings; its tooltip explains why.
- Tray actions switch top/bottom and exit. Settings failure never prevents monitoring; failed persistence is reported as applying only until restart.
- Exit releases AppBar before waiting for collectors. Waiting for collectors is bounded to 2 seconds.
- No GPU, temperatures, provider sections, installer, or autostart are required for this release.

## 1. Validate placement first

Build the two-project skeleton and empty AppBar before metrics or optional source research. Check start/exit, handled startup failure, repeated edge changes, Explorer restart, resolution/DPI changes, primary-monitor change, auto-hidden taskbar, and fullscreen video, exclusive game, and borderless game on real Windows 11.

Passing requires no focus stealing or stale reservations; the bar and tray recover after Explorer restart; changing the primary monitor moves and reserves the bar on the new primary while restoring the previous work area; every fullscreen scenario restores the bar and reservation after exit.

Time-box AppBar investigation to two working days. If blocking failures remain, stop dependent implementation and record a decision to use a non-reserving panel or suspend work. Neither fallback is automatic; a placement change requires revisiting ADR 0001.

## 2. Deliver CPU/RAM

Implement CPU and physical RAM measurements and independent freshness handling. Run every first-release gate in the implementation plan, including accuracy, CPU/memory budgets, eight-hour memory observation, DPI/rendering, and actual desktop behaviour. Exceeding a budget blocks release.

## Independent later slices

The following work may proceed in any order after the first release. None can retroactively block the accepted CPU/RAM release.

### GPU

Select the discrete adapter, or integrated adapter when no discrete one exists. Correlate its counters reliably; otherwise show unavailable. Compare GPU load with Task Manager on the selected adapter at idle and under load. Record adapter identity, comparison method, sample count, and numeric tolerance before acceptance.

### Installation and autostart

Add install/uninstall scripts and an optional per-user `HKCU` Run entry independently of GPU work. Do not elevate the bar. Use the canonical path, ACL, reparse-point, quoting, and ownership controls in the implementation plan. Verify install, sign out/in, disable autostart, exit, upgrade, and uninstall, including process, files, registry, settings, and work-area state.

### Subscription limits

Research Codex and Claude independently. Record dated results: client version, invocation/local file, response format, window names, reset times, used/remaining rounding, constraints, and sign-out behaviour.

Allowed sources are official client output or locally written non-credential files. Do not read tokens, cookies, credential files, or call undocumented endpoints. Changing this boundary requires a separate ADR.

Each provider passes only when two consecutive non-interactive polls return all windows and reset times, sign-in/network failures are distinguishable, and ten polls without other work leave limits unchanged and create no new sessions. Failed sources are not simulated or included as trusted data.

After a provider passes, show `short%/weekly%`, using the binding weekly window. The tooltip shows all windows and reset times; on error, the same tooltip explains the failure. Poll every minute. Sign-in failure is unavailable on the first failed poll. Network failure may retain explicitly stale data only before three minutes since last success, then `--/--`; expiry must not depend on a subsequent poll.

Acceptance compares every displayed remaining value and reset time with the official client in the same verification window. If the client reports used percentage, compare with `100 − used` using the client's verified rounding. After access loss, verify the error state and explanation. This manual account check is separate from parser tests. Repeat the ten-poll non-consumption check with the built adapter.

### Temperatures

Research temperature sources after the preceding delivery work. Record source, license, driver requirements, privileges, and Defender results. Reject a source requiring elevation of the bar or triggering antivirus warnings; absence of temperatures is acceptable. A privileged helper requires a separate future decision and is not authorized by this plan.

## Release rule

The first personal release requires stages 1 and 2 and all first-release gates in the implementation plan. Tests alone never prove desktop acceptance.

Public distribution is separate and prohibited until the implementation plan's authenticated-provenance gate passes. Also verify a clean Windows 11 profile, no client authorization, no UAC requirement, Defender results, and artifact hygiene before publishing publicly.
