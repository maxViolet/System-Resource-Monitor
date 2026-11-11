# Reserve B014 for the current-bar baseline and renumber later bricks

Status: accepted, 2026-11-11. This ADR records the user's requested roadmap ID change and supersedes only the brick-number references in [ADR 0008 — phases](0008-replace-taskbar-buttons-in-phases.md), [ADR 0009 — ManagedShell](0009-managedshell-for-tasks-and-tray.md), [ADR 0014 — installation](0014-per-user-installation-and-autostart.md), [ADR 0021 — used limits](0021-used-limit-display.md), and [ADR 0023 — icon-only tasks](0023-icon-only-task-buttons.md). Their substantive decisions remain in force.

## Decision

B014 records the measured current-bar baseline before the shared layout change. The former B014–B021 assignments move one number forward:

| Former ID | Current ID | Assignment |
|---|---|---|
| B014 | B015 | Shared bar redesign |
| B015 | B016 | Temperature-source qualification |
| B016 | B017 | Temperature readings |
| B017 | B018 | Authenticated public package |
| B018 | B019 | Task strip and clock |
| B019 | B020 | Own tray, shell indicators, and hidden Windows taskbar |
| B020 | B021 | Codex monthly-source qualification |
| B021 | B022 | Codex monthly limit |

Use this table for references written after [PR #54](https://github.com/maxViolet/System-Resource-Monitor/pull/54) and before this renumbering. Earlier documents used this prior assignment:

| Before PR #54 | Current ID | Assignment |
|---|---|---|
| B014 | B016 | Temperature-source qualification |
| B015 | B017 | Temperature readings |
| B016 | B018 | Authenticated public package |
| B017 | B019 | Task strip and clock |
| B018 | B020 | Taskbar replacement |
| B019 | B021 | Codex monthly-source qualification |
| B020 | B022 | Codex monthly limit |

Historical documents and pull-request titles retain the IDs they used when written. The current [roadmap](../bricks/README.md) and brick packages give the live assignments. Historical path stubs lead to the moved briefs and package files.

## Reason and alternatives

The current bar needs a separately assignable measured baseline before its WPF layout is changed. Keeping the former numbering and adding a B014.1 side task would avoid reference churn, but the user requested B014 as the baseline brick and a one-step shift of the current bricks. The shift preserves those bricks' behavior and dependencies while making B014 the prerequisite for B015.

The cost is a second historical ID translation after the earlier B014 temperature-source assignment moved to B015 in [PR #54](https://github.com/maxViolet/System-Resource-Monitor/pull/54). Keep both changes visible in the roadmap history; do not reinterpret historical acceptance reports as evidence for the new IDs.
