# B09 — Per-user installation and autostart

| | |
|---|---|
| **Area** | Release |
| **Depends on** | B07 |
| **Unblocks** | B14; optionally B16 |
| **Sources** | [Product plan — Add installation and autostart](../plan.md#4-add-installation-and-autostart), [Implementation plan — Later slices](../implementation-plan.md#later-slices) |

## Why

The first personal release runs from an extracted folder. This independent track adds repeatable per-user installation and optional sign-in startup without changing the bar's no-elevation model.

## What done looks like

Install and uninstall scripts place and remove the self-contained app safely for the current user. An optional `HKCU` Run entry starts the canonical installed executable after sign-in and can be disabled independently. Upgrade preserves settings; uninstall follows an explicit, documented settings-retention choice.

## Context

The source plan requires a canonical destination and ACL, ownership, reparse-point, and quoting controls, but the current implementation plan does not define them. Before script implementation, amend the implementation plan or add an accepted ADR naming the exact per-user destination, trust boundary, ownership/ACL checks, and reparse-point handling. Then use that contract, quote command lines correctly, and avoid an executable path controlled by another user. Scripts may perform per-user setup; the bar itself never elevates.

Verification covers process state, payload files, registry values, settings, and work-area restoration. Exit the running bar safely before replacement or removal.

## Acceptance criteria

- [ ] The canonical destination and its path-security contract are accepted in the implementation plan or a new ADR before scripts are implemented
- [ ] Install from a clean profile produces only the documented per-user files and registry values without UAC
- [ ] The Run value contains the canonical, correctly quoted executable path and starts exactly one bar after sign-out/sign-in
- [ ] Autostart can be disabled without uninstalling the app
- [ ] Upgrade replaces the payload safely and preserves settings
- [ ] Uninstall stops the process, releases the AppBar, removes owned payload/registry state, and applies the documented settings-retention choice
- [ ] ACL, ownership, canonical-path, and reparse-point negative cases fail safely without modifying attacker-controlled locations
- [ ] Install, sign-out/in, disable, exit, upgrade, and uninstall evidence records process, files, registry, settings, and work-area state
- [ ] Affected B07 gates pass for the installed artifact

## Out of scope

Machine-wide installation, elevation of the bar, MSIX/MSI, automatic update, and public package provenance. B16 owns public distribution.
