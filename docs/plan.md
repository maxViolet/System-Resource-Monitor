# Plan: System Resource Monitor

Terms are defined in [CONTEXT.md](../CONTEXT.md). Placement is recorded in [ADR 0001](adr/0001-separate-windows-appbar.md); architecture in [ADR 0002](adr/0002-appbar-first-cpu-ram-release.md), fullscreen behaviour in [ADR 0003](adr/0003-keep-appbar-registered-during-fullscreen.md), and first-release scope in [ADR 0004](adr/0004-subscription-limits-in-first-release.md). Delivery details and measurable gates are in the [implementation plan](implementation-plan.md); assignable stages and bricks are in the [delivery roadmap](bricks/README.md).

## Product and first release

The first personal release (v1) shows CPU load, RAM usage, and the remaining subscription limits of each provider whose source qualifies. It targets Windows 11 x64 and runs without elevation from a self-contained published folder.

- A thin dark bar follows the provided mockup and spans the primary monitor, at the top or above the Windows taskbar, reserving work area through AppBar.
- While fullscreen is active on the primary monitor, lower the bar to the bottom of the z-order without unregistering it or releasing its reserved strip. Restore its normal z-order without stealing focus when fullscreen ends or Alt+Tab activates a normal window, even if the game remains running. Fullscreen on another monitor does not change the bar state.
- Show `CPU 12%` and `RAM 64%` with fixed-width sections and a RAM used/total GiB tooltip, followed by a `short%/weekly%` section for each qualified provider (Codex, Claude).
- Refresh every 2 seconds. At 6 seconds without a successful reading, that reading becomes unavailable (`—`), independently of other readings; its tooltip explains why.
- Tray actions switch top/bottom and exit. Explorer restart restores the tray icon and both actions. Settings failure never prevents monitoring; failed persistence is reported as applying only until restart.
- Exit releases AppBar before waiting for collectors. Waiting for collectors is bounded to 2 seconds.
- No GPU, temperatures, installer, or autostart are required for this release. A provider whose qualification is rejected has no section.

## 1. Validate placement first

Build the two-project skeleton and empty AppBar before metrics or optional source research. Check start/exit, handled startup failure, repeated edge changes, Explorer restart, resolution/DPI changes, primary-monitor change, auto-hidden taskbar, and fullscreen video, exclusive game, and borderless game on real Windows 11. After Explorer restart, verify the AppBar reserves its strip again and the recreated tray icon can change position and exit. Verify restored work area after application exit and no focus stealing. For video, exclusive fullscreen, and borderless fullscreen, run ten enter/leave cycles per mode including Alt+Tab to a normal window and back. After each fullscreen departure, the bar must reappear with its strip still reserved; reservation remains unchanged throughout each cycle. Separately check Explorer restart during fullscreen and fullscreen on a second monitor.

Passing also requires no stale reservations, and changing the primary monitor moves and reserves the bar on the new primary while restoring the previous work area.

Time-box AppBar investigation to two working days. If blocking failures remain, stop dependent implementation and record a decision to use a non-reserving panel or suspend work. Neither fallback is automatic; a placement change requires revisiting ADR 0001.

## 2. Deliver CPU/RAM

Implement CPU and physical RAM measurements and independent freshness handling. Run every first-release gate in the implementation plan, including accuracy, CPU/memory budgets, eight-hour memory observation, DPI/rendering, and actual desktop behaviour, with every accepted provider enabled.

## 3. Deliver subscription limits

Start after stage 1 passes; this work runs in parallel with stage 2.

Research Codex and Claude independently. Record dated results: client version, invocation/local file, response format, window names, reset times, used/remaining rounding, constraints, and sign-out behaviour. A provider is rejected only after every allowed candidate source fails; a rejected provider gets no section and does not block the release.

Allowed sources are official client output or locally written non-credential files. Do not read tokens, cookies, credential files, or call undocumented endpoints. Changing this boundary requires a separate ADR.

Each provider passes only when two consecutive non-interactive polls return all windows and reset times, sign-in/network failures are distinguishable, and ten polls without other work leave limits unchanged and create no new sessions. Failed sources are not simulated or included as trusted data.

After a provider passes, show `short%/weekly%`, using the binding weekly window; show all windows and reset times in a tooltip. On failure, show a safe explanation in that same tooltip; an unavailable snapshot displays `--/--`. Poll every minute. Sign-in failure is unavailable on the first failed poll. Network failure may retain explicitly stale data only before three minutes since last success, then `--/--`; expiry must not depend on a subsequent successful poll.

Accept each built provider adapter only after a manual check on an authorized account: compare every displayed remaining value with the official client's remaining value, or with `100 − used` after applying the client's verified integer-rounding rule; compare every reset time with the client within the same verification window. Confirm sign-out/loss of access produces the unavailable display and safe error explanation. Repeat the ten-poll non-consuming check with the built adapter. Record client version, account state, observation times, sanitized comparisons without credentials or raw account output, and pass/fail. Parser fixtures and unit tests cannot close this manual gate.

## Independent later slices

The following work may proceed in any order after the first release. None can retroactively block the accepted first release.

### GPU

Select the discrete adapter, or integrated adapter when no discrete one exists. Correlate its counters reliably; otherwise show unavailable. Compare GPU load with Task Manager on the selected adapter at idle and under load. Record adapter identity, comparison method, sample count, and numeric tolerance before acceptance.

### Installation and autostart

Add install/uninstall scripts and an optional per-user `HKCU` Run entry independently of GPU work. Do not elevate the bar. Use the canonical path, ACL, reparse-point, quoting, and ownership controls in the implementation plan. Verify install, sign out/in, disable autostart, exit, upgrade, and uninstall, including process, files, registry, settings, and work-area state.

### Temperatures

Research temperature sources after the preceding delivery work. Record source, license, driver requirements, privileges, and Defender results. Reject a source requiring elevation of the bar or triggering antivirus warnings; absence of temperatures is acceptable. A privileged helper requires a separate future decision and is not authorized by this plan.

### Task strip and taskbar replacement

Replace the Windows taskbar in two phases while Explorer remains the shell ([ADR 0008](adr/0008-replace-taskbar-buttons-in-phases.md)); take tasks and tray icons from ManagedShell in Desktop only ([ADR 0009](adr/0009-managedshell-for-tasks-and-tray.md)); draw own volume, network, and battery indicators ([ADR 0010](adr/0010-own-shell-indicators.md)).

- **Phase A:** the bar shows CPU/RAM and limit sections on the left, the task strip in the centre, and the clock on the right. The task strip shows one task button per task on every monitor, with icon, trimmed title, active and flashing states; click activates or minimizes, middle click asks to close. Task buttons with a title are 48–160 DIP wide; when 48 DIP does not fit, they become 32 DIP icon-only buttons, and tasks that still do not fit move to the overflow menu. The clock shows the time in the Windows regional short time format and the full date in its tooltip. The Windows taskbar is not hidden.
- **Phase B:** a Windows 11 feasibility gate first proves that the bar's tray receives every icon of applications already running; otherwise phase B is rejected and the Windows taskbar stays. After the gate, the bar shows the tray (up to 6 icons, the rest in the tray overflow menu), the shell indicators, and the clock, then hides the Windows taskbar. It shows the Windows taskbar again when the tray fails and restores it on every handled exit; after abrupt termination, a recorded recovery restores it. The Windows taskbar is never hidden while the bar's tray does not work.

Task grouping, pinned applications, thumbnails, jump lists, taskbar progress and overlay icons, and a calendar are not part of either phase.

## Release rule

The first personal release requires stages 1–3 and all first-release gates in the implementation plan. Both provider qualifications must close as accepted or rejected; each accepted provider must pass its stage 3 acceptance, and its adapter is enabled during every first-release gate. A rejected provider does not block the release. Exceeding a budget blocks release. GPU, installation/autostart, temperatures, and the task strip and taskbar replacement cannot block it. Tests alone never prove desktop acceptance.

Public distribution is prohibited until the clean Windows 11 profile, no-client-authorization, no-UAC, Defender, and artifact-hygiene checks pass and the distribution has authenticated provenance. Publish a signed manifest listing SHA-256 for every payload file and the source commit; publish the verification key or its fingerprint through an independently authenticated channel and verify the signature and every payload hash before release. A checksum posted beside the download without a trusted signature does not meet this gate. Signing is not required for the personal release.
