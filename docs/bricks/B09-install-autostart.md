# B009 — Per-user installation and autostart

| | |
|---|---|
| **Area** | Release |
| **Depends on** | B007 |
| **Unblocks** | B014; optionally B016 |
| **Sources** | [Product plan — Installation and autostart](../plan.md#installation-and-autostart), [Implementation plan — Independent later slices](../implementation-plan.md#independent-later-slices), [ADR 0014](../adr/0014-per-user-installation-and-autostart.md) |

## Why

The first personal release runs from an extracted folder. This independent track adds repeatable per-user installation and optional sign-in startup without changing the bar's no-elevation model.

## What done looks like

Install and uninstall scripts place and remove the self-contained app safely for the current user. An optional `HKCU` Run entry starts the canonical installed executable after sign-in and can be disabled independently. Upgrade preserves settings; uninstall follows an explicit, documented settings-retention choice.

## Context

Apply the [Installation and autostart](../implementation-plan.md#installation-and-autostart) controls and [ADR 0014](../adr/0014-per-user-installation-and-autostart.md): the fixed per-user destination, ACL and reparse-point checks, one owned quoted `REG_SZ` Run value, and manifest-scoped uninstall. The destination is recorded before script implementation. Scripts may perform per-user setup; the bar itself never elevates.

Verification covers process state, payload files, registry values, settings, and work-area restoration. Exit the running bar safely before replacement or removal.

## Acceptance criteria

- [ ] The exact canonical per-user destination is recorded in the implementation plan before scripts are implemented
- [ ] Install from a clean profile produces only the documented per-user files and registry values without UAC
- [ ] The Run value contains the canonical, correctly quoted executable path and starts exactly one bar after sign-out/sign-in
- [ ] Autostart can be disabled without uninstalling the app
- [ ] Upgrade replaces the payload safely and preserves settings
- [ ] Uninstall stops the process, releases the AppBar, removes owned payload/registry state, and applies the documented settings-retention choice
- [ ] ACL, ownership, canonical-path, and reparse-point negative cases fail safely without modifying attacker-controlled locations
- [ ] Install, sign-out/in, disable, exit, upgrade, and uninstall evidence records process, files, registry, settings, and work-area state
- [ ] Affected B007 gates pass for the installed artifact

## Out of scope

Machine-wide installation, elevation of the bar, MSIX/MSI, automatic update, and public package provenance. B016 owns public distribution.
