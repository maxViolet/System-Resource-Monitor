# System Resource Monitor

## Scope and architecture

- The bar has two production projects. `SystemResourceMonitor.Core` targets plain `net10.0` and owns the model and workflows; `SystemResourceMonitor.Desktop` targets `net10.0-windows` and owns WPF and Win32 interop. Desktop references Core; Core never references Desktop, WPF, Windows Forms, or the APIs banned in its `BannedSymbols.txt`. Do not change this boundary without an ADR.
- Subscription limits come only from official client output or locally written non-credential files. Do not read tokens, cookies, or credential files, or call undocumented endpoints, without an ADR.
- For a task tied to a brick, read its package in full (the brief and the package plan when present) and every plan section and ADR it links before changing files. Treat its acceptance criteria and **Out of scope** as the implementation boundary.

## Documentation

- Documentation is English. Terms are defined in `CONTEXT.md`; use them instead of synonyms.
- Keep shared knowledge in `docs/plan.md`, `docs/implementation-plan.md`, and `docs/adr/`; bricks link to exact sections instead of duplicating them.
- Record a design choice with meaningful alternatives as a new ADR in `docs/adr/`. Do not rewrite an existing ADR; supersede it when necessary and update the plan and the affected bricks.
- Before changing project documentation — the plan, the implementation plan, ADRs, bricks, `CONTEXT.md`, or this file — present the complete plan and exact file-change list, then wait for explicit approval.
- For an approved change of documented behaviour, align the plan, the implementation plan, the affected bricks, ADR references, and `CONTEXT.md`. Search the repository for stale terms, states, and thresholds.

## Git

- Preserve the requested branch name. Before a commit, push, merge, or PR action, inspect the current branch, working-tree status, and worktree list.
- Do not commit, push, merge, or create a PR unless the user explicitly asks.
- Use one worktree and branch per terminal or agent.

## Branch names

- `<TYPE>-<NNN>-<short-kebab-name>`, for example `DOC-002-pr-and-commit-writing`. Types:
  - `DOC` — documentation only: docs, ADRs, the plan, the glossary.
  - `FIX` — corrects existing code behaviour.
  - `REFACTOR` — changes code structure without changing intended behaviour.
  - `TEST` — adds or changes tests only.
  - `FEAT` — new behaviour.
- `NNN` is three digits: the highest existing number of the same type plus one, found in `git branch -a` and in the titles of `gh pr list --state all`. The first branch of a type is `001`. A follow-up part of the same task adds `.N`, for example `DOC-005.1-…`.
- Existing branches that predate these rules keep their names; their pull requests still take a work ID in the title.

## Pull requests

- Title: the branch's work ID, a colon, a space, and a short summary — `<TYPE>-<NNN>: <summary>` (with `.N` when the branch has it). Examples: `DOC-001: plan the Windows resource bar and its feasibility gates`, `FEAT-003: register the bar as an AppBar`.
- The summary is English, imperative, lower-case after the colon, and without a trailing period. Do not use a Conventional Commits prefix (`docs:`, `feat:`, `fix:`) in the title; commit messages may keep it.
- Body sections, in order:
  - `## Summary` — why the change is needed, in one to three bullets.
  - `## Changes` — the files or areas changed and what changed in each.
  - `## Validation` — the commands run and their results; mark anything not run as unverified.
  - Links to the brick, the plan stage, ADRs, and related pull requests, when they exist.
- Do not add a "Generated with Claude Code" attribution line to the body.
- Before writing a PR body or a commit message, read `docs/pr-and-commit-writing.md`.

## Validation

- Run targeted tests first: `dotnet test --project <test project> -c Release`.
- Before handoff, run the commands from ADR 0005 when applicable: `dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode`, `dotnet build SystemResourceMonitor.slnx -c Release --no-restore`, `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build`, and `git diff --check`. Add `dotnet publish src/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true` when the change affects the published app.
- Automated checks do not close Windows desktop or resource gates. Run the scenarios a brick names on Windows 11 and record the evidence under `docs/acceptance/`.
- Report unavailable or blocked validation as unverified; never report it as passed.
