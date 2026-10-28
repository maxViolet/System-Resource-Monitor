# Claude Code 2.1.311 `/usage` local insights observation — 2026-10-27

Brick: [B012](../../bricks/B12-claude-source.md). Decision: [ADR 0025](../../adr/0025-claude-local-usage-insights.md). The earlier [2.1.304](claude-2026-10-21.md) and [2.1.306](claude-2.1.306-2026-10-21.md) reports remain historical source observations.

## Source observation

- The native `%USERPROFILE%\.local\bin\claude.exe` reports file version `2.1.311.0`. The running published bar showed `Claude: 5h -- | W --` with the safe tooltip `Claude answered in an unsupported format.`
- Repeated non-interactive calls with the fixed `--safe-mode --tools "" --no-session-persistence --print --output-format json "/usage"` arguments returned exit code zero. Each JSON envelope reported `local_command: usage`, `is_error: false`, `num_turns: 0`, `duration_api_ms: 0`, and `total_cost_usd: 0`. No credential or transcript content was read.
- The result retained `Current session` and `Current week (all models)` with integer used shares and reset text. After those rows, it added an attribution heading and `Approximate · sessions on this machine only · shares overlap`, followed by `Last 24h` and `Last 7d` summaries, advice rows, `Top skills`, and `Top subagents` with additional percentages. Values, skill names, subagent names, and slash-command arguments are omitted here.
- The local attribution section explicitly limits its meaning to approximate, overlapping shares of sessions on this machine. Across observed output variants, the heading was either `What's contributing to your limits usage?` or `What's contributing to your usage?`; the four advice templates were `Start fresh when you switch tasks · run /clear`, `Turn off skills you don't use · Nk tokens per request`, `Compact before you step away · run /<command>`, and `Run /<model> on a cheaper model`. Some advice lines appeared under both `Last 24h` and `Last 7d`. These percentages are not subscription-window used shares. A sanitized fixture with one such percentage reproduced parser rejection before the change; the existing window rows alone retained the accepted grammar.

## Evidence boundaries

The observed JSON envelope proves zero model turns, API duration, and cost for those calls; it does not close the controlled ten-poll non-consumption gate or prove that no other account work occurred. The direct command did not exercise the production child image-loading policy. After the parser change, the published app's visible Claude section exposed both qualified values through Windows UI Automation, without the unavailable label or unknown-format tooltip. This check does not close the same-window account comparison or the B012/B013 manual and resource gates. This report contains no raw account output or identifiers.
