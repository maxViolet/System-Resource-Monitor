# B015 — Implement the shared bar redesign

| | |
|---|---|
| **Area** | Desktop + acceptance |
| **Depends on** | B014 done |
| **Unblocks** | B019; optionally B018 |
| **Sources** | [B014 current-bar baseline](B14-current-bar-baseline.md), [Product plan — Shared bar design](../plan.md#shared-bar-design), [Implementation plan — Shared bar redesign](../implementation-plan.md#shared-bar-redesign), [First-release acceptance gates](../implementation-plan.md#first-release-acceptance-gates), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

The approved shared design covers the present resource and limit sections as well as the later task and tray phases. The current WPF bar arranges its sections in one left-aligned `StackPanel`; B015.1 replaces that structure with explicit regions for the later phases without changing the first-release measurement or provider contracts; B015.2 then applies the approved mockup's label colour.

## What done looks like

B015 is done when both follow-up tasks are done:

1. [B015.1 - Three-region layout with the current appearance](B15-bar-redesign/B15.1-three-region-layout.md): the WPF layout has fixed left, flexible centre, and right-hand regions while the published bar keeps its current appearance. Its acceptance criteria are the original B015 criteria.
2. [B015.2 - Visual redesign from the approved mockup](B15-bar-redesign/B15.2-visual-redesign.md): the system-metric labels take the approved mockup's secondary colour ([ADR 0029](../adr/0029-secondary-colour-for-metric-labels.md)); the rest of the mockup belongs to B019 and B020.

B019 depends on B015 and therefore on both tasks.

## Acceptance criteria

- [ ] B015.1 is done
- [ ] B015.2 is done

## Out of scope

Task enumeration, task buttons, keyboard task access, overflow, and the clock belong to B019. The in-bar tray, shell indicators, and hiding the Windows taskbar belong to B020. B015 does not change metric collection, provider acquisition, Core/Desktop ownership, section display semantics, or the first-release acceptance thresholds. A change of section text or format, rather than appearance, needs its own ADR under B015.2.
