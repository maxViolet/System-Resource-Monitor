# B009 implementation plan for coding agents

Status: planned. This package implements [B009 — Per-user installation and autostart](../B09-install-autostart.md). The [product-plan installation track](../../plan.md#installation-and-autostart), [independent later slices](../../implementation-plan.md#independent-later-slices), [installation requirements](../../implementation-plan.md#installation-and-autostart), [settings and lifecycle](../../implementation-plan.md#settings-and-lifecycle), [first-release gates](../../implementation-plan.md#first-release-acceptance-gates), [ADR 0014](../../adr/0014-per-user-installation-and-autostart.md), and [verification commands](../../implementation-plan.md#automated-verification-and-handoff) govern behavior and acceptance. The [B007 plan](../B07-first-release/plan.md) defines the prerequisite artifact and evidence. [ADR 0005](../../adr/0005-project-level-runtime-and-testing-platform.md) explains the build and test commands.

## Objective and boundary

Install the self-contained Windows 11 x64 Desktop payload for the current user, optionally start it at sign-in, upgrade it safely, and uninstall it without elevation. The canonical application directory is `%LocalAppData%\Programs\SystemResourceMonitor`; the executable is `SystemResourceMonitor.Desktop.exe` inside it. Settings remain in `%LocalAppData%\SystemResourceMonitor\settings.json`, outside the application directory. Uninstall always retains settings and any `settings.invalid-*.json` backups. The [implementation plan](../../implementation-plan.md#installation-and-autostart) owns these shared choices.

The scripts own only their installed payload and one `HKCU` Run value. Do not change Core/Desktop behavior or their boundary to implement installation. Do not add machine-wide installation, elevation of the bar, MSIX/MSI, automatic update, or public package provenance. B016 owns public distribution. A change to shared behavior or a design choice with meaningful alternatives follows the documentation and ADR process in `AGENTS.md`.

## Ordered work

### 1. Establish the B007 handoff and package identity

- Require B007 `done` with a dated acceptance report. Inspect its accepted provider configuration, release-gate results, published folder inventory, source commit, and hashes. B009 can be planned before B007 closes, but implementation and acceptance cannot proceed on a guessed payload.
- Publish the B009 candidate from one recorded source commit using the locked restore and self-contained publish commands below. Inventory and hash every payload file. Exclude the test host, fixtures, secrets, development configuration, and fault switches as in B007.
- Implement Windows PowerShell 5.1 entry points at `packaging/install.ps1` (clean install or upgrade, no automatic launch), `packaging/set-autostart.ps1` (`-Enabled $true` or `$false`), and `packaging/uninstall.ps1`. Distribute them beside the published Desktop folder, not inside it. Put exact invocations, optional autostart behavior, settings retention, and script execution-policy prerequisites in `packaging/README.md`; never change a machine- or user-scoped execution policy. If a system policy blocks the scripts, record the gate as `unverified` rather than bypassing that policy.
- Use the fixed `SystemResourceMonitor` Run value name and `install-manifest.json` format in the [implementation plan](../../implementation-plan.md#installation-and-autostart): source commit plus `{ relativePath, sha256 }` for every installed payload file. Reject absolute paths, `..`, duplicate names, and paths that escape the application directory. Do not accept a manifest as proof of ownership when its file inventory or hashes do not match the installed payload.
- Add script checks under `tests/installation/` using a disposable Windows 11 user profile and the same production entry points and fixed destination. Do not add a production destination override for tests. Record each script command and exit code separately from `dotnet test`.

**Gate:** B007 has passing prerequisite evidence; the source commit, self-contained payload, fixed script entry points, manifest format, Run value name, and user commands are fixed and reviewable.

### 2. Implement clean per-user installation

- Resolve `%LocalAppData%` through the current user's Windows known-folder location. Build and canonicalize the fixed destination; do not accept a destination or executable path from an argument, the current directory, or `PATH`. Reject a reparse point on the destination or any traversed component. Require the application directory's owner to be the current user's SID; allow effective write access only to that SID, `SYSTEM`, and built-in `Administrators` on the installation subtree. Reject write access granted to other principals. Recheck before each phase. The boundary excludes an actively malicious process running as the same user; do not promise that a path check alone closes that race.
- Stage the complete payload in a newly created `.SystemResourceMonitor.stage-<GUID>` sibling under the verified `%LocalAppData%\Programs` parent. Verify its manifest, hashes, ownership, ACLs, and lack of reparse points before renaming it to the active directory. On failed initial staging, remove only its verified files and empty staging directory. An unexpected active directory is a conflict, not permission to overwrite it.
- Install without UAC or writes outside the documented per-user application location and optional Run value. Neither installation nor upgrade launches the bar. A bar still running from an extracted folder retains the single-instance guard; the user must exit it before manually launching the installed executable.

**Gate:** a clean Windows 11 profile yields exactly the documented application files, manifest, and optional Run value, with no UAC; the installed executable and files match the candidate hashes. Inject a stage-copy or hash failure before the rename and confirm no partial active payload remains.

### 3. Implement optional sign-in startup

- Use the `SystemResourceMonitor` `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` `REG_SZ` value. Its complete data is the quoted, canonical absolute path of the installed executable, with no arguments or shell interpolation. Compare type and data as exact values; a pre-existing value without a valid installed manifest is foreign even if its data matches. Never overwrite a foreign value.
- Provide an independent disable operation that removes the value only when its type and data still match the owned value. Preserve the installed files and settings. Re-enabling verifies the destination, ACLs, and reparse-point state again.
- Verify a real sign-out/sign-in with autostart enabled and disabled. Before the enabled run, check that Windows Startup Apps shows the entry enabled; if it is disabled, record the gate as `unverified` until the user enables it through Windows Settings or Task Manager. Do not edit the undocumented approval state. Observe for up to 120 seconds after the desktop becomes available because Windows may delay Run entries. Count bar processes and AppBar reservations; ensure the single-instance guard does not leave a second process or window.

**Gate:** enabled sign-in starts exactly one installed bar; disabled sign-in starts none. Disablement changes only the owned Run value.

### 4. Implement upgrade and uninstall

- Before replacement or removal, identify the process by the canonical installed executable path, locate its bar HWND, and send `WM_CLOSE` to invoke the existing handled close path. Wait up to 5 seconds for exit. If the process cannot be identified uniquely or does not exit, stop without replacing or deleting payload files. The acceptance harness records the pre-launch work-area baseline and verifies its restoration after exit; the script does not infer that baseline from a running bar.
- For upgrade, recheck path, ownership, ACLs, reparse points, and the installed manifest. Abort if any manifest-listed file changed or an unexpected file is present. Stage and verify the new payload as in step 2. Rename the old application directory to a `.SystemResourceMonitor.previous-<GUID>` sibling, then rename the verified stage to the canonical directory. If activation fails, rename the old directory back; on the next invocation, detect and reconcile an interrupted swap before any new mutation. Do not claim success until the new executable and manifest match the candidate hashes. Preserve the existing autostart choice and settings; do not launch the bar.
- For uninstall, remove the Run value only if its type and data still match the owned value and the installed manifest validates. Remove only unchanged manifest-listed application files and the owned manifest, then remove installer-created directories only when empty. Leave changed or unexpected files and their containing directories in place and report the conflict; do not recursively delete. Always retain `%LocalAppData%\SystemResourceMonitor` and its settings.

**Gate:** a successful upgrade leaves a verified new payload ready for manual launch, with settings and autostart choice preserved. Inject a failure between the two directory renames and confirm rollback, then interrupt a swap and confirm the next invocation recovers or stops with the old payload intact. Successful uninstall leaves no installed bar process or AppBar reservation, removes owned payload and Run state, and retains settings. Conflicts fail without deleting changed files.

### 5. Verify safety and installed-app behavior

- Test clean install, repeat install, upgrade, disable/re-enable, and repeat uninstall using the actual scripts in a disposable profile. Exercise a path containing spaces; a planted executable with the installed name as a prefix; a foreign Run value; changed ownership or ACLs; and a pre-planted junction or symlink at the destination or a traversed component. For each phase, plant a replacement before its path recheck and confirm that it aborts without following the reparse point. Do not present this as proof against a malicious process running concurrently as the same user.
- Always run the installed-path B007 gates: artifact inventory and self-contained launch without UAC; accepted-provider resolution from the installed location; normal exit and work-area restoration; Explorer recovery; and sign-in startup. Compare the installed executable and loaded production assembly hashes and provider configuration with accepted B007 evidence. If they match and no production component or runtime configuration changed, cite the B007 CPU/RAM accuracy, DPI/fullscreen, and resource-budget evidence as unchanged-artifact evidence. If any identity or configuration differs, rerun those affected B007 gates, including the 30-minute CPU/memory and eight-hour memory budgets. Keep test-host results separate; rerun an injected gate only if a production component it exercises changed.
- Record the source commit and SHA-256 of the installed executable and loaded production assemblies. Create separate dated automated, Windows integration, and manual desktop/resource reports under `docs/acceptance/`, linked by a short B009 gate index. For install, sign-out/in, disable, exit, upgrade, and uninstall, record process count, installed-file inventory and hashes, Run value type/data comparison, settings retention, and work-area state. Sanitize paths and account information as required by the implementation plan. Mark unrun scenarios `unverified`.

**Gate:** every B009 acceptance criterion has passing evidence on Windows 11 x64 and every affected B007 gate passes for the installed artifact or has documented identical-artifact evidence as specified above. A command result alone does not close sign-in, AppBar, or desktop checks.

## Verification and handoff

Run targeted Release tests first for each changed test project, then run the installation script checks in their disposable profile. Run the repository commands from the root on Windows 11 x64:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

Record commands, exit codes, artifact hashes, negative-case results, and the Windows observations in the B009 acceptance report. Update the roadmap progress only when its stated evidence supports the new state. Do not mark B009 `done` from script tests, CI, or an extracted-folder B007 report alone.
