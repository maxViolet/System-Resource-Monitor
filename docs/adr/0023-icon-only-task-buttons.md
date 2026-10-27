# Use icon-only task buttons in the bar

Status: accepted, 2026-10-27. Refines [ADR 0008](0008-replace-taskbar-buttons-in-phases.md) for [B017](../bricks/B17-task-strip.md) and [B018](../bricks/B18-taskbar-replacement.md).

## Decision

Show one 32-DIP-wide icon-only task button for each task on every monitor. Never expand task buttons to show titles. Use the task's window icon when available and a neutral window icon otherwise. The full window title is available in the button tooltip, accessible name, and task overflow menu `»`.

Keep task-button order stable when activation changes. Append a new task at the end; closing a task closes the gap without sorting the others. When task buttons do not fit, move the trailing tasks into `»`. If the active task is there, mark `»` as active and mark that task in its menu. Show a steady, non-animated attention marker while a task reports flashing. Active and attention states must be distinguishable without color alone.

The visible bar remains non-activating on pointer interaction. B017 must also provide a separately focusable keyboard-access surface for selecting tasks and opening `»`; its entry command and conflict check are fixed in the B017 package plan before implementation. B018 extends that keyboard access to its tray, `^`, and shell indicators.

The fixed metric and qualified-limit sections, and the shell indicators and clock when present, keep their space. In B018, move tasks into `»` first, then move additional visible tray icons into `^` when necessary. B018 hides the Windows taskbar only while the tray and its keyboard access work; loss of either restores it. Measure a phase-specific minimum supported width in the real WPF bar, with the widest allowed section values and display formats, before implementing either phase's layout. Do not infer that width from the HTML prototype.

## Reason and alternatives

The user already uses icon-only task buttons and wants the same compact, predictable presentation in the bar. At 24 DIP high, titles would consume substantial horizontal space that can instead keep more tasks visible. Tooltips and the overflow menu provide full titles when icons are ambiguous.

- **Titled buttons that shrink from 160 to 48 DIP, then switch to icons (previous plan):** reveal titles directly but change each button's appearance as task count changes and leave less room for tasks.
- **Group tasks by application:** reduces duplicate icons but changes the one-task-per-button behavior and adds group interaction; it remains outside B017/B018 scope.
- **Always icon-only, one button per task (chosen):** stable size and lower space cost, with the tradeoff that duplicate application icons need a tooltip or `»` to distinguish them.

The keyboard surface adds implementation and acceptance work while preserving the bar's no-focus pointer behavior. Its exact entry command requires a conflict and feasibility check before B017 implementation.
