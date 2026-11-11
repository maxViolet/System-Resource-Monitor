# B014 implementation plan for coding agents

Status: planned. This package implements [B014 — Implement the shared bar redesign](../B14-bar-redesign.md). The [shared bar design](../../plan.md#shared-bar-design), [implementation-plan redesign section](../../implementation-plan.md#shared-bar-redesign), [first-release acceptance gates](../../implementation-plan.md#first-release-acceptance-gates), [automated verification and handoff](../../implementation-plan.md#automated-verification-and-handoff), and [CONTEXT](../../../CONTEXT.md#language) govern the work. B007 must be `done` before implementation starts.

## Outcome and boundary

Replace the current single left-aligned WPF `StackPanel` with fixed left, flexible centre, and right-hand regions. The published bar retains its current visible state: the left holds the existing system metric and qualified-limit sections, while the centre and right hosts remain empty. B018 adds the task strip and clock; B019 adds the in-bar tray and shell indicators. Do not start those behaviours, alter section display semantics or formatting, or change Core, provider acquisition, AppBar ownership, fullscreen policy, or the first-release thresholds in B014.

The [HTML prototype](../../../prototype/task-strip-design.html) is a visual reference, not WPF geometry or Windows acceptance evidence. Measured pre-change WPF bounds and screenshots are the regression baseline. A section that is wider, shifted, clipped, or newly visible requires investigation; do not treat the prototype's simulated widths as proof.

## Pre-existing provider-section gap

At plan time, `BarWindow` creates both provider sections even when a provider source is absent, and `DesktopStartup` merely skips starting its monitor. This conflicts with B014's criterion that an unqualified provider has no section. Record the baseline behaviour and track its correction as separate `FIX` work because B014 excludes changes to section display semantics. A test-host configuration with a null Codex source, a null Claude source, and both null must verify omission after that fix. Land the separate fix before the final B014 artifact build and gate runs in steps 4–5; if it lands later, rebuild both artifacts and rerun those gates on the post-fix commit. Do not mark B014 `done` while this criterion fails; do not conceal it with a B014-only placeholder or report the production configuration with both providers enabled as proof of omission.

## Work order

### 1. Establish the baseline and supported widths

- Confirm B007 is `done` in the [roadmap](../README.md#progress). Its historical acceptance reports are absent, so create a B014 baseline rather than assuming measurements from B007. Record the pre-change source commit, Windows build, monitor dimensions, DPI, and taskbar configuration.
- Build a clean self-contained published app and separate Release test host from that commit. Record SHA-256 hashes of both executables and the loaded production assemblies. Use the published app for visual, desktop, and resource observations and the test host for controlled states.
- At 100%, 150%, and 200% scaling, measure each visible left section's device-pixel bounds and the bar's 24-DIP height at the top and bottom edges. Cover 0%, 100%, active countdown, stale and unavailable states, qualified and unqualified monthly states where applicable, and the RAM used/total GiB tooltip. Note that the current test-host null-provider configuration exposes the known omission gap. Keep uncropped baseline captures private; put cropped, redacted bar-strip screenshots in the dated report, without window titles or desktop content.
- Calculate the required fixed-left width from the measured widest candidates plus each visible section's 8-DIP leading margin. For each target monitor and tested DPI, record its device-pixel width and DIP width (device pixels divided by scale), and label the width supported or unsupported against that fixed-left requirement. If a target width cannot hold the fixed sections, record the limit and resolve the design through the documentation and ADR process before any clipping or contract change. Record narrower unsupported widths explicitly.

**Gate:** the baseline identifies its exact artifacts and supported widths, and no unresolved width conflict is hidden by the planned layout.

### 2. Add a real-WPF region check before changing the layout

- Add a test-host mode that creates the real `BarWindow` HWND/AppBar and checks for three regions. It should fail against the current single-`StackPanel` layout before implementation.
- In that mode, fill the centre and right hosts with test-host-only content wider than their free space. At each supported DPI and both edges, measure every left section's `ActualWidth` and device-pixel position before and after filling the hosts. The fixed left sections must neither move nor resize; no region may create a new focus or tooltip surface.
- Replace the positional `StackPanel` patterns in `Desktop.TestHost/LifecycleRun.Sections` and `ProviderSections` with stable internal section accessors. The production assembly may expose internal references to the region hosts to its existing friend test-host assembly, but must expose no production command or diagnostic injection mode.

**Gate:** the new real-HWND check fails for the missing regions before the production change and can observe the left sections without depending on the outer visual-tree shape.

### 3. Implement the three-region layout

- Use a WPF `Grid` with `Auto / * / Auto` columns. Keep the existing section controls, widths, order, styling, tooltips, and left-side spacing inside the left region. Leave the centre and right hosts empty in the published app, with no focusable or hit-test surface added by an empty host.
- Preserve the bar's 24-DIP height, no-activation pointer behaviour, native tray position and Exit actions, AppBar reservation, fullscreen transitions, Explorer recovery, and edge switching. The flexible centre absorbs remaining width; the preflight width gate prevents an unsupported narrow target from silently clipping fixed sections.
- Keep placeholder creation and types inside `SystemResourceMonitor.Desktop.TestHost`. Check the published folder and loaded assemblies for absence of test-host code and fixtures.

**Gate:** the test-host region check passes, the published bar has empty centre and right hosts, and its left-section device-pixel bounds are identical to the baseline at each tested DPI and edge. A difference fails this gate until corrected in code or resolved through the documentation and ADR process.

### 4. Verify code and the published artifact

- Run targeted Desktop layout and width tests first, including `NativeLayoutTests`, `MetricSectionFormatTests`, `CodexSectionWidthTests`, and `ClaudeSectionWidthTests`, plus the new real-HWND test-host mode. Rerun existing test-host metric and provider display modes affected by the changed tree. Do not add unit tests that merely assert XAML structure or forward properties.
- Run locked restore, Release build, solution tests, self-contained Desktop publish, and `git diff --check` using the [handoff commands](../../implementation-plan.md#automated-verification-and-handoff). In Herdr, run restore, build, test, and publish in the `qa` pane as `HERDR.md` requires.
- On Windows 11, compare the exact published artifact with the recorded baseline at 100%, 150%, and 200% scaling and both edges. Check 0%, 100%, active countdown, stale and unavailable states, qualified and unqualified monthly states where applicable, the RAM used/total GiB tooltip, optional GPU, qualified and unqualified provider combinations, section text and tooltips, 24-DIP reservation, pointer focus, native tray actions, fullscreen cycles, Explorer restart, and primary-monitor changes. The separate provider-omission fix must be present before the unqualified-provider criterion can pass.

**Gate:** automated results and real-desktop observations are recorded separately; a passing build or prototype comparison does not close a Windows scenario.

### 5. Rerun affected first-release resource and artifact gates

- On the exact published artifact with the production-enabled provider configuration, including Codex and Claude, repeat the 30-minute idle app CPU-cost gate (`<= 0.5%` whole-machine-normalized), the private-byte ceiling (`<= 150 MiB` in the 30-minute and eight-hour runs), and the memory-growth gate (after a 30-minute warm-up, last-window minus first-window mean `<= 10 MiB` across a complete eight hours). Record the enabled configuration.
- With Codex enabled, sample app and Codex-child private bytes at least every 100 ms throughout the 30-minute idle run and at every Codex poll in the eight-hour run. Count each child from start through confirmed exit; their sum must be `<= 150 MiB` at every sample. Record the maximum, sample count, and interruptions; an incomplete run is unverified.
- Repeat the affected DPI/rendering, desktop-behaviour, and artifact-hygiene gates from the [first-release acceptance table](../../implementation-plan.md#first-release-acceptance-gates). The published folder must contain no test host, placeholder fixture, development configuration, or reachable diagnostic hook.

**Gate:** every affected B007 rendering, desktop, CPU, and memory gate passes on its required artifact; the test-host placeholder check does not count as published-app evidence.

### 6. Record evidence and decide acceptance

- Write separate dated reports under `docs/acceptance/`: `YYYY-MM-DD-B14-automated.md`, `YYYY-MM-DD-B14-windows-integration.md`, and `YYYY-MM-DD-B14-manual-desktop-resources.md`. Record source commit, relevant executable and loaded-assembly SHA-256 hashes, hardware category, Windows build, DPI/monitor setup, aggregate measurements, sample counts, and pass/fail or `unverified` for each gate.
- Commit only sanitized observations and cropped, redacted bar-strip screenshots that contain no window titles or desktop content. Keep raw traces and uncropped captures private; do not commit unredacted screenshots, usernames, absolute user paths, account output, window titles, or precise activity timelines.
- When working in Herdr, request a `reviewer` before committing implementation changes to `src/` or `tests/`. Update the roadmap status to `done` only after every [B014 acceptance criterion](../B14-bar-redesign.md#acceptance-criteria) has passing evidence, including the separately corrected provider-omission criterion. Otherwise keep B014 open and name each failing or unverified gate.

**Gate:** the evidence distinguishes published app, test host, automated checks, and manual Windows observations, with no open acceptance criterion represented as passed.
