# B014 — Qualify temperature sources

| | |
|---|---|
| **Area** | Research |
| **Depends on** | B008 done/rejected; B009 done |
| **Unblocks** | B015 when accepted |
| **Sources** | [Product plan — Temperatures](../plan.md#temperatures), [Implementation plan — Independent later slices](../implementation-plan.md#independent-later-slices), [ADR 0012](../adr/0012-discrete-gpu-selected-by-dxcore-and-correlated-by-luid.md), [CONTEXT — Language](../../CONTEXT.md#language) |

## Why

Hardware temperatures often require third-party libraries, drivers, privileges, or behavior that triggers security products. The plan allows no elevation of the bar and accepts no temperature feature when no safe source exists.

## What done looks like

A dated report evaluates candidate CPU-package and GPU temperature sources and either accepts each trustworthy sensor source or rejects the feature with evidence when none pass. The report is complete enough to define B015's supported hardware, sensor coverage, failure behavior, and deployment inputs.

## Context

For each candidate record source and version, license and redistribution terms, supported hardware, driver or service requirements, required privileges, handle/resource ownership, polling/blocking behavior, and Windows Defender results. Test the exact binaries intended for use.

A privileged helper is not authorized. If a candidate requires elevation of the bar, installs an unapproved privileged component, or triggers antivirus warnings, reject it. Absence of temperature readings is preferable to weakening the privilege or security boundary.

## Acceptance criteria

- [ ] Candidate inventory and dated evidence cover every context field above
- [ ] CPU evidence, when available, uses a whole-processor/package sensor rather than a single core; GPU evidence, when available, identifies the one discrete adapter under the product plan's discrete-only rule even if B008 rejected load-counter correlation
- [ ] License and redistribution terms permit the intended use and packaging
- [ ] The exact candidate binaries pass Windows Defender checks and require no elevation of the bar
- [ ] Blocking, timeout, shutdown, and resource-ownership characteristics are measured and recorded
- [ ] An accepted source defines supported hardware and a trustworthy unavailable result for unsupported or ambiguous sensors
- [ ] If no source passes, the report marks temperatures rejected, B015 is not started, and no simulated readings or privileged helper are added

## Out of scope

Implementing collection or showing readings belongs to B015. Designing or shipping a privileged helper requires a separate future decision and is not part of this roadmap.
