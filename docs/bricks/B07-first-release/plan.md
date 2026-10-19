# B007 implementation plan for coding agents

Status: planned. This package implements [B007 — First-release evidence and self-contained publication](../B07-first-release.md). The [product-plan release rule](../../plan.md#release-rule), [slice 3](../../implementation-plan.md#slice-3-first-personal-release), [first-release acceptance gates](../../implementation-plan.md#first-release-acceptance-gates), [automated verification and handoff](../../implementation-plan.md#automated-verification-and-handoff), [ADR 0004](../../adr/0004-subscription-limits-in-first-release.md), and [ADR 0011](../../adr/0011-exit-when-a-collector-run-faults.md) govern scope and acceptance. Use the commands in the implementation plan; [ADR 0005](../../adr/0005-project-level-runtime-and-testing-platform.md) explains their runtime and test-runner flags.

## Objective and boundary

Produce a clean, self-contained Windows 11 x64 folder for the first personal release and reproducible evidence for every release-blocking gate. Build the published Desktop app and separate Release test host from one source commit. Test the published app with every accepted provider enabled; a rejected provider has no section or placeholder. The test host exercises injected faults through the production lifecycle components and is never included in the published folder.

This brick owns release evidence, publication, artifact hygiene, and user instructions. It may add the smallest test-host-only fault mode needed to verify ADR 0011 on a real desktop; the published app gains no fault switch. It does not add GPU, installation or autostart, temperatures, task strip, public distribution, or a placement fallback. Do not change a shared threshold or release rule in this package plan; a change to documented behavior requires the documentation and ADR process in `AGENTS.md`.

## Ordered work

### 1. Close the prerequisite ledger

- Require B006 `done` and inspect its [acceptance report](../../acceptance/2026-10-15-B06-cpu-ram-presentation.md). Its waived visual-expiry check becomes the visual observation in step 3. Its waived 150%/200% scaling and unavailable (`—`) tooltip checks become step 5 gates; `done` does not count them as passed for B007.
- B004 is `done` by the user decision recorded in the [B006 report](../../acceptance/2026-10-15-B06-cpu-ram-presentation.md#prerequisite), while its own [report](../../acceptance/2026-10-15-B04-fullscreen-reconciliation.md) leaves named desktop scenarios unverified. Step 5 reruns those scenarios on the B007 published artifact. Passing step 5 closes them for B007 without requiring a separate B004 report update; an unrun or failed scenario still blocks B007. Inspect the B002–B003 regression scenarios named by B004 as part of that step.
- Require a dated closeout for both B010 and B011. For each accepted qualification, require the corresponding B012 or B013 implementation and its own acceptance evidence. For each rejected qualification, require the implementation brick to be `not applicable` and verify that the published bar has no section or placeholder for it.
- Record the exact source commit and working-tree status before making artifacts. On the target Windows 11 x64 PC, record the Windows build, hardware category, logical processor count, display topology, DPI, Windows taskbar configuration, and measurement tools. The test host is framework-dependent: record the version and location of the .NET 10 Desktop runtime it uses, including a session-local runtime if needed. That runtime is not evidence about the published app's self-contained behavior. Record the provider configuration without account output or credentials.
- Before scheduling desktop gates, arrange two physical monitors with a switchable primary, 100%/150%/200% scaling, an auto-hidden taskbar, fullscreen video, an exclusive game, a borderless game, CPU/RAM load tools, and official clients with authorized accounts for each accepted provider and a safe way to test access loss. Record any unavailable setup early; run independent gates while it is obtained, but do not mark B007 `done` until every required scenario passes.

**Gate:** every prerequisite has an explicit evidence link and state, and missing equipment is tracked before measurement begins. B004's open scenarios are carried into step 5; they are not a second prerequisite report.

### 2. Build, test, and identify the two artifacts

Run targeted Release tests for any test project changed during B007, then run the repository handoff commands from the root:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

- Build the separate `SystemResourceMonitor.Desktop.TestHost` Release executable from the same commit. Record the commands, exit codes, source commit, and SHA-256 of both executables and every production assembly loaded by each. Record the publish folder file list and hashes needed to bind later observations to the tested files.
- The solution build includes the test host. Query `dotnet msbuild tests/SystemResourceMonitor.Desktop.TestHost/SystemResourceMonitor.Desktop.TestHost.csproj -getProperty:TargetDir -getProperty:AssemblyName -p:Configuration=Release`, resolve `<TargetDir><AssemblyName>.exe`, and verify that file exists before hashing it. `TargetPath` names the DLL, not the executable. Keep the resolved executable path in the private run log and its hash in the sanitized report; record the test-host DLL hash separately from the loaded production-assembly hashes.
- Write the user instructions in the root `README.md` and copy them into the published folder before freezing its file list and hashes. The copied guide is allowed release material. Run all published-app gates on that final folder; do not add files to it afterwards without re-identifying the artifact.
- Keep injection code, fixtures, secrets, development configuration, and diagnostic hooks out of the published folder. Do not copy the test host into it.
- If implementation changes after a gate is measured, rebuild and identify the new artifact, then rerun every affected gate. Evidence for an older hash does not accept a new artifact.

**Gate:** locked restore, build, tests, self-contained publish, and `git diff --check` pass; both artifacts and their shared source commit are identifiable.

### 3. Exercise test-host and fault gates on the target PC

- Run the existing test-host `--native-reads 1800` mode with the production CPU and RAM native wrappers at normal two-second polling. Timestamp every call. Neither reader may have two consecutive calls longer than two seconds.
- Run `--metric-freshness cpu` and `--metric-freshness ram`. A blocked reading must expire at six seconds while the other keeps updating; the test host checks the rendered transition within 0.5 seconds of the deadline. Also observe the `—` transition on screen, closing B006's waived visual check. Keep the deterministic fake-time exact-boundary test distinct from this real-desktop run.
- For each accepted provider, use fake time to verify that sign-in failure becomes unavailable on the first failed poll; network-stale data becomes `--/--` at three minutes since last success without a new poll; a blocked or timed-out provider cannot delay CPU/RAM or the other provider; and its child process tree ends within the shutdown deadline. An ordinary provider source failure must yield `Unavailable`, leave the bar and other collectors running, and produce no `owner-failed` marker or failure exit, as ADR 0011 requires.
- Run `--settings-read-failure`, `--settings-backup-failure`, and `--settings-write-failure` while CPU/RAM monitoring is active. Monitoring continues. On the real desktop, visually confirm the restart-only persistence message in the write-failure mode while Position and Exit remain usable; the recorded marker alone does not prove visibility.
- Run the existing `--fail-after-setpos` handled-startup-failure mode and `--bounded-shutdown` mode. Verify prior work area is restored before collector wait, the total wait is at most two seconds, pending work is recorded, late results cannot publish, no in-use resource is disposed, and the process exits without leftovers.
- Add a dedicated `--faulted-metric-run` test-host mode through the production composition seam, without a published-app switch, and cover it in `LifecycleProcessTests`. On a real desktop with a registered AppBar, fault the metric monitor after startup. Require `owner-failed:metric-monitor:<exception type>`, the failure exit code, `ABM_REMOVE`, baseline work area restored before collector wait, and no leftover process. Deterministic `ShutdownCoordinatorTests` alone do not close ADR 0011's desktop gate.

**Gate:** record each scenario, its observations, and pass/fail against the test-host executable and loaded-assembly hashes. Deterministic tests alone do not close Windows integration or published-app gates.

### 4. Measure published-app accuracy and provider behavior

- At idle and sustained CPU load separately, collect 30 paired observations at two-second intervals against Task Manager in the same 60-second window. The difference between window means must be at most five percentage points in each regime; record transition spikes separately.
- At idle and sustained RAM load separately, collect 30 paired observations per regime against Task Manager physical memory usage. At least 29 of 30 observations in each regime must differ by at most two percentage points. Record used and total bytes.
- For each accepted provider, compare every displayed remaining limit and reset time with the official client in the same verification window, using its verified rounding rule. Exercise access loss and verify `--/--` plus a safe tooltip explanation. Repeat ten polls with no other work and verify no limit change or new session. Record sanitized comparisons and client version, never raw account output.

**Gate:** CPU accuracy, RAM accuracy, and every accepted provider's manual account checks pass on the published app. A rejected provider has no visible section.

### 5. Repeat desktop, DPI, and shutdown gates on the published app

- Repeat every published-app [placement scenario](../../plan.md#1-validate-placement-first) and the [B004 acceptance scenarios](../B04-placement-gate.md#acceptance-criteria) on actual Windows 11: start and exit, repeated top/bottom changes, Explorer restart, resolution and DPI changes, primary-monitor change, auto-hidden taskbar, focus, and stale-reservation checks. Verify the recovered tray icon's position and Exit actions. The injected handled-startup-failure check belongs to the test host in step 3.
- Run ten enter/leave cycles each for fullscreen video, an exclusive game, and a borderless game, including Alt+Tab to a normal window and back. Verify the bar lowers only for active fullscreen on the primary monitor, registration and reservation remain unchanged, restoration occurs within one second without focus stealing, and the prior work area returns on exit. Separately test Explorer restart during fullscreen, fullscreen on another monitor, and an ordinary maximized window.
- At 100%, 150%, and 200% scaling, verify the reserved strip matches the rendered bar. CPU/RAM and accepted-provider sections remain fixed and unclipped for 0%, 100%, and unavailable states; the RAM tooltip shows used/total GiB. Hover over an unavailable (`—`) CPU or RAM section and verify its explanatory tooltip without activating the bar. These checks close B006's waived scaling and unavailable-tooltip items for B007.
- Use normal Exit to verify work area restoration occurs before collector wait and the process leaves no background child or reservation.
- Observe the published app throughout its desktop and resource runs. Check that readings continue to update and the bar's position, tooltips, and Exit action remain responsive; record observations against the published executable and loaded-assembly hashes.

**Gate:** record direct published-app desktop evidence for every scenario. Passing the previously unverified B004 scenarios here closes them for B007 on this artifact; earlier B004 evidence alone does not.

### 6. Measure resource budgets without gaps

- On the published Release app at idle with every accepted provider enabled, measure process CPU time across 30 minutes. Compute `(process CPU time delta / elapsed wall time / logical processor count) × 100`; pass only at `<= 0.5%`. Sample private bytes every two seconds; every sample must be `<= 150 MiB`.
- After a 30-minute warm-up, keep normal monitoring and every accepted provider enabled for a complete eight-hour observation. Sample private bytes every two seconds. Every sample must be `<= 150 MiB`; the last 30-minute mean minus the first 30-minute mean must be `<= 10 MiB`.
- Record sample counts, instrument, aggregate values, interruptions, and workload changes. A missing sample or interrupted run invalidates the complete-run result and must be repeated. Report the growth calculation without claiming it proves absence of all leaks.

**Gate:** all three resource budgets pass on the same identified published-app configuration.

### 7. Check artifact hygiene and write user instructions

- Copy the same hashed published folder to the target Windows 11 x64 PC used in steps 3–6. Verify extraction and launch without elevation or provider authorization; in the unauthorized state, accepted-provider sections show unavailable safely. Record whether a .NET runtime is installed there. If it is, verify that the running published app loads its runtime binaries from its own folder, so the installed runtime is not needed by this run; report the observation without calling it a clean-profile test. Do not use the framework-dependent test host as evidence that the published app is self-contained.
- Compare the folder contents with the prohibited-material list: no test host, injection code, fixtures, secrets, development configuration, or production diagnostic hook. Inspect the published entry point for a reachable fault switch. Record all existing `InternalsVisibleTo` friend declarations, including `SystemResourceMonitor.Desktop.Tests` and `SystemResourceMonitor.Desktop.TestHost`: metadata alone is not a runtime diagnostic hook, provided no test assembly is shipped and no production entry point selects injection modes. Verify the app still offers only first-release behavior.
- Open the `README.md` copied into the published folder and follow its extraction, launch, top/bottom position, Exit, settings-error, and unavailable-reading instructions. Confirm it names only the provider sections actually present in the artifact.

**Gate:** target-PC launch, self-contained runtime evidence, and artifact inspection pass, and the instructions reproduce the tested first-release workflow. The clean-profile check remains B016's public-distribution gate.

### 8. Record evidence and decide acceptance

- As the [roadmap](../README.md#how-to-pick-up-work) explicitly requires, create separate dated automated, Windows integration, and manual desktop/resource reports under `docs/acceptance/`. B007 deliberately keeps these evidence classes in separate reports because the test host, published app, and eight-hour measurement have different artifact and observation records; link them from a short B007 gate index. Each report identifies the source commit, relevant executable and loaded-production-assembly hashes, target hardware category, Windows build, logical processor count, display/DPI/taskbar setup, instrumentation, sample counts, aggregate observations, and pass/fail. Mark every unavailable or unrun gate `unverified`.
- Commit only sanitized reports. Keep raw traces privately only as long as needed for diagnosis. Never put usernames, absolute user paths, account or provider output, process or window titles, environment dumps, precise activity timelines, or unredacted screenshots in the repository.
- Update the [roadmap progress table](../README.md#progress) to `done` only after every B007 criterion and release-blocking gate passes on its required artifact. Otherwise leave B007 open and name the failing or unverified gate. B008, B009, B016, and B017 are unblocked only by B007 `done` evidence.

**Gate:** each acceptance criterion in the [B007 brief](../B07-first-release.md#acceptance-criteria) points to a passing result, and no report treats automated checks as proof of manual Windows behavior.
