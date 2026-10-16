# Herdr roles

These rules apply only when the agent runs inside a Herdr pane (`HERDR_ENV=1`). Outside Herdr, ignore this file. Inside Herdr, they count as an explicit request to use Herdr.

If you were started as a Herdr role agent (your prompt says you are `reviewer` or `qa`), do not start Herdr panes or agents. Do only the task in your prompt.

## Roles

- `qa` — a plain shell pane for restores, builds, and test runs. Use the `herdr-qa` skill.
- `reviewer` — a Codex agent that reviews a finished change. Use the `herdr-reviewer` skill.

The skills live in the user's global skill directory, not in this repository. If a skill is missing, run the commands in the main shell and skip the review.

## When to use each role

- Run the restore, build, test, and publish commands from the **Validation** section of `AGENTS.md` in the `qa` pane, not in the main shell. Short commands (`git status`, `git diff`, `git diff --check`, reading files) stay in the main shell.
- Start the `reviewer` before a commit or a PR when the change touches `src/` or `tests/`, or when the user asks for a review. Documentation-only changes do not need a reviewer unless the user asks.
- The `reviewer` reviews against `main` unless the branch targets another base.
- Herdr roles do not close Windows desktop or resource gates. Follow the **Validation** section of `AGENTS.md` for those.
