# B009 Windows integration — 2026-10-19

Brick: [B009 — Per-user installation and autostart](../bricks/B09-install-autostart.md). Package: [B009 implementation plan](../bricks/B09-install-autostart/plan.md). Gate index: [B009 gate index](2026-10-19-B09-gate-index.md).

Status: the installation script checks pass on Windows 11 x64 in the developer's own profile, not in a disposable profile. Rows marked `skipped` are unverified. These checks do not establish sign-in startup, Startup Apps state, Explorer recovery, or the other installed-path B007 gates; see the [manual desktop and resource report](2026-10-19-B09-manual-desktop-resources.md).

## Setup

| Field | Value |
|---|---|
| Target | Windows 11 Pro x64, build 26300; 22 logical processors |
| Shell | Windows PowerShell 5.1, not elevated; `CurrentUser` execution policy `RemoteSigned`, process scope `Bypass` for each child |
| Package | `artifacts\SystemResourceMonitor-8f9477a`, built by `packaging\new-package.ps1` from source commit `8f9477ac9dde06aa653ad721e3e41cbcb8f09053` (merged PR #32) with the FIX-001 module and checks from the working tree; the payload hashes equal those in the [automated report](2026-10-19-B09-automated.md#package) |
| Profile | The developer's profile. `%LOCALAPPDATA%\Programs` already existed and is shared with other apps. Before each run, the application directory, its `.SystemResourceMonitor.*` siblings, and the `SystemResourceMonitor` Run value were absent; after each run they were absent again. |
| Paths with spaces | Both candidate packages ran from `%TEMP%\SRM installation checks <id>\package A` and `…\package B`; the planted copy lived in `%LOCALAPPDATA%\Programs\SystemResourceMonitor Planted`. |
| Candidates | Package A is the built package. Package B is a copy whose `app\README.md` has one appended line and whose manifest was regenerated with the same source commit, so an upgrade changes one payload file. |

The first run of the harness on this date skipped the running-bar checks because a bar from another worktree held the session guard. With the user's approval, that bar was closed through `WM_CLOSE`. The table below is the run after the FIX-001 ownership fixes: a Run value that already starts the installed path before installation stops the install, and uninstall changes nothing when any listed file is changed, missing, or unexpected.

## Command

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\installation\Invoke-InstallationChecks.ps1 `
    -PackageDirectory artifacts\SystemResourceMonitor-dc4b13d -ConfirmDisposableProfile -ResultPath artifacts\checks.md
```

Exit code `0`: 68 passed, 0 failed, 4 skipped. Rows naming `Invoke-Srm*` ran in a child process that replaced one internal module function before calling the entry function that the script calls. Exit code `-1` is the forced termination of that child.

## Results

| Check | Command | Exit code | Result | Observation |
|---|---|---|---|---|
| Reject candidate manifest: absolute path | `install.ps1 (crafted manifest)` | 1 | pass | nothing installed |
| Reject candidate manifest: parent segment | `install.ps1 (crafted manifest)` | 1 | pass | nothing installed |
| Reject candidate manifest: duplicate path | `install.ps1 (crafted manifest)` | 1 | pass | nothing installed |
| Reject candidate manifest: drive-relative path | `install.ps1 (crafted manifest)` | 1 | pass | nothing installed |
| Reject candidate manifest: alternate data stream | `install.ps1 (crafted manifest)` | 1 | pass | nothing installed |
| Reject package payload that does not match its manifest | `install.ps1 (package B, tampered)` | 1 | pass | nothing installed |
| Junction at destination: install | `install.ps1` | 1 | pass | decoy untouched, junction left |
| Junction at destination: enable autostart | `set-autostart.ps1 -Enabled $true` | 1 | pass | Run value absent |
| Junction at destination: uninstall | `uninstall.ps1` | 1 | pass | decoy untouched |
| Directory symbolic link at destination | `install.ps1` |  | skipped | creating a symbolic link needs Developer Mode or elevation |
| Junction at traversed Programs component | `install.ps1` |  | skipped | Programs already exists in this profile and is shared with other apps |
| Injected stage-copy failure | `Invoke-SrmInstall (copy fails after 5 files)` | 1 | pass | no active payload, staging removed |
| Injected staged hash mismatch | `Invoke-SrmInstall (third copy corrupted)` | 1 | pass | no active payload; only the unverified file and staged manifest remain |
| Planted junction before install rename | `Invoke-SrmInstall (junction planted at recheck 4)` | 1 | pass | decoy untouched, staging removed |
| Clean install | `install.ps1 (package A)` | 0 | pass | files and hashes match package A plus manifest; no Run value; bar not started |
| Repeat install | `install.ps1 (package A)` | 0 | pass | unchanged |
| Enable autostart | `set-autostart.ps1 -Enabled $true` | 0 | pass | REG_SZ equals the quoted canonical executable path |
| Repeat enable | `set-autostart.ps1 -Enabled $true` | 0 | pass | unchanged |
| Disable autostart | `set-autostart.ps1 -Enabled $false` | 0 | pass | Run value removed; files kept |
| Repeat disable | `set-autostart.ps1 -Enabled $false` | 0 | pass | unchanged |
| Foreign Run value (unquoted path): enable | `set-autostart.ps1 -Enabled $true` | 1 | pass | value unchanged |
| Foreign Run value (unquoted path): disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | value unchanged |
| Foreign Run value (path with argument): enable | `set-autostart.ps1 -Enabled $true` | 1 | pass | value unchanged |
| Foreign Run value (path with argument): disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | value unchanged |
| Foreign Run value (planted prefix executable): enable | `set-autostart.ps1 -Enabled $true` | 1 | pass | value unchanged |
| Foreign Run value (planted prefix executable): disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | value unchanged |
| Foreign Run value (REG_EXPAND_SZ type): enable | `set-autostart.ps1 -Enabled $true` | 1 | pass | value unchanged |
| Foreign Run value (REG_EXPAND_SZ type): disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | value unchanged |
| Enable autostart before negative cases | `set-autostart.ps1 -Enabled $true` | 0 | pass | owned |
| Users write access on application directory: upgrade | `install.ps1 (package B)` | 1 | pass | nothing changed |
| Users write access on application directory: disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | nothing changed |
| Users write access on application directory: uninstall | `uninstall.ps1` | 1 | pass | nothing changed |
| Users write access on installed file: upgrade | `install.ps1 (package B)` | 1 | pass | nothing changed |
| Users write access on installed file: disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | nothing changed |
| Users write access on installed file: uninstall | `uninstall.ps1` | 1 | pass | nothing changed |
| Users write access on Programs | `uninstall.ps1` |  | skipped | Programs already exists in this profile and is shared with other apps |
| Changed application directory owner | `uninstall.ps1` |  | skipped | a standard user cannot assign another owner |
| Changed installed file: upgrade | `install.ps1 (package B)` | 1 | pass | nothing changed |
| Changed installed file: disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | Run value kept |
| Unexpected installed file: upgrade | `install.ps1 (package B)` | 1 | pass | nothing changed |
| Junction inside the application directory: uninstall | `uninstall.ps1` | 1 | pass | nothing changed |
| Planted replacement before upgrade swap | `Invoke-SrmInstall (replacement at recheck 4)` | 1 | pass | moved installation intact; Run value unchanged |
| Planted replacement before autostart disable | `Invoke-SrmSetAutostart (replacement at recheck 3)` | 1 | pass | moved installation intact; Run value unchanged |
| Planted replacement before uninstall Run removal | `Invoke-SrmUninstall (replacement at recheck 3)` | 1 | pass | moved installation intact; Run value unchanged |
| Planted replacement before uninstall file removal | `Invoke-SrmUninstall (replacement at recheck 4)` | 1 | pass | moved installation intact; Run value unchanged |
| Re-enable after negative cases | `set-autostart.ps1 -Enabled $true` | 0 | pass | owned, files intact |
| Upgrade A to B | `install.ps1 (package B)` | 0 | pass | payload B; autostart and settings kept |
| Injected failure between the two renames | `Invoke-SrmInstall (second rename fails)` | 1 | pass | payload B restored, no work directories |
| Interrupt after the first rename | `Invoke-SrmInstall (process killed)` | -1 | pass | no active directory; previous and staging directories remain |
| Next invocation recovers, then upgrades | `install.ps1 (package A)` | 0 | pass | payload A, no work directories |
| Interrupt after the second rename | `Invoke-SrmInstall (process killed)` | -1 | pass | payload B active; previous directory remains |
| Next invocation removes the previous installation | `set-autostart.ps1 -Enabled $true` | 0 | pass | payload B, no work directories |
| Upgrade closes the running bar | `install.ps1 (package A)` | 0 | pass | bar exited, work area restored, not restarted |
| Planted prefix executable is not closed or removed | `uninstall.ps1` | 0 | pass | planted bar still running; installation removed |
| Uninstall closes the running bar | `uninstall.ps1` | 0 | pass | bar exited, work area restored, payload and Run value removed |
| Uninstall with an unexpected file | `uninstall.ps1` | 1 | pass | nothing removed; Run value kept |
| Uninstall with a changed file | `uninstall.ps1` | 1 | pass | nothing removed; Run value kept |
| Uninstall with a missing file | `uninstall.ps1` | 1 | pass | nothing removed; Run value kept |
| Uninstall after the conflicts are resolved | `uninstall.ps1` | 0 | pass | payload, manifest, directory, and Run value removed |
| Pre-existing matching Run value: install | `install.ps1 (package A)` | 1 | pass | nothing installed; value unchanged |
| Pre-existing matching Run value: disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | value unchanged |
| Pre-existing matching Run value: uninstall | `uninstall.ps1` | 1 | pass | value unchanged |
| Matching Run value planted during staging: install | `Invoke-SrmInstall (value planted at recheck 3)` | 1 | pass | nothing installed; value unchanged |
| Installed manifest with '../escape.txt': uninstall | `uninstall.ps1` | 1 | pass | nothing removed |
| Installed manifest with '../escape.txt': disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | Run value kept |
| Installed manifest with 'C:/escape.txt': uninstall | `uninstall.ps1` | 1 | pass | nothing removed |
| Installed manifest with 'C:/escape.txt': disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | Run value kept |
| Installed manifest with 'SYSTEMRESOURCEMONITOR.DESKTOP.EXE': uninstall | `uninstall.ps1` | 1 | pass | nothing removed |
| Installed manifest with 'SYSTEMRESOURCEMONITOR.DESKTOP.EXE': disable | `set-autostart.ps1 -Enabled $false` | 1 | pass | Run value kept |
| Uninstall | `uninstall.ps1` | 0 | pass | payload and Run value removed; settings retained |
| Repeat uninstall | `uninstall.ps1` | 0 | pass | nothing to do |
| Enable autostart without an installation | `set-autostart.ps1 -Enabled $true` | 1 | pass | Run value not written |

"Settings retained" compares the SHA-256 of every file in `%LOCALAPPDATA%\SystemResourceMonitor` before the first check and after upgrade and uninstall. "Work area restored" compares `SPI_GETWORKAREA` before launching the installed bar with the value after the script returned; the bar's reserved work area differed from the baseline while it ran. "Bar not started" counts processes whose path is the installed executable 2 seconds after the script returned.

## Unverified here

- A disposable profile. The plan requires one; these runs used the developer profile with clean preconditions.
- A junction at `%LOCALAPPDATA%\Programs` and `Users` write access on `Programs`: the harness runs these only when `Programs` does not exist yet.
- A directory symbolic link at the destination: creating one needs Developer Mode or elevation, which this account did not have.
- An application directory owned by another principal: a standard user cannot assign one. The scripts compare the owner SID with the current user's SID before every phase.
