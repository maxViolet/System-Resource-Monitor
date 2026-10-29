# B014 temperature-source qualification plan

Status: planned. This package executes [B014 — Qualify temperature sources](../B14-temperature-source.md). The [product-plan temperature rule](../../plan.md#temperatures), [independent later slices](../../implementation-plan.md#independent-later-slices), [source and lifecycle rules](../../implementation-plan.md#initial-model-and-workflows), [settings and lifecycle](../../implementation-plan.md#settings-and-lifecycle), [ADR 0012](../../adr/0012-discrete-gpu-selected-by-dxcore-and-correlated-by-luid.md), and [CONTEXT](../../../CONTEXT.md#language) govern the qualification. [B015](../B15-temperatures.md) owns collection and display.

## Outcome and boundary

Produce dated evidence for candidate CPU-package and selected-discrete-GPU temperature sources. Accept each sensor source that passes the B014 brief, or reject temperatures when none passes. An accepted result gives B015 supported hardware, sensor coverage, failure behavior, deployment inputs, and measured lifecycle characteristics. B014 does not add production collection, UI, simulated readings, or a privileged helper. It does not choose B015's polling interval, freshness deadline, plausible value bounds, display format, or accuracy tolerance; those product-level choices require an accepted plan amendment or ADR before B015 code.

## Work order

### 1. Confirm prerequisites and record the test context

- Require B008 to be `done` or `rejected` and B009 to be `done` in the [roadmap progress table](../README.md#progress), with their evidence linked. The B014 plan may be prepared before that gate; do not start candidate qualification or mark B014 `in progress` until it closes.
- Record the target Windows 11 x64 build, CPU model, every DXCore graphics candidate and its `InstanceLuid`, the B008 selection outcome, the B009 installed payload identity, and the ordinary-user account context. The B008 load-counter result does not change the discrete-only temperature rule.
- Record the source commit and the intended test-binary hashes before measurements. Keep names and paths in committed evidence sanitized under the [acceptance-report rules](../../implementation-plan.md#first-release-acceptance-gates).

**Gate:** both dependencies are closed in their required states and the hardware and artifact identities are known.

### 2. Inventory and screen candidates

- Inventory plausible CPU-package and GPU sensor sources separately. For each candidate record its exact version and binary provenance, license and redistribution terms for the intended use and packaging, supported hardware, sensor naming, driver or service requirements, required privileges, and files that B015 would need to deploy.
- Reject a candidate if its license forbids the intended packaging, it elevates the bar, it installs an unapproved privileged component, or its intended binaries trigger Windows Defender warnings. Record the reason rather than treating a rejected candidate as a fallback. An existing driver or service is not assumed safe merely because it is already installed; document its ownership and access requirements.
- Keep a candidate in the inventory when its feasibility is unknown. Mark the missing evidence `unverified` until the exact candidate can be tested; do not infer acceptance from documentation or a different binary version.

**Gate:** every candidate has a dated inventory row and a traceable screen result.

### 3. Prove sensor and adapter identity

- For CPU, establish that the reading is a whole-processor/package sensor, not one core, an estimate, or another thermal zone. Record the sensor identifier and the hardware on which it was verified.
- For GPU, apply ADR 0012's DXCore selection: no discrete adapter or multiple discrete adapters makes GPU temperature unavailable. With exactly one, prove that the reported sensor belongs to that selected adapter. Match a stable hardware identity to its DXCore `InstanceLuid` where the source exposes one; otherwise document an independently verifiable mapping. Adapter name, vendor, enumeration order, or temperature plausibility alone is insufficient. If the mapping cannot be proved, reject that GPU sensor.
- Test unsupported hardware, missing sensors, and ambiguous sensor identities. An accepted source must produce a trustworthy unavailable result in each case rather than a zero or guessed reading.

**Gate:** each accepted sensor has a reproducible identity proof and defined unavailable cases.

### 4. Measure execution and shutdown behavior

- On the target PC, record read durations and resource use at idle and under load, including blocking behavior, timeouts, owned handles or processes, and cleanup order. Report measurement counts, observation periods, and maxima; do not choose B015's polling or freshness contract from an unmeasured guess.
- Determine whether a source can genuinely block. A blocking source requires the child-process model in the [implementation plan](../../implementation-plan.md#initial-model-and-workflows): on timeout or shutdown, terminate its process tree and confirm exit within the remaining two-second total application shutdown deadline. If confirmed termination is not feasible, reject it. Do not dispose an in-use resource or claim `Task.Run` can cancel a blocked native read.
- Record the measured ownership model and a feasible bounded polling range for B015. If evidence cannot establish a safe timeout or cleanup path, mark that candidate unqualified.

**Gate:** blocking, timeout, shutdown, and resource ownership are measured, and each accepted source can meet the lifecycle boundary.

### 5. Verify exact binaries, packaging, and Defender

- Test as a standard user from the B009 per-user installed location, without elevating the bar. Record any installation step, UAC prompt, driver or service creation, and the final payload files.
- Identify every exact binary by version and SHA-256. Run Windows Defender checks on those binaries and their intended installed payload, recording Defender version, definition date, method, and result. A clean result for a different build does not qualify the intended build.
- Confirm that redistribution terms cover every file B015 would package. Leave the outcome unverified if a required term or binary cannot be checked; reject a candidate that triggers a warning or requires an unapproved privileged component.

**Gate:** each accepted source has licensing, no-elevation, packaging, and Defender evidence for its intended files.

### 6. Decide and hand off to B015

- Record an `accepted`, `rejected`, or `unverified` result for CPU-package and GPU sensors independently. B014 is `accepted` only when at least one sensor source passes every applicable criterion and all candidate outcomes are resolved; B015 then covers only the accepted sensors. If none passes, mark B014 `rejected` with evidence and leave B015 `not applicable` and unstarted.
- For each accepted source, hand off supported hardware and sensor coverage, the identity proof, unavailable and failure cases, measured read costs and blocking behavior, timeout and shutdown results, handle/process ownership, exact deployment files and license terms, and a reference tool or method for B015's later accuracy comparison. Record measurements that inform—but do not preselect—the B015 polling, freshness, bounds, display, and accuracy contract.
- Put sanitized, dated automated, Windows integration, and manual desktop evidence in **separate** reports under `docs/acceptance/`, as the [roadmap](../README.md#how-to-pick-up-work) requires. Mark checks that were not run `unverified`. Link each [B014 acceptance criterion](../B14-temperature-source.md#acceptance-criteria) to its evidence and update roadmap progress only after the applicable criteria close. A meaningful new design choice follows the ADR process in `AGENTS.md`.

**Gate:** the decision is supported by evidence for every applicable B014 criterion, and B015 has the inputs needed to define its separate behavior contract.
