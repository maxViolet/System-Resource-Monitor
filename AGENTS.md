# System Resource Monitor

## Documentation

- Documentation is English. Terms are defined in `CONTEXT.md`; use them instead of synonyms.
- Record a design choice with meaningful alternatives as a new ADR in `docs/adr/`. Do not rewrite an existing ADR; supersede it when necessary.

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
  - Links to the plan stage, ADRs, and related pull requests, when they exist.
- Do not add a "Generated with Claude Code" attribution line to the body.
- Before writing a PR body or a commit message, read `docs/pr-and-commit-writing.md`.
