# B08 foreign GPU counter correlation follow-up

Status: partial verification; B08 acceptance remains open. This report records why ADR 0013 was proposed and what evidence remains required.

## Diagnostic evidence

- The published app displayed `GPU —`.
- A fresh 5-second `--gpu-correlation` run exited with failure: the first read was a warmup `ReadFailed`; subsequent reads were `Uncorrelated`, with 96 unparseable names and no measured reads.
- A PowerShell inspection of five current counter snapshots found 96 empty-engine names per sample, all with LUID `00000000:0000f917`, physical index 0. None matched the selected NVIDIA GeForce RTX 4070 Laptop GPU LUID `00000000:0000fddc`.
- The selected adapter had 108 counted instances. Three independent snapshots produced candidate maximum-of-engine-sums values of 27%, 30%, and 30%. These are diagnostic calculations, not a Task Manager accuracy result.

Process identifiers and raw PDH instance names are omitted.

## Follow-up verification

- `dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode`: passed.
- Core tests: 99 passed, 0 failed.
- GPU source tests: 6 passed, 0 failed.
- `dotnet build SystemResourceMonitor.slnx -c Release --no-restore`: passed with 0 warnings and 0 errors.
- The initial full Desktop run found and prompted correction of the diagnostic test's expected instance count; its lifecycle cases also failed at startup while a monitor process held the AppBar lifecycle guard.
- `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build`: 232 passed, 1 failed. `Desktop_reserves_one_strip_ignores_a_second_launch_and_restores_the_work_area_on_close` reported that the Desktop process exited before showing the bar. A monitor from another worktree was running; AppBar contention is likely but not proven. This run is not a passing result.
- Standard publish output was locked by that same running process. Publish to `artifacts/gpu-fix-publish` succeeded.
- Updated `--gpu-correlation 10`: 5 reads, 4 measured reads, 0 correlation failures, max read 294.398 ms, no consecutive read overruns. The first read was the normal PDH warmup `ReadFailed`. The first measured value was 28.448%. This short run does not replace the required 30-minute correlation gate.
- Updated `--gpu-correlation 1800`: process exit code 0; 895 reads, 891 measured reads, 0 correlation failures, maximum read 191.504 ms, and no two consecutive reads over the 2-second threshold.
- The updated app was published to the standard output folder. The user reported that the display looked correct after restart. A later process inventory showed a monitor process from a different worktree, so the visible process's artifact identity is unverified; this is not a recorded Task Manager comparison.
- SHA-256 artifact identity from the local validation build: published app EXE `15D05C317ED2D3D06B6215EB4A12BCF3B021CB5F6E9796F1355FD6014BC43179`, Desktop DLL `C6890129508B2092E8D05B9D03D97B5FC3AE0B677FFDE0E67096770123B15968`, Core DLL `5951A89CEC40E6925BA0C3BDB56C6AC29263C56C52411DF28174C697F94C9A14`; test-host EXE `E3A9CC336031AA790EBC61A7C7F738E458451B2505FC2181650F3A2286F5284B`, Desktop DLL `C6890129508B2092E8D05B9D03D97B5FC3AE0B677FFDE0E67096770123B15968`, Core DLL `5951A89CEC40E6925BA0C3BDB56C6AC29263C56C52411DF28174C697F94C9A14`. Rebuild/publish after commit is still needed to bind artifact identity to a commit.
- `git diff --check`: passed.

## Required follow-up gates

- Run the updated Core and Desktop tests and the full solution verification commands from `docs/implementation-plan.md`.
- Compare the published app with Task Manager for 30 paired samples at idle and 30 under sustained windowed 3D load. Require a difference between window means of at most 5 percentage points in each regime.
- Repeat affected B07 resource, freshness, shutdown, and rendering gates on newly identified artifacts.
- Record the published-app GPU tooltip hover/non-activation behavior and any still-unverified affected B07 gates.

The historical 2026-10-16 B08 integration report remains a record of that run. It does not validate the ADR 0013 behavior. B08 remains incomplete until all applicable criteria are evidenced.
