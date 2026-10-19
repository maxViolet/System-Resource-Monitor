# Installation script checks

`Invoke-InstallationChecks.ps1` runs the production `install.ps1`, `set-autostart.ps1`, and `uninstall.ps1` from a built package against the fixed destination `%LOCALAPPDATA%\Programs\SystemResourceMonitor` and the `HKCU` Run value `SystemResourceMonitor`. The scripts have no destination override, so the checks change the real per-user location. Run them in a disposable Windows 11 user profile.

The checks start only when the installation, its `.SystemResourceMonitor.*` work directories, the Run value, and the planted test directory are absent. They remove what they created when they finish, and they only read `%LOCALAPPDATA%\SystemResourceMonitor` to prove that settings are retained.

## Run

From a non-elevated Windows PowerShell 5.1 window at the repository root, after building a package as described in [`packaging/README.md`](../../packaging/README.md#build-a-package-developers):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\installation\Invoke-InstallationChecks.ps1 `
    -PackageDirectory artifacts\SystemResourceMonitor-<commit> -ConfirmDisposableProfile -ResultPath artifacts\installation-checks.md
```

The harness prints one line per check with the script command and its exit code, and writes the same rows as a Markdown table to `-ResultPath`. It exits with `0` only when no check failed. Record this command and its exit code separately from `dotnet test`.

## Coverage

- Candidate manifests with absolute, drive-relative, parent-traversal, duplicate, and alternate-data-stream paths, and a payload that does not match its manifest.
- A junction and, where Windows allows it, a directory symbolic link at the destination; a junction at `Programs` when the profile has no `Programs` directory yet; a junction inside the application directory.
- Injected stage-copy and staged-hash failures before activation.
- A junction or moved installation planted immediately before the recheck that precedes the install rename, the upgrade swap, autostart removal, uninstall Run-value removal, and uninstall file removal.
- Clean install, repeat install, no launch, enable, repeat enable, disable, repeat disable, and re-enable.
- Foreign Run values: unquoted, with an argument, pointing to a planted copy whose folder name has the installed name as a prefix, and with the `REG_EXPAND_SZ` type. A value that already starts the installed path before installation, or is planted during staging, stops install and survives disable and uninstall.
- Write access for `Users` on the application directory, an installed file, and a new `Programs` directory; an Administrators owner when the account can assign it.
- Changed and unexpected installed files during upgrade; changed, missing, and unexpected installed files during uninstall, which then changes nothing; and tampered installed manifests.
- Upgrade with retained settings and autostart choice, an injected failure between the two renames, and a process killed after each rename followed by recovery in the next invocation.
- When no other bar holds the session guard: upgrade and uninstall close a running installed bar through `WM_CLOSE`, the work area returns to its pre-launch value, and a running planted copy is neither closed nor removed.

Failure injections run in a child process that imports the package module and replaces one of its internal functions before calling the same entry function as the script. They do not add a switch or environment variable to the production scripts.

Skipped rows are not passes. Sign-out/sign-in, Startup Apps state, Explorer recovery, and the other desktop gates need the manual Windows 11 scenarios in the [B009 package plan](../../docs/bricks/B09-install-autostart/plan.md).
