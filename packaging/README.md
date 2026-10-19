# Install System Resource Monitor for the current user

These scripts install the self-contained bar for your Windows account, optionally start it when you sign in, upgrade it, and uninstall it. They never need administrator rights, and the bar never runs elevated.

## What the package contains

| Item | Purpose |
|---|---|
| `app\` | The published bar. Do not change its files. |
| `install-manifest.json` | The source commit and the SHA-256 of every file in `app\`. |
| `install.ps1` | Installs or upgrades the bar. It does not start the bar. |
| `set-autostart.ps1` | Turns start at sign-in on or off. |
| `uninstall.ps1` | Removes the installed bar. |
| `SystemResourceMonitor.Install.psm1` | Shared code used by the three scripts. |

Keep the extracted package folder, or at least its scripts and `SystemResourceMonitor.Install.psm1`, to change autostart or uninstall later.

## Where the bar is installed

| Item | Location |
|---|---|
| Application folder | `%LOCALAPPDATA%\Programs\SystemResourceMonitor` |
| Executable | `%LOCALAPPDATA%\Programs\SystemResourceMonitor\SystemResourceMonitor.Desktop.exe` |
| Installed file list | `%LOCALAPPDATA%\Programs\SystemResourceMonitor\install-manifest.json` |
| Settings (never removed) | `%LOCALAPPDATA%\SystemResourceMonitor` |
| Autostart (optional) | `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`, value `SystemResourceMonitor` |

The location is fixed; the scripts take no destination argument.

## Before you start

- Windows 11 x64 and Windows PowerShell 5.1, which Windows includes.
- Open **Windows PowerShell** normally. Do not use **Run as administrator**; the scripts refuse to run elevated.
- Exit any bar you started from an extracted release folder. Only one bar runs per Windows session.

PowerShell may refuse to run scripts from a downloaded folder. Allow them for the current PowerShell window only:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

This setting ends when you close the window. Do not change the execution policy for your user or the computer. If your organization sets the policy through Group Policy, the command above has no effect and the scripts cannot run.

In the same window, go to the extracted package folder:

```powershell
Set-Location '<path to the extracted package>'
```

Every script prints what it did. Exit code `0` (`$LASTEXITCODE`) means success; `1` means the script stopped and printed the reason after `Error:`.

## Install

```powershell
.\install.ps1
```

The script verifies `app\` against `install-manifest.json`, copies it into a temporary folder next to the application folder, verifies the copy, and then moves it into place. If anything fails before that move, no application folder is created.

The bar does not start. The scripts add no Start menu entry; start the bar by running:

```powershell
& "$env:LOCALAPPDATA\Programs\SystemResourceMonitor\SystemResourceMonitor.Desktop.exe"
```

Running `install.ps1` again with the same package changes nothing.

## Start at sign-in (optional)

Turn autostart on:

```powershell
.\set-autostart.ps1 -Enabled $true
```

Turn it off without uninstalling:

```powershell
.\set-autostart.ps1 -Enabled $false
```

Autostart writes one Run value whose data is the quoted path of the installed executable. The script never replaces a `SystemResourceMonitor` Run value that it did not write; it stops and leaves that value unchanged.

Windows can delay Run entries for a short time after sign-in. Windows Settings → **Apps** → **Startup** and Task Manager's **Startup apps** page can also block the entry. The scripts do not change that choice; turn **System Resource Monitor** on there if the bar does not start.

## Upgrade

Extract the newer package and run its `install.ps1`:

```powershell
.\install.ps1
```

The script first checks that the installed files still match their manifest. It then prepares and verifies the new files, closes a running installed bar through its normal exit, and swaps the folders. If the swap fails, the previous installation is restored. If an upgrade was interrupted, the next run of any script finishes or undoes the swap before it changes anything else.

Settings and the autostart choice stay as they were. The bar is not restarted; start it again or sign in.

The upgrade stops without changes when an installed file was changed, an unexpected file was added, or the installed bar does not exit within 5 seconds. Exit the bar from its notification-area icon and run the script again.

## Uninstall

```powershell
.\uninstall.ps1
```

The script closes a running installed bar, which releases its screen strip, removes the autostart value it owns, and removes the installed files listed in the manifest. It does not delete:

- your settings in `%LOCALAPPDATA%\SystemResourceMonitor`, including any `settings.invalid-*.json` backups;
- an installed file that was changed after installation;
- a file that the installation did not create.

If it leaves files behind, it lists them, exits with code `1`, and keeps the manifest. It then keeps the autostart value as well, because changed or extra files mean it cannot prove the installation is its own. Remove or restore the listed files and run `uninstall.ps1` again. To remove the settings, delete `%LOCALAPPDATA%\SystemResourceMonitor` yourself after uninstalling.

## Safety checks

Each script stops without changes when it finds one of these conditions:

- a junction, symbolic link, or other reparse point on the path to the application folder or inside it;
- an application folder that is not owned by your account;
- write permission for anyone other than your account, `SYSTEM`, or the Administrators group on `Programs`, the application folder, or its files;
- an installed manifest with an absolute path, a `..` segment, a duplicate entry, or a path outside the application folder;
- another installation script already running.

The scripts check these conditions again before each step. They protect against other accounts and against files planted before or between steps. They cannot protect against a malicious program that runs as your own account at the same time.

## Build a package (developers)

From a clean commit at the repository root:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
powershell.exe -NoProfile -ExecutionPolicy Bypass -File packaging\new-package.ps1
```

`new-package.ps1` refuses uncommitted changes to the publish inputs and test or development files in the publish folder. It writes `artifacts\SystemResourceMonitor-<commit>\` and prints the source commit, file count, and executable SHA-256. The installation checks live in [`tests/installation`](../tests/installation/README.md).
