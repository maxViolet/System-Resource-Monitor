# Per-user installation and autostart

Status: accepted, 2026-10-19. Applies to the [installation track](../plan.md#installation-and-autostart), [implementation requirements](../implementation-plan.md#installation-and-autostart), and [B009](../bricks/B09-install-autostart.md).

## Context

The first personal release runs from an extracted self-contained folder. B009 adds repeatable installation, optional sign-in startup, upgrade, and uninstall for the current user without elevating the bar. The executable must stay separate from settings so that replacement and removal do not destroy the user's edge choice. An installer must not follow a planted junction into another location or delete files it did not install.

## Decision

- Install the self-contained Desktop payload in the current user's canonical `%LocalAppData%\Programs\SystemResourceMonitor` directory. The executable there is `SystemResourceMonitor.Desktop.exe`. Keep settings at `%LocalAppData%\SystemResourceMonitor`; uninstall always retains that settings directory and its invalid-settings backups.
- Use Windows PowerShell 5.1 scripts distributed beside, not inside, the published Desktop folder. `packaging/install.ps1` installs or upgrades without launching the bar, `packaging/set-autostart.ps1` enables or disables sign-in startup, and `packaging/uninstall.ps1` removes the installation. The B009 implementation supplies invocation instructions and script checks. The scripts do not change the persistent PowerShell execution policy.
- Use one `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` `REG_SZ` value named `SystemResourceMonitor`. Its data is exactly the quoted canonical absolute executable path without arguments. Never overwrite a pre-existing value that cannot be tied to the valid installed manifest. Do not alter Windows Startup Apps approval state; the user controls it through Windows Settings or Task Manager.
- Store an `install-manifest.json` in the application directory with the source commit and each installed payload file's relative path and SHA-256. Reject absolute, parent-traversal, duplicate, or escaping paths. Uninstall removes only unchanged manifest-listed files, the owned manifest, and empty directories created by the installer. Changed or unexpected files remain and are reported.
- Treat the current user's SID, `SYSTEM`, and built-in `Administrators` as trusted for write access. The application directory is owned by the current user. Reject reparse points and effective write access for other principals on the traversed installation subtree. A concurrent process running as the same user has the user's authority; B009 does not claim to defend against an actively malicious same-user process. Revalidate paths and access before each operation and abort on observed substitution.

## Alternatives

- Put executables next to `settings.json`: rejected because manifest-scoped removal becomes harder to distinguish from retained user data.
- Let callers choose any installation directory: rejected because path and ACL validation would have a wider trust boundary, and the Run entry would be less predictable.
- Use the Startup folder instead of `HKCU` Run: rejected because the existing B009 contract requires one owned `REG_SZ` Run value.
- Delete settings on uninstall or add a purge switch: rejected for this personal release because keeping the edge choice is the safer reversible behavior. A future purge option needs its own documented ownership and confirmation rules.
- Add an MSI/MSIX or machine-wide installer: outside B009 and inconsistent with its no-elevation scope.

## Consequences

The package includes separate scripts and a payload manifest in addition to the self-contained Desktop folder. The installed executable must have the same identity as the tested candidate. Sign-in startup can be delayed or disabled by Windows or the user; the acceptance run checks the Startup Apps state and observes an actual sign-in. Public package authenticity remains B016 work.
