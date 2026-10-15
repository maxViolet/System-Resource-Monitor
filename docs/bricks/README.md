# Delivery roadmap

The roadmap translates the [product plan](../plan.md) into independently assignable bricks. A brick is a bounded unit of work with an observable result, explicit dependencies, acceptance criteria, and out-of-scope work.

The product plan, implementation plan, and accepted ADRs remain authoritative. If implementation reveals a design choice with meaningful alternatives, record it in a new ADR and update the affected brick; do not silently change the roadmap.

## How to pick up work

1. Choose a brick whose dependencies are complete.
2. Open the brick's package. Read its brief, every linked plan section, every linked ADR, and its package plan when present.
3. Create the branch required by [`AGENTS.md`](../../AGENTS.md); brick IDs are roadmap identifiers, not branch names.
4. Satisfy every acceptance criterion. Automated tests do not replace the named Windows 11 desktop checks.
5. Put automated, Windows integration, and manual desktop evidence in separate dated reports under `docs/acceptance/`.

A brick can complete with a documented rejection only when its acceptance criteria define that outcome. It does not authorize a fallback or simulated data. A conditional implementation brick is `not applicable` when its qualification prerequisite is rejected.

## Stages

Every stage ends in an observable result. Stages 3–4 are independent tracks after the first personal release and may proceed in any order. Stage 5 starts only after B08 and B09 finish. Stage 7 starts after the first personal release; stage 8 starts only after B17 finishes.

| Stage | Observable result |
|---|---|
| 0. Runnable foundation | The solution builds, the architecture boundary is enforced, and the test host is ready to host lifecycle components introduced by later bricks. |
| 1. Placement feasibility | An empty bar reliably reserves the primary monitor's edge, survives shell/display/fullscreen scenarios, and restores work area on exit. |
| 2. First personal release (v1) | A self-contained Windows 11 x64 folder shows trustworthy CPU load, RAM usage, and remaining limits and reset times for each qualified Codex or Claude source, and passes every first-release gate. |
| 3. GPU track | The selected adapter's GPU load is shown only when adapter/counter correlation is proven. |
| 4. Installation track | Per-user install, optional autostart, upgrade, and uninstall work without elevating the bar. |
| 5. Temperature track | A safe source is either accepted and exposed as temperature readings, or rejected with evidence; the bar remains unelevated. |
| 6. Public distribution | A clean-profile package passes the public-release checks and has authenticated payload provenance. |
| 7. Task strip | The bar shows a task button for each task on every monitor and a clock, while the Windows taskbar stays reachable. |
| 8. Taskbar replacement | The bar shows every tray icon and its own volume, network, and battery indicators; the Windows taskbar is hidden while the bar runs and restored on every handled exit, or the feasibility gate rejects the phase. |

B01–B07 and B10–B13 produce the first personal release ([ADR 0004](../adr/0004-subscription-limits-in-first-release.md)): roadmap stages 0–1 implement product-plan stage 1; roadmap stage 2 implements product-plan stages 2 and 3. Provider qualification (B10, B11) starts after B04 and runs in parallel with B05–B06. B07 waits until both qualifications close and every accepted provider's implementation is done; a rejected provider closes its track without blocking B07. Failure of B04 blocks B05–B07 and B10–B13 pending an explicit placement decision. B08, B09, B14, and B15 never retroactively block an accepted B07 release.

## Brick table

| # | Brick | Area | Depends on |
|---|---|---|---|
| **Stage 0 — Runnable foundation** ||||
| B01 | [Solution and enforced Core boundary](B01-solution-boundary/README.md) | Core + Desktop | — |
| **Stage 1 — Placement feasibility** ||||
| B02 | [Empty AppBar lifecycle](B02-empty-appbar-lifecycle/README.md) | Desktop | B01 |
| B03 | [Tray, edge settings, and shell/display recovery](B03-shell-position-recovery/README.md) | Desktop | B02 |
| B04 | [Fullscreen reconciliation and placement gate](B04-placement-gate/README.md) | Desktop + acceptance | B03 |
| **Stage 2 — First personal release (v1)** ||||
| B05 | [Independent metric monitoring](B05-metric-monitoring/README.md) | Core | B04 |
| B06 | [CPU/RAM sources and bar presentation](B06-cpu-ram-presentation/README.md) | Core + Desktop | B05 |
| B10 | [Qualify a Codex limit source](B10-codex-source/README.md) | Research | B04 |
| B11 | [Qualify a Claude limit source](B11-claude-source/README.md) | Research | B04 |
| B12 | [Codex remaining limits](B12-codex-limits/README.md) | Core + Desktop | B06; B10 accepted |
| B13 | [Claude remaining limits](B13-claude-limits/README.md) | Core + Desktop | B06; B11 accepted |
| B07 | [First-release evidence and self-contained publication](B07-first-release/README.md) | Release | B06; B10/B11 accepted/rejected; B12/B13 done/not applicable |
| **Stage 3 — GPU track** ||||
| B08 | [GPU adapter selection, correlation, and display](B08-gpu/README.md) | Core + Desktop | B07 |
| **Stage 4 — Installation track** ||||
| B09 | [Per-user installation and autostart](B09-install-autostart/README.md) | Release | B07 |
| **Stage 5 — Temperature track** ||||
| B14 | [Qualify temperature sources](B14-temperature-source/README.md) | Research | B08 done/rejected; B09 done |
| B15 | [Temperature readings](B15-temperatures/README.md) | Core + Desktop | B14 accepted |
| **Stage 6 — Public distribution** ||||
| B16 | [Authenticated public package](B16-public-distribution/README.md) | Release | B07; every feature included in the package |
| **Stage 7 — Task strip** ||||
| B17 | [Task strip and clock](B17-task-strip/README.md) | Desktop | B07 |
| **Stage 8 — Taskbar replacement** ||||
| B18 | [Own tray, shell indicators, and hidden Windows taskbar](B18-taskbar-replacement/README.md) | Desktop + acceptance | B17 |

`Accepted` and `rejected` are explicit outcomes defined by a brick's acceptance criteria. A rejected B10, B11, B14, or B18 closes that track; B12, B13, or B15 respectively becomes `not applicable`.

## Dependency graph

```mermaid
graph LR
  B01[B01 foundation] --> B02[B02 empty AppBar]
  B02 --> B03[B03 shell and position]
  B03 --> B04[B04 placement gate]
  B04 --> B05[B05 monitoring]
  B05 --> B06[B06 CPU and RAM]
  B04 --> B10[B10 Codex source]
  B04 --> B11[B11 Claude source]
  B10 -->|accepted| B12[B12 Codex limits]
  B11 -->|accepted| B13[B13 Claude limits]
  B06 --> B12
  B06 --> B13
  B06 --> B07[B07 first release]
  B10 -->|closed| B07
  B11 -->|closed| B07
  B12 -. if built .-> B07
  B13 -. if built .-> B07

  B07 --> B08[B08 GPU]
  B07 --> B09[B09 install]
  B08 --> B14[B14 temperature source]
  B09 --> B14
  B14 -->|accepted| B15[B15 temperatures]

  B07 --> B16[B16 public package]
  B08 -. if included .-> B16
  B09 -. if included .-> B16
  B15 -. if included .-> B16

  B07 --> B17[B17 task strip]
  B17 --> B18[B18 taskbar replacement]
  B17 -. if included .-> B16
  B18 -. if included .-> B16
```

## Progress

Update this table when evidence changes a brick's state. Valid states are `planned`, `in progress`, `blocked`, `accepted`, `rejected`, `not applicable`, and `done`. `Rejected` requires an explicit rejection path in the brick; `not applicable` requires rejection of that brick's qualification prerequisite.

| Brick | Status |
|---|---|
| B01 | in progress |
| B02–B18 | planned |

## Brick template

```markdown
# BNN — Title

| | |
|---|---|
| **Area** | Core / Desktop / Release / Research |
| **Depends on** | BNN — or “—” |
| **Unblocks** | BNN — or “—” |
| **Sources** | links to exact plan sections and ADRs |

## Why

The problem this brick closes and why it is separate.

## What done looks like

An observable result rather than an implementation layer.

## Context

Required behavior, invariants, evidence, and named outputs not already owned by a linked source.

## Acceptance criteria

- [ ] Checkable outcomes, each proven by a command, automated check, or named manual scenario

## Out of scope

Explicit neighbouring work, with its owning brick where applicable.
```
