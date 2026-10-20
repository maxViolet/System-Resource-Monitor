# System Resource Monitor

Glossary for an application that shows the computer's state and the remaining limits of personal subscriptions on a separate Windows bar.

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

**Remaining limit**:
The available share of subscription usage in one limit window, as a percentage. If the client reports the used share, remaining equals `100 − used`, where the used share is rounded to an integer the same way the official client rounds it.
_Avoid_: used percentage

**Binding weekly window**:
The subscription's weekly window with the lowest remaining limit; its remaining limit is shown on the bar as the weekly value.
_Avoid_: main window

**Limit snapshot**:
The remaining limits and reset times of all limit windows of one subscription, obtained in a single poll.
_Avoid_: subscription balance

**Unqualified limit window**:
A requested limit period for which the accepted source supplies no trustworthy value. Codex monthly usage is unqualified in B010 and appears as `month: --`; it is distinct from an unavailable qualified snapshot and from zero remaining.
_Avoid_: zero, estimated limit

**Unavailable**:
The state of a metric or limit snapshot when there is no trustworthy current value; distinct from a valid zero reading. Freshness deadlines and failure handling are specified in the product plan.
_Avoid_: zero
