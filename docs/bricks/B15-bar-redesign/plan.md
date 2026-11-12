# B015 implementation plan

Status: planned; implementation waits for B014 `done`. This package executes the [B015 brief](../B15-bar-redesign.md), [shared bar design](../../plan.md#shared-bar-design), [implementation-plan redesign section](../../implementation-plan.md#shared-bar-redesign), and [CONTEXT](../../../CONTEXT.md#language). [ADR 0026](../../adr/0026-post-release-brick-renumbering.md) defines the current brick IDs.

## Outcome and boundary

Replace the left-aligned WPF row with fixed left, flexible centre, and right-hand regions while retaining the current visible bar. Centre and right stay empty in the published app. Preserve section order, widths, formatting, tooltips, colors, 12-DIP Segoe UI, 24-DIP height, non-activating pointer behavior, native tray actions, and AppBar/fullscreen policy. B019 owns tasks and clock; B020 owns the in-bar tray and taskbar replacement. Metric acquisition, provider acquisition, Core boundaries, and acceptance thresholds do not change.

## 1. Establish the baseline and entry gate

Read the B014 [automated](../../acceptance/2026-11-12-B14-automated.md), [Windows integration](../../acceptance/2026-11-12-B14-windows-integration.md), and [manual/resource](../../acceptance/2026-11-12-B14-manual-desktop-resources.md) reports in full. Confirm B014 is `done` in the [roadmap](../README.md#progress) before changing production code. It is currently `in progress`; preparing this plan does not close it.

Use the recorded source commit and exact artifact hashes as the comparison identity. The baseline row ends at 754.5–755.3 DIP; widths below about 756 DIP are derived unsupported widths, not tested limits. Measure the widest supported states again for the implementation configuration, including each section's leading margin. Do not copy CSS widths from the prototype or preserve a known clipping defect as visual parity.

Carry forward the live 200%-to-125% DPI failure, missing desktop scenarios, and resource runs assigned to B015. The historical Codex combined-memory failure remains owned by B011 and is not waived. CPU/RAM accuracy remains with [B023](../B23-cpu-accuracy.md); its unresolved CPU-gate failure prevents B015 acceptance even though B023 is not an implementation-start dependency. Provider qualification remains with B010/B012; provider implementation acceptance remains with B011/B013. Baseline evidence does not pass any redesigned-artifact gate.

**Gate:** B014 is closed with linked evidence; baseline identities, supported widths, and open dependencies are explicit.

## 2. Add observable layout and DPI regression checks

Use the separate Desktop test host to create the real BarWindow HWND/AppBar. Add stable internal section/host accessors where needed instead of relying on the outer StackPanel shape. Keep injected content and diagnostic entry points in the test-host project.

Before changing layout, demonstrate a failing check for missing regions. Populate centre/right hosts with oversized test content and measure left-section positions and widths before and after: they must not move, resize, overlap with the test content, or lose tooltip access. Exercise both edges and 100%, 150%, and 200% DPI; include the baseline's native 125% scale.

Reproduce the recorded live 200%-to-125% DPI defect separately from fresh-start geometry. Observe HWND DPI, WPF scale, section text/bounds, AppBar reservation, and work-area restoration. Diagnose the notification and reconciliation path before choosing the fix; a restart is not a fix. Add regression coverage at the responsible seam and retain an actual Windows live-change acceptance scenario.

**Gate:** checks expose the current layout and live-DPI failures with artifact-specific evidence.

## 3. Implement regions and correct blocking presentation defects

Use a Grid with `Auto / * / Auto` columns, retaining existing controls and spacing in the left region. Keep empty centre/right hosts non-focusable and without new pointer surfaces. Constrain test content to its assigned region without allowing it to obscure fixed sections. Recheck supported-width calculations before accepting narrow displays; an unresolved width conflict requires the documentation/ADR process rather than silent clipping.

Correct the live-DPI defect assigned to B015 using the existing [AppBar and shell rules](../../implementation-plan.md#appbar-and-shell-behaviour): renegotiate against current primary-monitor geometry and DPI without activation, restore the previous work area, and reconcile fullscreen state. Preserve registration during fullscreen. Do not preselect a native-message workaround without reproducing the cause.

Recheck provider omission in production composition. Qualification and client availability are different: an unqualified provider has no section; a qualified provider with a missing client retains its documented unavailable section. Provider-omission correction belongs to separate FIX work, preserving this brick's display-semantics boundary. Before implementation starts, assign that work a concrete FIX ID and link its tracked task here; land it before the final artifact build. Use a test-host-only configuration seam through the production composition path to exercise Codex unqualified, Claude unqualified, and both unqualified. Do not hide controls only in the test host, or infer qualification from a failed read or missing executable. Both providers are currently qualified, so those synthetic combinations are test-host evidence, not published-app observations. The published app must retain both sections when their clients are missing or reads fail.

**Gate:** region checks pass; left sections match the supported baseline states; live DPI updates correctly; no unresolved provider-omission criterion is hidden.

## 4. Verify the final artifacts

Run targeted Desktop layout, metric-format, and provider-width tests first, plus affected real-HWND test-host modes. Avoid tests that only mirror visual-tree structure. Run the exact restore/build/test/publish commands in [Automated verification and handoff](../../implementation-plan.md#automated-verification-and-handoff), then `git diff --check`. Use Herdr's QA pane when required by HERDR.md.

Build the self-contained published app and separate Release test host from one final commit, recording executable and loaded-production-assembly SHA-256 values. Any later fix invalidates affected artifact evidence and requires rebuilding and rerunning its gates.

On Windows 11, verify both edges, fresh starts at 100/125/150/200%, live scale changes in both directions, supported widths, 0%/100%, active countdowns, stale/unavailable values, RAM tooltip, GPU, and qualified-provider available/unavailable states. Run synthetic unqualified-provider combinations in the separate test host as specified in step 3. Record cropped, redacted bar images and measured bounds. Exercise the complete product-plan stage 1 and Desktop behaviour gate: start/exit, handled startup failure in the test host, repeated edge changes, resolution changes, auto-hidden taskbar, ordinary maximized windows, pointer focus, tray position/Exit, Explorer restart normally and during fullscreen, primary-monitor changes, and restored work area. Run ten enter/leave cycles per required video, exclusive-game, and borderless-game mode, including Alt+Tab; separately test second-monitor fullscreen. Keep unavailable hardware/scenarios `unverified`.

**Gate:** automated, real-HWND, and published-app observations independently satisfy their applicable criteria, including live DPI recovery.

## 5. Measure resources and close acceptance

Apply the [first-release acceptance gates](../../implementation-plan.md#first-release-acceptance-gates) to the exact redesigned published artifact with accepted providers enabled. Measure 30-minute idle CPU cost (at most 0.5%, whole-machine normalized), private bytes (at most 150 MiB), and memory growth after a 30-minute warm-up over a complete eight-hour run (last-minus-first 30-minute means at most 10 MiB). Sample app private bytes every two seconds. Sample the app plus Codex-child private bytes at least every 100 ms during the idle run and every poll in the eight-hour run, from child start through confirmed exit; the combined ceiling is 150 MiB. Record maxima, counts, configuration, and interruptions. Incomplete runs remain unverified.

Require the B011 owner to resolve any combined-memory failure that blocks B015 acceptance; layout parity does not waive it. Require the B023 owner to resolve any CPU-accuracy gate failure before B015 is marked done. An unverified result is not passing evidence for a required gate. Recheck normal/fault shutdown and published-artifact hygiene affected by the work. No test host, placeholders, fixture, or diagnostic entry point may ship. Keep unrelated owners' acceptance open rather than claiming it from these checks.

Write separate dated reports under `docs/acceptance/`: `YYYY-MM-DD-B15-automated.md`, `YYYY-MM-DD-B15-windows-integration.md`, and `YYYY-MM-DD-B15-manual-desktop-resources.md`. Map every brief criterion to evidence, with artifact identities, sanitized aggregate measurements, and `pass`, `fail`, or `unverified`. Keep raw account output, user paths, window titles, uncropped desktop captures, and precise activity timelines out of the repository.

**Gate:** mark B015 `done` only after every applicable brief criterion passes. Otherwise retain its open status and name the failing or unverified gate and owner. Implementation commits require the repository's review workflow; this documentation plan does not claim any implementation or runtime validation.
