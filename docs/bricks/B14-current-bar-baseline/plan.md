# B014 implementation plan for coding agents

Status: planned. This package executes [B014 — Record the current bar baseline](../B14-current-bar-baseline.md). Read its brief, the [shared bar design](../../plan.md#shared-bar-design), [current bar baseline](../../implementation-plan.md#current-bar-baseline), [first-release gates](../../implementation-plan.md#first-release-acceptance-gates), [handoff commands](../../implementation-plan.md#automated-verification-and-handoff), and [CONTEXT](../../../CONTEXT.md#language) before starting. B007 must be `done`.

## Outcome and boundary

Produce a measured, reproducible reference for the existing single-left-`StackPanel` WPF bar before B015 changes it. Do not implement the B015 layout, fix source composition, or claim that the [HTML prototype](../../../prototype/task-strip-design.html) proves geometry, accessibility, AppBar behavior, or resource budgets. B014 can close when every required observation has an honest outcome, each non-pass gate has a named open owner, and the baseline can be used by B015. A failed or unverified first-release gate remains open under that owner's contract.

## Work order

### 1. Freeze the artifacts and environment

- Check the roadmap's B007 status and record the source commit and clean working-tree state. B007 has no complete acceptance reports here, so this is a first measured baseline, not a rerun against historical B007 measurements.
- Build a clean, self-contained published Desktop folder and a separate Release test host from that commit. Record SHA-256 for each executable and every loaded production assembly. Keep test-host fixtures and injection hooks out of the published folder.
- Record Windows 11 build, hardware category, logical processor count, monitor dimensions, each DPI scale, primary-monitor assignment, taskbar setting, and enabled provider configuration. Check for an already running Desktop instance before lifecycle tests.

**Gate:** every observation can be traced to the right artifact and environment.

### 2. Measure the current WPF bar

- At the top and bottom edges and at 100%, 150%, and 200% scaling, measure the bar's physical-pixel rectangle, 24-DIP height, work-area reservation, and each visible section's device-pixel bounds. Convert monitor width to DIP for supported-width records. Measure the widest formatter candidates and record clipping or unsupported narrow widths.
- Cover CPU/RAM 0% and 100%, RAM used/total GiB tooltip, optional GPU, accepted-provider clients present and absent, short and weekly countdowns, and stale and unavailable values. Observe the actual provider-section behavior; do not treat an absent client as an unqualified limit window or mask a discrepancy with a fixture.
- Compare sanitized bar crops with the prototype's Current illustration. Record visual differences without adopting its hard-coded section widths, sample values, or label colors as WPF requirements.

**Gate:** B015 has measured section and monitor bounds for every required DPI and edge, plus an explicit list of discrepancies.

### 3. Run automated and Windows desktop checks

- Run targeted Desktop tests first, then locked restore, Release build, solution tests, self-contained publish, and `git diff --check` as applicable. In Herdr, run restore, build, test, and publish in the `qa` pane. Record commands and results; a build does not prove desktop behavior.
- On Windows 11 with the published app, observe pointer focus, tray position and Exit, normal work-area restoration, fullscreen enter/leave and Alt+Tab cycles, fullscreen on a second monitor, maximized windows, Explorer restart during fullscreen, DPI/display changes, and primary-monitor change. Use the separate test host only for injected failures and source timing.
- Run the applicable first-release gates with every accepted provider enabled. Include the 30-minute idle CPU and private-byte runs and the complete eight-hour memory-growth run, plus app-and-Codex-child peak samples where B011 is enabled. Preserve the 150-MiB combined limit; the prior B011 report's 175-MiB peak is a known failure, not a waiver. Mark interrupted or missing samples `unverified`.

**Gate:** each applicable gate names the tested artifact and has a measured `pass`, `fail`, or `unverified` result.

### 4. Record evidence and hand off

- Write `docs/acceptance/YYYY-MM-DD-B14-automated.md`, `YYYY-MM-DD-B14-windows-integration.md`, and `YYYY-MM-DD-B14-manual-desktop-resources.md`. Include aggregate results, sample counts, hashes, environment, observed discrepancies, and each gate outcome. Commit only sanitized data and redacted bar crops; keep raw traces and unredacted captures private.
- Link each [B014 acceptance criterion](../B14-current-bar-baseline.md#acceptance-criteria) to its evidence. Assign every failed or unverified first-release gate to a named open brick or follow-up task before setting B014 `done`; B011 remains the owner of its known combined-memory gap. If no existing owner fits, create and index a follow-up under the responsible brick through the documentation approval process, and keep B014 open until that owner exists. Give B015 the measured bounds, unsupported widths, provider-section discrepancy, and any visual differences to investigate. B015's own affected-gate pass criteria still apply.

**Gate:** the handoff distinguishes baseline evidence from B015 implementation acceptance and does not close B008, B011, or B013.
