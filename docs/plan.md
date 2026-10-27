# Plan: System Resource Monitor

Terms are defined in [CONTEXT.md](../CONTEXT.md). Placement is recorded in [ADR 0001](adr/0001-separate-windows-appbar.md); architecture in [ADR 0002](adr/0002-appbar-first-cpu-ram-release.md), fullscreen behaviour in [ADR 0003](adr/0003-keep-appbar-registered-during-fullscreen.md), first-release scope in [ADR 0004](adr/0004-subscription-limits-in-first-release.md), tray-menu focus in [ADR 0008](adr/0008-tray-menu-foreground-owner.md), exit on failed tray recovery in [ADR 0009](adr/0009-exit-when-tray-recovery-fails.md), discrete GPU selection and counter correlation in [ADR 0012](adr/0012-discrete-gpu-selected-by-dxcore-and-correlated-by-luid.md) and [ADR 0013](adr/0013-foreign-empty-gpu-engine-instances.md), Codex limit display in [ADR 0015](adr/0015-codex-limit-display-from-qualified-windows.md), Codex client resolution in [ADR 0016](adr/0016-codex-client-resolution.md), Claude limit display in [ADR 0017](adr/0017-claude-limit-display-in-codex-style.md), the shared used-limit display in [ADR 0021](adr/0021-used-limit-display.md), the weekly countdown in [ADR 0023](adr/0023-weekly-limit-countdown-display.md), its client-output source in [ADR 0018](adr/0018-claude-usage-client-output.md), its child image-loading policy in [ADR 0019](adr/0019-claude-child-image-load-policy.md), and the minimum client version in [ADR 0022](adr/0022-claude-code-minimum-version.md). Delivery details and measurable gates are in the [implementation plan](implementation-plan.md); assignable stages and bricks are in the [delivery roadmap](bricks/README.md).

## Product and first release

The first personal release (v1) shows CPU load, RAM usage, and the used subscription limits of each provider whose source qualifies. It targets Windows 11 x64 and runs without elevation from a self-contained published folder.

- A thin dark bar follows the provided mockup and spans the primary monitor, at the top or above the Windows taskbar, reserving work area through AppBar.
- While fullscreen is active on the primary monitor, lower the bar to the bottom of the z-order without unregistering it or releasing its reserved strip. Restore its normal z-order without stealing focus when fullscreen ends or Alt+Tab activates a normal window, even if the game remains running. Fullscreen on another monitor does not change the bar state.
- Show `CPU 12%` and `RAM 64%` with fixed-width sections and a RAM used/total GiB tooltip, followed by a section for each qualified provider (Codex, Claude). Both providers use the used-limit format in [ADR 0021](adr/0021-used-limit-display.md) and the weekly countdown in [ADR 0023](adr/0023-weekly-limit-countdown-display.md), for example `Claude: 5h 24% in 02:37 | W 18% in 01 12`.
- Refresh every 2 seconds. At 6 seconds without a successful reading, that reading becomes unavailable (`—`), independently of other readings; its tooltip explains why.
- Tray actions switch top/bottom and exit. Explorer restart restores the tray icon and both actions. Settings failure never prevents monitoring; failed persistence is reported as applying only until restart. While the tray menu is open, only a hidden menu owner may hold foreground; the bar itself never takes focus.
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

Codex retains the strict source gates recorded in B010. Claude uses the official-client `/usage` output under [ADR 0018](adr/0018-claude-usage-client-output.md) from client version 2.1.304 or later ([ADR 0022](adr/0022-claude-code-minimum-version.md)), with the child image-loading policy in [ADR 0019](adr/0019-claude-child-image-load-policy.md): two polls must return the short and all observed weekly windows, but a 0%-used window may omit its reset; reset text has minute precision with an observed one-minute variation. `auth status --json` distinguishes sign-out. Only a verified explicit client response may classify a network failure; other failures are unavailable as unknown format. Every poll's envelope must show no model or API work. The controlled ten-poll non-consumption observation remains a release check, and the built adapter repeats it. Failed sources are not simulated or included as trusted data.

After a provider passes, show its qualified windows and reset times, using the binding weekly window where applicable. Both providers follow ADR 0021 and ADR 0023: `Codex: 5h 24% in HH:mm | W 67% in DD HH` and `Claude: 5h 24% in HH:mm | W 18% in DD HH`, with the actual used limits. The bar has no monthly field. Codex sources follow ADR 0015 and ADR 0016; Claude sources follow ADR 0017 and [ADR 0018](adr/0018-claude-usage-client-output.md). For either provider, omit the 5-hour countdown at 0% used; above 0%, round the positive countdown up to whole minutes. Round a positive weekly countdown up to whole hours and omit it at 0% used or at or after reset. At 0% used show `W 0%` even without a reset; above 0%, show `W --` when the binding weekly reset is unknown or outside the qualified window. For Claude, `W` is the binding weekly window and the tooltip lists every observed window and reset time at the client's minute precision, or states that an unused window has no reported reset. A qualified Claude monthly window appears only in the tooltip with its used limit. An unavailable snapshot renders `Claude: 5h -- | W --` with a safe tooltip explanation. Poll every minute. Sign-in failure and unknown format are unavailable on the first failed poll. Only a verified network failure may retain explicitly stale data before three minutes since last success; expiry must not depend on a subsequent successful poll.

Accept each built provider adapter only after a manual check on an authorized account: compare every displayed used limit with the official client's used value: Codex `usedPercent` and Claude's displayed integer used share. Compare each reported reset time with the client within the same verification window; for Claude, record minute precision and the absence of a reset at 0% used. Confirm sign-out/loss of access produces the unavailable display and safe error explanation. Repeat the ten-poll non-consuming check with the built adapter. Record client version, account state, observation times, sanitized comparisons without credentials or raw account output, and pass/fail. Parser fixtures and unit tests cannot close this manual gate.

## Independent later slices

The following work may proceed in any order after the first release. None can retroactively block the accepted first release.

### Bundled Codex candidate

After the first release, investigate including a version-pinned official Codex client in the bar's package as an alternative to requiring a separate Codex installation. This is an unscheduled candidate, not an approved packaging change or a first-release requirement. Before choosing it, establish distribution rights, update and security-patch ownership, executable provenance, account sign-in behaviour, and the resource and lifecycle cost of the exact published package. Record the choice in a new ADR and rerun affected provider and first-release gates if bundling is adopted.

### Codex monthly limit

After the first release, qualify a Codex monthly window independently of the accepted 5-hour and weekly contract. Use only official client output or a locally written non-credential file; never infer monthly usage from the weekly bucket, subscription calendar, billing date, or plan tier. Acceptance must establish the exact window meaning, used-value rule, reset time, failure behavior, non-consumption, and compatibility with the existing qualified windows. If every allowed candidate fails, the bar keeps no monthly field.

Only after qualification succeeds, record the source-composition and display decision in a new ADR, then add a monthly field with the qualified used limit and exact reset information. Rerun the affected provider, desktop, publish, shutdown, CPU, and memory gates. This later slice does not retroactively block the accepted first release.

### GPU

Show load only for the discrete adapter, never for an integrated one ([ADR 0012](adr/0012-discrete-gpu-selected-by-dxcore-and-correlated-by-luid.md)). With no discrete adapter or more than one, GPU load is unavailable. Correlate the selected adapter's counters reliably; structurally valid empty-engine instances from foreign LUIDs are ignored, while ambiguous identity or an empty engine on the selected LUID makes the reading unavailable ([ADR 0013](adr/0013-foreign-empty-gpu-engine-instances.md)). Compare GPU load with Task Manager on the selected adapter at idle and under load. Record adapter identity, comparison method, sample count, and numeric tolerance before acceptance.

### Installation and autostart

Add install/uninstall scripts and an optional per-user `HKCU` Run entry independently of GPU work. Do not elevate the bar. Use the canonical path, ACL, reparse-point, quoting, and ownership controls in the implementation plan. Verify install, sign out/in, disable autostart, exit, upgrade, and uninstall, including process, files, registry, settings, and work-area state.

### Temperatures

Research temperature sources after the preceding delivery work. Record source, license, driver requirements, privileges, and Defender results. Reject a source requiring elevation of the bar or triggering antivirus warnings; absence of temperatures is acceptable. A privileged helper requires a separate future decision and is not authorized by this plan.

### Task strip and taskbar replacement

Replace the Windows taskbar in two phases while Explorer remains the shell ([ADR 0008](adr/0008-replace-taskbar-buttons-in-phases.md)); take tasks and tray icons from ManagedShell in Desktop only ([ADR 0009](adr/0009-managedshell-for-tasks-and-tray.md)); draw own volume, network, and battery indicators ([ADR 0010](adr/0010-own-shell-indicators.md)). [ADR 0023](adr/0023-icon-only-task-buttons.md) fixes the task-button presentation for both phases.

#### Shared bar design

This section is the shared design contract for the existing bar and both later phases. The [HTML prototype](../prototype/task-strip-design.html) illustrates it with sample values and icons; its simulated interactions and widths are not Windows 11 or WPF acceptance evidence. The bar remains 24 device-independent pixels (DIP) high, with the existing `#1E1E1E` background, `#E6E6E6` primary text, and 12-DIP Segoe UI text. It spans the primary monitor at the chosen top or bottom edge. System metrics and each qualified limit keep fixed section widths; an unqualified provider has no section, and an unavailable qualified reading uses its documented unavailable display rather than a guessed value.

| Delivery state | Left | Centre | Right | Windows taskbar |
|---|---|---|---|---|
| Before B017 | CPU/RAM and any implemented optional GPU and qualified limit sections | Empty | Empty; the bar's separate native tray icon offers position and exit | Remains available |
| B017, phase A | The same fixed-width sections | Task strip | Clock | Remains visible or auto-hidden as configured |
| B018, phase B | The same fixed-width sections | Task strip | Tray, shell indicators, clock | Hidden only while the replacement tray and its keyboard access work |

In B017 and B018, the task strip has one 32-DIP icon-only task button for each task on every monitor. Use the task's window icon or a neutral window icon if none is available. The full window title is in the tooltip, accessible name, and `»` menu. Keep button order stable on activation, append new tasks at the end, and close gaps without re-sorting. Click activates a task or minimizes it if already active; middle click asks it to close. Use a persistent active marker and a steady, non-animated attention marker, each distinguishable without color alone. When trailing tasks do not fit, move them to `»`; if the active task is there, mark `»` and its menu item as active. Keep the task buttons centred in the space left after the fixed sections and right-hand controls. The clock uses the Windows regional short time format and shows the full date in its tooltip; clicking it does nothing.

The visible bar does not take focus on pointer interaction. A separate focusable surface entered by a keyboard command must let the user select tasks and use `»`; B018 extends that access to the tray, `^`, and shell indicators. Fix the entry command after a conflict and feasibility check in the B017 package plan, before implementation. Measure a phase-specific minimum supported width in the real WPF bar with the widest allowed values and display formats and the crowded task/tray scenarios before implementing layout. Below that measured width, do not silently clip fixed sections or make overflow controls unreachable; revise the design decision before implementation.

- **Phase A:** show the task strip and clock while leaving the Windows taskbar as configured. B017 owns the task and clock acceptance scenarios.
- **Phase B:** first prove that the bar's tray receives every icon of applications already running on Windows 11; otherwise reject B018 and leave the Windows taskbar available. After the gate, show up to six tray icons in Windows order, shell indicators, and the clock, then hide the Windows taskbar only while the tray and its keyboard access work. Under width pressure, move tasks into `»` first and additional visible tray icons into `^` second; fixed left sections, shell indicators, and clock keep their space. Show the Windows taskbar again if either access path fails and restore it on handled exit; record and prove recovery after abrupt termination.

Task grouping, pinned applications, thumbnails, jump lists, taskbar progress and overlay icons, and a calendar are not part of either phase.

## Release rule

The first personal release requires stages 1–3 and all first-release gates in the implementation plan. Both provider qualifications must close as accepted or rejected; each accepted provider must pass its stage 3 acceptance, and its adapter is enabled during every first-release gate. A rejected provider does not block the release. Exceeding a budget blocks release. GPU, installation/autostart, temperatures, and the task strip and taskbar replacement cannot block it. Tests alone never prove desktop acceptance.

Public distribution is prohibited until the clean Windows 11 profile, no-client-authorization, no-UAC, Defender, and artifact-hygiene checks pass and the distribution has authenticated provenance. Publish a signed manifest listing SHA-256 for every payload file and the source commit; publish the verification key or its fingerprint through an independently authenticated channel and verify the signature and every payload hash before release. A checksum posted beside the download without a trusted signature does not meet this gate. Signing is not required for the personal release.
