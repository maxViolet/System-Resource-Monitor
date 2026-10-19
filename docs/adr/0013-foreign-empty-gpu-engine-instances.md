# Ignore structurally valid empty-engine instances from foreign GPU adapters

Status: accepted, 2026-10-19. Applies to B08 and refines [ADR 0012](0012-discrete-gpu-selected-by-dxcore-and-correlated-by-luid.md): an empty `engtype_` suffix is treated as parseable only when the full identity shape is valid, and it is ignored only for a foreign LUID. A selected-LUID empty suffix and every other unparseable identity remain `Uncorrelated`. Adapter enumeration and selection remain governed by ADR 0012.

## Context

On the target Windows 11 system, the GPU Engine counter returned 96 names with an empty `engtype_`. Their PID/LUID/physical/engine identity fields were structurally parseable and all belonged to a LUID other than the selected discrete NVIDIA adapter. Valid counted instances for the selected adapter were present. Treating these foreign records as unparseable made the whole GPU reading unavailable even though their adapter ownership could be established.

## Decision

Parse an empty engine name as `Unspecified` only when the complete PID, LUID, physical index, and engine index shape is valid. Ignore that instance if its LUID differs from the selected adapter. If its LUID matches the selected adapter, return `Uncorrelated`. Continue returning `Uncorrelated` for malformed identity fields, a selected-LUID non-zero physical index, and absence of a usable selected-adapter counted engine.

This change does not alter the discrete adapter selection rule, counted engine types, per-engine aggregation, or load calculation.

## Alternatives considered

- Treat every empty engine name as `Uncorrelated`: preserves the previous blanket rule but hides valid selected-adapter load due to identifiable records from another adapter.
- Ignore every empty engine name: rejected because a selected-adapter record without an engine type cannot be proven irrelevant to the displayed load.

## Consequences and validation

The core calculator needs tests for both foreign and selected LUID cases. The native correlation mode must show measured reads and no unparseable instances on the target system. Its resulting values still require comparison with Task Manager under the B08 acceptance thresholds; this decision alone does not establish accuracy or complete B08.
