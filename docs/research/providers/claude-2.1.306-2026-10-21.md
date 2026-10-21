# Claude Code 2.1.306 `/usage` follow-up — 2026-10-21

Brick: [B012](../../bricks/B12-claude-source.md). Decision: [ADR 0020](../../adr/0020-qualify-claude-code-2-1-306.md). The [2.1.304 report](claude-2026-10-21.md) remains the historical qualification for that client version.

## Source observation

- Windows 11, Asia/Qyzylorda, Pro account. The native client at `%USERPROFILE%\.local\bin\claude.exe` reported Claude Code 2.1.306, file version `2.1.306.0`, and a valid Authenticode signature. `auth status --json` returned `loggedIn: true`.
- Two consecutive non-interactive `/usage` polls used `--safe-mode --tools "" --no-session-persistence --print --output-format json "/usage"`. Each returned `local_command: usage`, `is_error: false`, `num_turns: 0`, `duration_api_ms: 0`, and `total_cost_usd: 0`.
- Both polls showed `Current session` at 13% used and `Current week (all models)` at 2% used, each with a minute-precision reset in Asia/Qyzylorda. Thus the adapter should show 87% short remaining and 98% weekly remaining in the same observation window. No model-specific weekly or monthly row appeared.
- The signed 2.1.306 client completed a test-host `/usage` probe under the ADR 0019 child image-loading policy. No credential or transcript contents were read; only the sanitized limit rows and envelope fields above were retained.

## Built adapter and published-app observations

The built adapter completed ten polls one minute apart. Polls 1–6 returned 13% used for the session and 2% used for the weekly window. Polls 7–10 returned 14% and 2%. Every poll returned one weekly row and an active session reset. The Claude project transcript file count rose from 333 to 335 during the run. Other Claude processes were present, so this series does **not** close the no-other-work or non-consumption gate. The changing share cannot be attributed to this adapter or excluded as other account activity from these observations alone.

After a Release self-contained publish, the visible production app displayed `Claude: 5H - 86% in 01 19 | W 98% | month: --`. In the same verification window, the official 2.1.306 `/usage` command reported 14% session used, reset 21 October at 17:30, and 2% weekly used, reset 28 October at 03:00, in Asia/Qyzylorda. The accessibility tooltip reported 86% and 98% left and the same two reset times at minute precision. Its Claude text rectangle (305 by 16 pixels) was visible inside the 1920 by 24 pixel bar. The published executable's SHA-256 was `83FBE6A64DB117B6C1F55DBA0764E74944EC81D69A9CA06BD4EB270F1D593963`; the folder contained neither `claude.exe` nor the test host. The app was left running for inspection.

The targeted Claude locator tests passed 6/6. Locked restore, Release solution build with zero warnings, full solution tests 464/464, and self-contained publish passed. `git diff --check` passed.

With an isolated empty `CLAUDE_CONFIG_DIR`, the built adapter reported `SignedOut` on its first poll. The real account configuration was not changed, and the temporary directory was removed.

## Review fixes and final artifact

Review found that a parseable response from a nonzero child exit could be accepted and that the pipe bound counted decoded characters instead of UTF-8 bytes. The adapter now checks the child exit status and independently bounds raw stdout and stderr bytes before decoding. Fake-client tests cover a nonzero auth exit, a nonzero `/usage` exit with valid-looking JSON, and a multibyte response whose byte size exceeds the bound. Targeted source tests passed 9/9. After these fixes, locked restore, Release solution build with zero warnings, full solution tests 467/467, and self-contained publish passed. The real 2.1.306 adapter probe returned the two expected windows twice.

The final published executable's SHA-256 is `76FFB57EBB06DBBE63E253041DADA6344E47A09042CC901C014E07990C00328D`. At approximately 16:35 Asia/Qyzylorda, that artifact displayed `Claude: 5H - 84% in 00 55 | W 98% | month: --`. The official client in the same window reported 16% session used with a 21 October 17:30 reset and 2% weekly used with a 28 October 03:00 reset. The tooltip matched both remaining shares and reset minutes. The 305 by 16 pixel Claude section was visible inside the 1920 by 24 pixel bar. The app was left running.

## Gates still open

The controlled ten one-minute polls with no other account work, including the B013 adapter repeat, remain unverified for 2.1.306. The earlier 2.1.304 result does not close this gate. Other tiers, model-specific windows, a distinct network response for 2.1.306, and long desktop resource checks remain unverified.
