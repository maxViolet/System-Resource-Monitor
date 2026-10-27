# System Resource Monitor

Glossary for an application that shows the computer's state and the used limits of personal subscriptions on a separate Windows bar.

## Language

**Bar**:
The application's persistent strip on the primary monitor, separate from the Windows taskbar.
_Avoid_: taskbar widget, taskbar

**Task**:
An open top-level window that Windows shows as a button on the Windows taskbar.
_Avoid_: process, application, when meaning one window

**Task button**:
The bar's control for one task: its icon, trimmed title, and active or flashing state.
_Avoid_: taskbar button, when meaning the bar's control

**Task strip**:
The centre part of the bar that holds the task buttons of all tasks on every monitor.
_Avoid_: taskbar

**Overflow menu**:
The `»` menu at the end of the task strip that lists the tasks whose task buttons do not fit.
_Avoid_: hidden tasks

**Clock**:
The current local time on the right of the bar, in the Windows regional short time format.
_Avoid_: system clock, when meaning the bar's clock

**Shell indicator**:
The bar's own volume, network, or battery indicator, shown between the tray and the clock.
_Avoid_: tray icon, when meaning the bar's own indicator

**System metric**:
A measured value of CPU, RAM or GPU state together with its measurement time.
_Avoid_: resource

**Load**:
Current CPU or GPU utilization as a percentage. For the GPU, the maximum across engines (3D, Compute, Copy, Video), as in Task Manager, of the one discrete GPU only; with no discrete GPU or more than one, GPU load is unavailable.
For GPU counters, an empty engine type is ignored only when the full identity parses and its adapter LUID differs from the selected GPU. An empty engine type on the selected LUID or an unparseable identity makes GPU load unavailable.
_Avoid_: power

**RAM usage**:
The share of physical memory currently in use, as a percentage.
_Avoid_: RAM load

**Temperature**:
A hardware sensor reading for the CPU or GPU in degrees Celsius. For the CPU, the whole-processor (package) sensor, not a single core.
_Avoid_: estimated temperature

**Subscription**:
A personal Codex or Claude plan used through its authorized client; distinct from a paid API balance.
_Avoid_: API account, credit balance

**Limit window**:
The period over which the service accounts subscription usage. The short window lasts several hours; Claude Code calls it "Current session". A subscription may have several weekly windows, for example one for all models and one for a specific model. The actual set of windows for each provider is recorded in its independent source spike.
_Avoid_: session, when meaning the accounting period

**Used limit**:
The consumed share of subscription usage in one limit window, as the integer percentage the official client reports. The bar and its tooltips show used limits ([ADR 0021](docs/adr/0021-used-limit-display.md)).
_Avoid_: used capacity, consumption

**Remaining limit**:
The available share of subscription usage in one limit window, as a percentage: `100 − used limit`. It is not shown on the bar.
_Avoid_: free share

**Binding weekly window**:
The subscription's weekly window with the highest used limit (the lowest remaining limit); its used limit is shown on the bar as the weekly value.
_Avoid_: main window

**Short-window countdown**:
The whole minutes until an active short limit window resets, shown as `HH:mm` only above 0% used. Round up so a positive duration never appears as `00:00`; an unused 0% window has no countdown.
_Avoid_: reset time, when meaning the relative countdown

**Weekly reset countdown**:
The time until the binding weekly window resets, shown beside its used limit as `Dd Hh` without leading zeroes when that limit is above 0% and a qualified reset time is known ([ADR 0024](docs/adr/0024-weekly-countdown-units.md)).
_Avoid_: weekly usage duration

**Limit snapshot**:
The used limits and reset times of all limit windows of one subscription, obtained in a single poll.
For Claude Code `/usage`, a window at 0% used may omit its reset time; this is an explicit unknown reset, not an unavailable used limit.
_Avoid_: subscription balance

**Unqualified limit window**:
A requested limit period for which the accepted source supplies no trustworthy value. It is not shown on the bar: Codex monthly usage is unqualified in B010 and B019 owns any later monthly qualification; Claude monthly usage is unqualified when B012 qualifies no monthly window. It is distinct from an unavailable qualified snapshot, which shows `--`, and from zero used.
_Avoid_: zero, estimated limit

**Stale limit snapshot**:
The last successful limit snapshot, still shown and marked stale after a network failure until three minutes after that success; then the snapshot is unavailable. Other failures make the snapshot unavailable at once.
_Avoid_: cached limits, last known limits

**Unavailable**:
The state of a metric or limit snapshot when there is no trustworthy current value; distinct from a valid zero reading. Freshness deadlines and failure handling are specified in the product plan.
_Avoid_: zero
