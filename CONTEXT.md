# System Resource Monitor

Glossary for an application that shows the computer's state and the remaining limits of personal subscriptions on a separate Windows bar.

## Language

**Bar**:
The application's persistent strip on the primary monitor, separate from the Windows taskbar.
_Avoid_: taskbar widget, taskbar

**System metric**:
A measured value of CPU, RAM or GPU state together with its measurement time.
_Avoid_: resource

**Load**:
Current CPU or GPU utilization as a percentage. For the GPU, the maximum across engines (3D, Compute, Copy, Video), as in Task Manager; the discrete GPU is used, or the integrated one if there is no discrete GPU.
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
The period over which the service accounts subscription usage. The short window lasts several hours; Claude Code calls it "Current session". A subscription may have several weekly windows, for example one for all models and one for a specific model. The actual set of windows for each provider is recorded in stage 1 of the plan.
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

**Unavailable**:
The state of a metric when the application has no trustworthy current value. A limit snapshot becomes unavailable immediately after a sign-in error, and after network errors once more than 3 minutes have passed since the last successful poll.
_Avoid_: zero
