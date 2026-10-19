# Exit when a collector run faults

Status: accepted, 2026-10-15. Applies to collector tasks in the [implementation plan](../implementation-plan.md#technology-and-layout) and to B006. [ADR 0009](0009-exit-when-tray-recovery-fails.md) remains in force.

## Decision

`DesktopStartup` starts each collector run and passes its task to `ShutdownCoordinator.TrackOwner(operation, task)`. The first collector run is `MetricMonitor.RunAsync`. `TrackOwner` adds the task to the bounded shutdown wait and observes it on the UI thread:

- A task that completes, or that is cancelled by the lifetime token, is not a failure.
- A task that faults records `owner-failed:<operation>:<exception type>` and enters the shared shutdown path with the failure exit code. That path sends `ABM_REMOVE`, so the reservation is released.

The lifetime token comes from `ShutdownCoordinator.Lifetime`; collectors do not receive a separate token.

Source failures do not fault a collector run. A failed or invalid read is a `SampleUnavailable` sample and becomes an `Unavailable` reading. A collector run faults only on a defect, such as a `Changed` handler that throws, which stops both polling loops and the expiry timers.

## Reason and alternatives

After a fault, no loop polls and no timer expires a reading. The bar would keep showing the last values as fresh indefinitely, which breaks the six-second freshness rule without any visible sign. Exiting makes the defect visible and restores the work area.

- **Show both readings as `Unavailable` and keep running.** Rejected: the readings never recover, the bar stays reserved with nothing to show, and the defect stays hidden behind a state that also means an ordinary read failure.
- **Restart the collector run with a bounded retry.** Rejected for the first release: a fault comes from a defect, not from a transient cause, so a restart would repeat it. A retry also adds a timer state machine. Revisit if Windows evidence shows a transient fault.

Later collectors, such as the provider monitors in B012 and B013, follow the same rule. They map source failures to `Unavailable` readings and must not fault on them, so one provider's failure does not stop the bar. Revisit this ADR if a later collector can fault for a transient cause.

## Acceptance

Deterministic tests in `ShutdownCoordinatorTests` cover four behaviours: a faulted owner exits with the failure exit code, a completed or cancelled owner causes no failure, a real `MetricMonitor` whose `Changed` handler throws exits with the failure exit code, and a run cancelled by shutdown is not a failure. On a real desktop, a faulted collector run must exit with the work area restored.
