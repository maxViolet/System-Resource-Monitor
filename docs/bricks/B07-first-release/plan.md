# B07 implementation plan for coding agents

Status: planned. This package implements [B07 — First-release evidence and self-contained publication](../B07-first-release.md). The [product-plan release rule](../../plan.md#release-rule), [slice 3](../../implementation-plan.md#slice-3-first-personal-release), [first-release acceptance gates](../../implementation-plan.md#first-release-acceptance-gates), [automated verification and handoff](../../implementation-plan.md#automated-verification-and-handoff), and [ADR 0004](../../adr/0004-subscription-limits-in-first-release.md) govern scope and acceptance. Use the commands in the implementation plan; [ADR 0005](../../adr/0005-project-level-runtime-and-testing-platform.md) explains their runtime and test-runner flags.

## Objective and boundary

Produce a clean, self-contained Windows 11 x64 folder for the first personal release and reproducible evidence for every release-blocking gate. Build the published Desktop app and separate Release test host from one source commit. Test the published app with every accepted provider enabled; a rejected provider has no section or placeholder. The test host exercises injected faults through the production lifecycle components and is never included in the published folder.

This brick owns release evidence, publication, artifact hygiene, and user instructions. It does not add GPU, installation or autostart, temperatures, task strip, public distribution, or a placement fallback. Do not change a shared threshold or release rule in this package plan; a change to documented behavior requires the documentation and ADR process in `AGENTS.md`.

## Ordered work

### 1. Close the prerequisite ledger

- Require B06's accepted CPU/RAM presentation and monitoring handoff. Inspect the B04 desktop report as the placement baseline. An unverified B04 scenario stays open and cannot count as passing at B07.
- Require a dated closeout for both B10 and B11. For each accepted qualification, require the corresponding B12 or B13 implementation and its own acceptance evidence. For each rejected qualification, require the implementation brick to be `not applicable` and verify that the published bar has no section or placeholder for it.
- Record the exact source commit and working-tree status before making artifacts. Identify the Windows 11 x64 measurement PC for the accuracy, resource, and desktop gates; record its Windows build, hardware category, logical processor count, display topology, DPI, Windows taskbar configuration, and measurement tools. Record the provider configuration without account output or credentials.

**Gate:** every prerequisite has an explicit evidence link and state. Do not mark B07 accepted while a prerequisite or B04 desktop scenario is open.

### 2. Build, test, and identify the two artifacts

Run targeted Release tests for any test project changed during B07, then run the repository handoff commands from the root:

```powershell
dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode
dotnet build SystemResourceMonitor.slnx -c Release --no-restore
dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build
dotnet publish src/system-resources/SystemResourceMonitor.Desktop/SystemResourceMonitor.Desktop.csproj -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true
git diff --check
```

- Build the separate `SystemResourceMonitor.Desktop.TestHost` Release executable from the same commit. Record the commands, exit codes, source commit, and SHA-256 of both executables and every production assembly loaded by each. Record the publish folder file list and hashes needed to bind later observations to the tested files.
- The solution build includes the test host. Query `dotnet msbuild tests/SystemResourceMonitor.Desktop.TestHost/SystemResourceMonitor.Desktop.TestHost.csproj -getProperty:TargetDir -getProperty:AssemblyName -p:Configuration=Release`, resolve `<TargetDir><AssemblyName>.exe`, and verify that file exists before hashing it. `TargetPath` names the DLL, not the executable. Keep the resolved executable path in the private run log and its hash in the sanitized report; record the test-host DLL hash separately from the loaded production-assembly hashes.
- Keep injection code, fixtures, secrets, development configuration, and diagnostic hooks out of the published folder. Do not copy the test host into it.
- If implementation changes after a gate is measured, rebuild and identify the new artifact, then rerun every affected gate. Evidence for an older hash does not accept a new artifact.

**Gate:** locked restore, build, tests, self-contained publish, and `git diff --check` pass; both artifacts and their shared source commit are identifiable.

### 3. Exercise test-host and fault gates on the measurement PC

- Run the production CPU and RAM native wrappers at normal two-second polling for 30 minutes. Timestamp every call. Neither reader may have two consecutive calls longer than two seconds.
- With fake time, block one system-metric collector after a success. At exactly six seconds its reading expires while the other continues to update; observe the actual UI transition within 0.5 seconds of the deadline. Repeat with the other collector blocked.
- For each accepted provider, verify that sign-in failure becomes unavailable on the first failed poll; network-stale data becomes `--/--` at three minutes from last success without a new poll; a blocked or timed-out provider cannot delay CPU/RAM or the other provider; and its child process tree ends within the shutdown deadline.
- Inject settings read, backup, and write failures while CPU/RAM monitoring is active. Monitoring continues, and failed persistence shows the restart-only message.
- Inject a handled startup failure and a never-completing, handle-free source. Verify prior work area is restored before collector wait, the total wait is at most two seconds, pending work is recorded, late results cannot publish, no in-use resource is disposed, and the process exits without leftovers.

**Gate:** record each scenario, its observations, and pass/fail against the test-host executable and loaded-assembly hashes. Deterministic tests alone do not close Windows integration or published-app gates.

### 4. Measure published-app accuracy and provider behavior

- At idle and sustained CPU load separately, collect 30 paired observations at two-second intervals against Task Manager in the same 60-second window. The difference between window means must be at most five percentage points in each regime; record transition spikes separately.
- At idle and sustained RAM load separately, collect 30 paired observations per regime against Task Manager physical memory usage. At least 29 of 30 observations in each regime must differ by at most two percentage points. Record used and total bytes.
- For each accepted provider, compare every displayed remaining limit and reset time with the official client in the same verification window, using its verified rounding rule. Exercise access loss and verify `--/--` plus a safe tooltip explanation. Repeat ten polls with no other work and verify no limit change or new session. Record sanitized comparisons and client version, never raw account output.

**Gate:** CPU accuracy, RAM accuracy, and every accepted provider's manual account checks pass on the published app. A rejected provider has no visible section.

### 5. Repeat desktop, DPI, and shutdown gates on the published app

- Repeat every published-app [placement scenario](../../plan.md#1-validate-placement-first) and the [B04 acceptance scenarios](../B04-placement-gate.md#acceptance-criteria) on actual Windows 11: start and exit, repeated top/bottom changes, Explorer restart, resolution and DPI changes, primary-monitor change, auto-hidden taskbar, focus, and stale-reservation checks. Verify the recovered tray icon's position and Exit actions. The injected handled-startup-failure check belongs to the test host in step 3.
- Run ten enter/leave cycles each for fullscreen video, an exclusive game, and a borderless game, including Alt+Tab to a normal window and back. Verify the bar lowers only for active fullscreen on the primary monitor, registration and reservation remain unchanged, restoration occurs within one second without focus stealing, and the prior work area returns on exit. Separately test Explorer restart during fullscreen, fullscreen on another monitor, and an ordinary maximized window.
- At 100%, 150%, and 200% scaling, verify the reserved strip matches the rendered bar. CPU/RAM and accepted-provider sections remain fixed and unclipped for 0%, 100%, and unavailable states; the RAM tooltip shows used/total GiB.
- Use normal Exit to verify work area restoration occurs before collector wait and the process leaves no background child or reservation.
- Observe the published app throughout its desktop and resource runs. Check that readings continue to update and the bar's position, tooltips, and Exit action remain responsive; record observations against the published executable and loaded-assembly hashes.

**Gate:** record direct published-app desktop evidence for every scenario. Earlier B04 evidence is a baseline, not a substitute for testing the B07 artifact and provider configuration.

### 6. Measure resource budgets without gaps

- On the published Release app at idle with every accepted provider enabled, measure process CPU time across 30 minutes. Compute `(process CPU time delta / elapsed wall time / logical processor count) × 100`; pass only at `<= 0.5%`. Sample private bytes every two seconds; every sample must be `<= 150 MiB`.
- After a 30-minute warm-up, keep normal monitoring and every accepted provider enabled for a complete eight-hour observation. Sample private bytes every two seconds. Every sample must be `<= 150 MiB`; the last 30-minute mean minus the first 30-minute mean must be `<= 10 MiB`.
- Record sample counts, instrument, aggregate values, interruptions, and workload changes. A missing sample or interrupted run invalidates the complete-run result and must be repeated. Report the growth calculation without claiming it proves absence of all leaks.

**Gate:** all three resource budgets pass on the same identified published-app configuration.

### 7. Check artifact hygiene and write user instructions

- Copy the same hashed published folder to a clean Windows 11 x64 PC with no separately installed .NET runtime. Record the runtime inventory before extraction, then verify launch without elevation or provider authorization. In the unauthorized state, accepted-provider sections show unavailable safely. Run the accuracy, resource, and desktop gates on the measurement PC specified in step 1; the clean-PC launch is an additional artifact-hygiene check.
- Compare the folder contents with the prohibited-material list: no test host, injection code, fixtures, secrets, development configuration, or production diagnostic hook. Verify the app still offers only first-release behavior.
- Write user-facing instructions for extraction, launch, top/bottom position, Exit, settings-error behavior including restart-only persistence, and unavailable CPU/RAM or provider readings. Name the qualified provider sections actually present in the artifact.

**Gate:** clean-folder launch and artifact inspection pass, and the instructions reproduce the tested first-release workflow.

### 8. Record evidence and decide acceptance

- Create separate dated automated, Windows integration, and manual desktop/resource reports under `docs/acceptance/`, with a short B07 gate index linking each criterion to its result. Each report identifies the source commit, relevant executable and loaded-production-assembly hashes, target hardware category, Windows build, logical processor count, display/DPI/taskbar setup, instrumentation, sample counts, aggregate observations, and pass/fail. Mark every unavailable or unrun gate `unverified`.
- Commit only sanitized reports. Keep raw traces privately only as long as needed for diagnosis. Never put usernames, absolute user paths, account or provider output, process or window titles, environment dumps, precise activity timelines, or unredacted screenshots in the repository.
- Update the [roadmap progress table](../README.md#progress) to `accepted` only after every B07 criterion and release-blocking gate passes on its required artifact. Otherwise leave B07 open and name the failing or unverified gate. B08, B09, B16, and B17 are unblocked only by accepted B07 evidence.

**Gate:** each acceptance criterion in the [B07 brief](../B07-first-release.md#acceptance-criteria) points to a passing result, and no report treats automated checks as proof of manual Windows behavior.
