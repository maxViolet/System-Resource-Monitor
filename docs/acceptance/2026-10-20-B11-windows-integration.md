# B011 Windows integration — 2026-10-20

Brick: [B011 — Codex remaining limits](../bricks/B11-codex-limits.md). Companion reports: [automated](2026-10-20-B11-automated.md), [manual desktop and resources](2026-10-20-B11-manual-desktop-resources.md).

Status: the built adapter works against the real client, and the ten-poll non-consumption check passes. The combined memory gate **fails** on the published artifact (see below).

## Environment

- Windows 11 Pro 26H2, build 26300, timezone UTC+05:00. Observations ran on 2026-10-20 between 16:37 and 17:23 local time.
- Working tree on `FEAT-011-codex-limits` from `d831f38`, not yet committed. The test host for the probes was a Release build of the same tree. Since that build, only documentation comments and test files have changed.
- Client: `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe`, canonical path `%USERPROFILE%\.codex\packages\standalone\releases\0.159.0-x86_64-pc-windows-msvc\bin\codex.exe`, `codex-cli 0.159.0`, Authenticode `Valid`, signer `CN="OpenAI OpCo, LLC", O="OpenAI OpCo, LLC", L=San Francisco, S=California, C=US`. The entry chain has two junctions (`bin` → `standalone\current\bin`, and `current` → `releases\0.159.0-x86_64-pc-windows-msvc`). The canonical chain has no reparse point, and only the user, SYSTEM, and Administrators have write access to it. Another local account has read and execute access.
- Account: ChatGPT sign-in, plan tier `plus`. No account identifiers are recorded.
- Other Codex activity: Codex agents in other Herdr workspaces may have been running; this session started no Codex task. No session file appeared during the ten polls (below).

## Built adapter against the real client

`SystemResourceMonitor.Desktop.TestHost.exe --codex-probe <polls> <interval-seconds>` runs the production `CodexClientLocator.ForCurrentUser()` and `CodexAppServerSource`.

| Run | Result |
|---|---|
| Two polls, 5 s apart (16:37) | Both `succeeded`: 5-hour used 27, left 73, resets 2026-10-20 17:55:24; weekly used 4, left 96, resets 2026-10-27 12:55:24. The first poll took 7.9 s, including the first signature verification. The second took 1.7 s. |
| Empty `CODEX_HOME` for the child (signed out, real account untouched) | `failed reason=SignedOut` after 1.2 s. |
| `HTTPS_PROXY=http://127.0.0.1:9` for the child | `failed reason=NetworkFailed` after 3.1 s. |

## Ten-poll non-consumption check with the built adapter

Ten polls 60 seconds apart from 16:41:50 to 16:51:05. Every poll succeeded in 1.6–2.5 s and returned the same values: 5-hour used 27 % (left 73 %), reset 17:55:24; weekly used 4 % (left 96 %), reset 2026-10-27 12:55:24. The 5-hour window had started, so its reset time stayed fixed. Session files `%USERPROFILE%\.codex\sessions\**\rollout-*.jsonl`, counted by name only: 417 before and 417 after, with no new name. **Pass.**

The reference reading from the official TUI `/status` was not taken (see the manual report).

## Published artifact: app plus Codex child private bytes

The artifact was published with the command in the automated report:

| File | SHA-256 |
|---|---|
| `SystemResourceMonitor.Desktop.exe` | `6519d3c3146c4c6787b671448ae1d8399eec899aa94b382d8ce701107a08b47f` |
| `SystemResourceMonitor.Desktop.dll` | `80782dcd81d4e03c24389e86fee2f8c7866f268342d3f396a5896e8cfa7e1908` |
| `SystemResourceMonitor.AIResources.Core.dll` | `9c78d7162c3085daa6e9cb6808d39c13bf60e9bdf14ac00b1b0d818a57468309` |
| `SystemResourceMonitor.Core.dll` | `8139bcb4886cfbbfe75cb69c6b21349afc0fb9b0390cd1e34c3f299960cce96c` |

A PowerShell sampler started the published app, then read `PrivateMemorySize64` of the app and of every process at the qualified release path every 100 ms plus query time. It ran for 200 s; the app was then closed through `WM_CLOSE` and exited.

| Measure | Value |
|---|---|
| Samples | 1,348 (about one every 150 ms); 34 with a child alive |
| Child lifetimes | 4 polls, 4 lifetimes observed; none missed |
| App private bytes | maximum 81.8 MiB; 73.3 MiB at the end |
| Poll 1 (t = 2.2 s) | child maximum 76.6 MiB; app + child maximum 144.8 MiB |
| Poll 2 (t = 61.4 s) | child maximum 81.2 MiB; app + child maximum **162.9 MiB** |
| Poll 3 (t = 121.3 s) | child maximum 100.6 MiB; app + child maximum **175.0 MiB** |
| Poll 4 (t = 181.4 s) | child maximum 59.5 MiB; app + child maximum 133.4 MiB |

An earlier 200 s run of the same artifact had a combined maximum of 174.4 MiB over 4 child lifetimes. **The ADR 0015 gate (app plus child ≤ 150 MiB at every sampled poll) fails.** The child alone reaches 100 MiB, so the gate cannot pass by reducing the app's share alone without a large reduction. The 30-minute idle and eight-hour runs were not performed.

## Shutdown

The published app, closed through `WM_CLOSE` while Codex polling was active, exited within the 10-second wait in both runs. Shutdown during an in-flight client is covered by the automated cancellation tests only. On the published artifact it is **unverified**.
