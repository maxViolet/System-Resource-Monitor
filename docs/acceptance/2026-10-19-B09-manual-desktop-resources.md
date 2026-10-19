# B009 manual desktop and resources — 2026-10-19

Brick: [B009 — Per-user installation and autostart](../bricks/B09-install-autostart.md). Package: [B009 implementation plan](../bricks/B09-install-autostart/plan.md). Gate index: [B009 gate index](2026-10-19-B09-gate-index.md).

Status: no manual scenario has been run. Every row below is `unverified`. The [Windows integration report](2026-10-19-B09-windows-integration.md) covers script behavior, including closing a running installed bar and restoring the work area; it does not replace these scenarios.

## Artifact under test

Install the package recorded in the [automated report](2026-10-19-B09-automated.md#package) with `install.ps1` in a disposable Windows 11 x64 profile. No accepted B007 artifact hashes exist to compare with, so the installed executable cannot use unchanged-artifact evidence: every affected B007 gate must run on the installed artifact.

## Scenarios

| Scenario | Method and pass condition | Result |
|---|---|---|
| Clean-profile install without UAC | In a new disposable profile without `%LOCALAPPDATA%\Programs`, run `install.ps1`. No UAC prompt; the application folder contains exactly the manifest-listed files and `install-manifest.json`; no Run value; no bar process. | unverified |
| Startup Apps state | After `set-autostart.ps1 -Enabled $true`, Windows Settings → Apps → Startup shows System Resource Monitor enabled. If it shows disabled, the sign-in gate stays unverified until the user enables it there. | unverified |
| Sign-in with autostart enabled | Sign out and in. Within 120 seconds after the desktop appears: exactly one installed bar process, one reserved strip, no second window. Record process count, Run value type and data, and work area. | unverified |
| Sign-in with autostart disabled | Run `set-autostart.ps1 -Enabled $false`, sign out and in, wait 120 seconds: no bar process; installed files and settings unchanged. | unverified |
| Exit | Exit the installed bar from its tray icon: process exits, work area returns to the pre-launch value. | unverified |
| Upgrade and uninstall with the bar running after sign-in | Repeat the integration running-bar checks with the bar started by sign-in: process exits within 5 seconds, work area restored, payload replaced or removed, settings kept. | unverified |
| Accepted-provider resolution | No accepted provider exists at this commit; record the provider configuration of the installed artifact. | unverified |
| Explorer recovery | Restart Explorer with the installed bar running: strip reserved again, tray icon position and Exit work. | unverified |
| Affected B007 gates | CPU/RAM accuracy, app CPU cost, memory ceiling, eight-hour memory growth, desktop behavior, DPI and rendering, artifact hygiene, normal shutdown, run on the installed executable. | unverified |

## Evidence to record

For install, sign-out/in, disable, exit, upgrade, and uninstall: bar process count, installed-file inventory and hashes, Run value type and data compared with the owned value, settings-file hashes, and work area. Record the SHA-256 of the installed executable and of the production assemblies loaded by the running bar. Sanitize user names and absolute user paths.
