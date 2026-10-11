# Writing pull requests and commit messages

The reader of a pull request or a commit has little working memory and acts on what is on screen.
Shape the text so the reader knows what changed and what to do next from the first and last lines.

The title format and the required body sections live in `AGENTS.md` under **Pull requests**. This
document covers how to write the content inside them.

## Pull request body

- **Lead with the outcome.** The first `## Summary` bullet states what now works or what changes
  for the reader, in concrete terms: "The bar now releases its AppBar space on every exit path",
  not "Some improvements to the bar".
- **Rank and group changes.** Group `## Changes` by area, most important first, at most five
  bullets per group. Add another group when more bullets are relevant; keep every relevant change.
- **Restate position.** A branch with `.N` states where it sits, for example "Part 2 of 3: limit
  snapshot parsing", and names what the next part covers.
- **Keep one topic.** Put findings unrelated to the change under a final `## Follow-ups` section,
  one bullet each.
- **State failures plainly.** In `## Validation`, give the failing command, the decisive error
  line, its cause, and the fix or the reason it stays open.
- **End with one review step.** Close the body with one concrete action for the reviewer, for
  example "Review first: `docs/plan.md` stage 1".
- **Start with content.** Each section opens with its first fact and ends with its last one.

## Commit messages

- **Subject.** State the change in imperative mood; a Conventional Commits prefix is allowed.
- **Body.** When present, open with why the change is needed, then list one change per line.
- **Concrete names.** Name the files, functions, or behaviour touched. End the body when the last
  change is listed.
