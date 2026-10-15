# B05 metric monitoring report — 2026-10-15

Brick: [B05 — Independent metric monitoring](../bricks/B05-metric-monitoring.md). Package plan: [B05 plan](../bricks/B05-metric-monitoring/plan.md).

Status: implemented; automated criteria pass; not accepted: B04 gate open. Windows integration and manual desktop checks are not applicable to B05: Desktop does not compose `MetricMonitor` until B06.

## Prerequisite and exception

The plan requires B04 to be accepted before B05 implementation begins. [B04's 2026-10-15 report](2026-10-15-B04-fullscreen-reconciliation.md) on `main` remains in progress and states: "B04 is not accepted and B05, B10, and B11 remain gated." The roadmap progress table is unchanged. On 2026-10-15 the user stated that B04 is done and authorized B05 implementation now. That implementation-order exception, approved on 2026-10-15, permits B05 code and deterministic tests while retaining every B04 and B05 acceptance gate. It does not accept either brick or unblock dependent work. This report does not change the B04 report.

## Environment

| | |
|---|---|
| Source | Branch `FEAT-005-metric-monitoring`, uncommitted working tree based on commit `955fbc5fc12778936310988a428852ddf2cad96f` |
| Tree | `b6dbba810339d189bd8d49ef1f964ee34b25282a`: the B05 working tree without this report, written through a temporary index |
| Windows | Windows 11 Pro x64, build 10.0.26300 |
| .NET SDK | 10.0.401, pinned in `global.json`. The machine-wide `dotnet` had only SDK 8.0.425, so SDK 10.0.401 was installed with the official `dotnet-install.ps1` into a session-local folder and used for every command |
| Test runner | Microsoft.Testing.Platform with xunit.v3 4.0.2 ([ADR 0005](../adr/0005-project-level-runtime-and-testing-platform.md)) |

## Baseline

Both baseline checks ran on the clean tree at `955fbc5` before any B05 change.

| Command | Exit code | Result |
|---|---|---|
| `dotnet test --project tests/SystemResourceMonitor.Core.Tests/SystemResourceMonitor.Core.Tests.csproj -c Release` | 0 | 8 total, 8 succeeded. |
| `dotnet build SystemResourceMonitor.slnx -c Release` | 0 | 0 warnings, 0 errors. |

## Implemented behavior

- `SystemResourceMonitor.Core.Metrics` contains the explicit `MetricReading` union. `AvailableMetric` has a private constructor and one validating factory, `AvailableMetric.Create`. `UnavailableMetric` carries one `UnavailabilityReason`. The namespace also has `RamBytes` and the immutable `DashboardState` with `Initial` and `With`.
- `IMetricSource` is the synchronous source port. Its `Read` returns the closed `MetricSample` union: `MeasuredSample`, `MeasuredRamSample`, or `SampleUnavailable`. `SampleUnavailable` rejects `Expired`. Core computes RAM percentage from the bytes. A sample with the wrong shape for its kind is `Invalid`.
- `MetricMonitor` owns the state behind one lock. It raises `Changed` inside that lock and runs one polling loop per source on thread-pool workers, with no overlapping reads. When a read overruns its interval, the loop skips the missed ticks. Each kind has one `TimeProvider` timer for its 6-second monotonic expiry. On cancellation, the monitor disposes both timers and rejects late results.
- Core's `BannedSymbols.txt` and the AI Core's `BannedSymbols.txt` now also ban `DateTime.Now`/`UtcNow`, `DateTimeOffset.Now`/`UtcNow`, `Environment.TickCount`/`TickCount64`, `Stopwatch`, `System.Threading.Timer`, and the four `Task.Delay` overloads that take no `TimeProvider`. `ArchitectureTests.BannedSymbolIds` lists the same IDs.

## Automated checks

| Command | Exit code | Result |
|---|---|---|
| `dotnet test --project tests/SystemResourceMonitor.Core.Tests/SystemResourceMonitor.Core.Tests.csproj -c Release`, ten consecutive runs | 0 each | 69 total and 69 succeeded in every run. |
| `dotnet restore SystemResourceMonitor.slnx -r win-x64 --locked-mode` | 0 | All six projects restored from lock files. |
| `dotnet build SystemResourceMonitor.slnx -c Release --no-restore` | 0 | 0 warnings, 0 errors. |
| `dotnet test --solution SystemResourceMonitor.slnx -c Release --no-build` | 0 | 152 total, 152 succeeded, 0 failed, on the first run. An earlier handoff run of the same command, before the review fixes, failed 1 of 149: the B04 real-HWND test `LifecycleProcessTests.TestHost_lifecycle_mode_passes_its_work_area_and_marker_assertions(mode: "--fullscreen-reconciliation")`, which depends on the interactive desktop foreground. It passed in three Desktop-only reruns and in a solution rerun. B05 changes no Desktop code; the test remains intermittent and outside B05. |
| `git diff --check` | 0 | No whitespace errors. |

`dotnet publish` was not run. B05 does not change the published app's behavior because Desktop does not compose `MetricMonitor` until B06.

Test counts: Core.Tests has 69 tests (8 from the baseline and 61 new in `tests/SystemResourceMonitor.Core.Tests/Metrics/`). Desktop.Tests is unchanged at 83, for a solution total of 152.

### Stability notes

During development, an earlier version of the test fixtures failed intermittently. The `ObservedTimeProvider` test wrapper reported a timer as armed before it reached `FakeTimeProvider`, so a test could move time past the timer. The wrapper now reports a timer only after the fake clock holds it. `MetricMonitor` also arms a reading's expiry timer before it raises `Changed`. After that fix, 20 consecutive `--no-build` runs passed, followed by the ten-run gate in the table above.

`FakeTimeProvider` cannot move its UTC clock without also moving its timers. The wall-clock test therefore uses the minimal test-only `SplitClockTimeProvider`, as the plan permits.

### Boundary probes

The probe files and edits below were temporary and are not in the working tree.

| Probe | Result |
|---|---|
| A temporary Core file that used `DateTime.UtcNow`, `Environment.TickCount64`, `Stopwatch`, `Task.Delay(int)`, and `new System.Threading.Timer(...)` | The Core build failed with `RS0030` for each symbol. |
| Remove `T:System.Diagnostics.Stopwatch` from Core's `BannedSymbols.txt` | `ArchitectureTests.Core_enforces_banned_apis_as_errors` failed. Restoring the file made it pass. |

## Acceptance criteria

| Criterion | Result | Proving tests |
|---|---|---|
| Tests cover percentage bounds, finite values, RAM byte invariants, each unavailability reason, and valid zero as distinct from unavailable | Pass | `MetricReadingTests`: `Cpu_percentage_from_0_through_100_is_available_without_bytes`, `Cpu_percentage_outside_0_through_100_or_not_finite_is_invalid` (just outside 0 and 100, NaN, both infinities), `Ram_percentage_is_computed_from_consistent_bytes` (`used == 0`, `used == total`), `Ram_bytes_with_zero_total_or_used_above_total_are_invalid`, `Cpu_carrying_ram_bytes_is_invalid`, `Ram_without_bytes_is_invalid`, `Source_unavailability_keeps_its_reason`, `A_source_cannot_report_expired`, `Initial_dashboard_state_has_not_sampled_either_metric`, `Valid_zero_is_available_and_distinct_from_unavailable`; `Expired` through `MetricMonitorStateTests.Reading_is_available_until_the_last_tick_before_six_seconds_and_expires_once_at_six` |
| Exact-boundary tests prove availability before six seconds and `Expired` at monotonic age `>= 6s`, unaffected by wall-clock changes | Pass | `MetricMonitorStateTests.Reading_is_available_until_the_last_tick_before_six_seconds_and_expires_once_at_six` (CPU and RAM), `Wall_clock_jumps_neither_expire_nor_extend_a_reading` (+3 h and −3 h), `A_newer_reading_moves_the_deadline_to_six_seconds_after_it` |
| A blocked CPU source expires while RAM continues updating, and the inverse also passes | Pass | `MetricMonitorPollingTests.A_blocked_source_expires_at_six_seconds_while_the_other_keeps_updating` (CPU blocked and RAM blocked) |
| Slow reads never overlap for the same source and never prevent the other source's schedule or expiry | Pass | `MetricMonitorPollingTests.A_five_second_read_neither_overlaps_nor_bursts_and_leaves_the_other_cadence_unchanged`, `Each_source_is_read_every_two_seconds`, `A_blocked_source_expires_at_six_seconds_while_the_other_keeps_updating`, `A_blocked_read_does_not_delay_the_expiry_of_the_other_source_whose_reads_fail` (CPU blocked with RAM failing, and the inverse; both readings expire at exactly 6 s) |
| Failed reads do not renew measurement time; known-invalid readings become unavailable immediately | Pass | `MetricMonitorStateTests.Failed_read_keeps_the_available_reading_and_its_original_deadline`, `Failed_read_after_expiry_publishes_once_and_a_later_success_makes_the_reading_available_again`, `Known_invalid_reading_replaces_an_available_one_immediately_and_leaves_the_other_source_alone`; `MetricMonitorPollingTests.A_throwing_read_is_a_failed_read_that_does_not_renew_or_stop_the_source` |
| Cancellation prevents new scheduling and publication of late results | Pass | `MetricMonitorPollingTests.Cancellation_stops_new_reads_and_discards_late_results_and_expiry`, `Cancellation_before_the_run_starts_no_read`, `The_monitor_runs_only_once`, `An_unexpected_failure_stops_both_loops_and_faults_the_run` |
| Core remains free of Windows, WPF, file, registry, process, and interop dependencies | Pass | `ArchitectureTests` (all 8, including `Core_enforces_banned_apis_as_errors` with the new time bans), the `RS0030` build check, and the boundary probes above |

The notification rule has its own tests: `MetricMonitorStateTests.Each_accepted_reading_publishes_even_when_its_value_is_unchanged` and `Unavailable_readings_publish_only_when_the_reason_changes`. Together with the expiry tests above, they prove one notification for each accepted reading and each availability transition, and no notification for an unchanged unavailable reason.

## Decisions within the plan

- When the current reading is available, `NotYetSampled` gets the same treatment as `ReadFailed`: the reading and its deadline stay unchanged. The plan names only `ReadFailed` for this case. A source reports `NotYetSampled` before its first baseline and again after a baseline reset.
- When an expiry timer fires for a reading that is still current but has not reached its deadline, the monitor re-arms the timer for the remaining time. The deadline still comes only from the monotonic timestamp. A reading that already has age 6 seconds or more when it is applied is published and then expires at once.
- `MetricMonitor.Apply` is `internal`, and Core declares `InternalsVisibleTo` for Core.Tests, so state tests can apply readings without the polling loops.
- A `Changed` handler that throws stops both loops and faults `RunAsync`, whether it throws on a polling loop or on an expiry timer callback. On the expiry path, the monitor catches the exception, records it in a `TaskCompletionSource` that `RunAsync` awaits, and cancels the run; the exception never escapes the timer callback. The event documentation says handlers must not throw. Tests: `MetricMonitorPollingTests.An_unexpected_failure_stops_both_loops_and_faults_the_run` and `A_handler_failure_during_expiry_faults_the_run_instead_of_escaping_the_timer`.

## Not applicable

Windows integration and manual desktop checks are not applicable to B05. The native adapters, dispatcher marshalling, and the real UI freshness transition belong to B06; the responsiveness gate belongs to B07.
