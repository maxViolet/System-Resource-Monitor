# Plan: System Resource Monitor

Terms are defined in [CONTEXT.md](../CONTEXT.md); the placement decision is in [ADR 0001](adr/0001-separate-windows-appbar.md). The buildable clean-architecture slices are in the [MVP implementation plan](implementation-plan.md).

## Requirements

- Windows 11; personal use first, possible public release later.
- A thin dark bar across the full width of the primary monitor, following the provided mockup.
- Top position or bottom position above the Windows taskbar; the bar reserves space and hides while a fullscreen application runs on the primary monitor.
- On the bar: CPU/GPU load and temperature, RAM usage; for Codex and Claude, `short%/weekly%`, where weekly is the remaining limit of the binding weekly window.
- On hover over a subscription: all limit windows with remaining limits and reset times; on error, an explanation in the same tooltip. An unavailable sensor shows a dash; an unavailable limit snapshot shows `--/--`.
- System metrics refresh every 2 seconds, limits every minute.
- Start at Windows sign-in, which can be disabled.

## 1. Verify limit sources — blocking spike for the subscription sections

- On already authorized Codex CLI and Claude Code, find a reproducible local way to obtain, for each limit window, the remaining or used share and the reset time.
- Record each provider's set of windows with their names in the client; whether the client reports remaining or used share and how it rounds it; the client version, invocation method, response format, constraints, and behaviour after signing out.
- Allowed sources: commands and output of the official client, and files the client itself writes locally, excluding credentials. Do not read client tokens or call undocumented endpoints with them, do not store secrets, do not read browser cookies. Relaxing this rule requires a separate ADR.
- Success for each provider:
  - two consecutive non-interactive polls return all windows and reset times;
  - sign-in and network errors are distinguishable and never turn into a stale value;
  - polling does not consume the limit: a series of 10 polls with no other work leaves remaining limits unchanged and creates no new sessions in the client's history.
- If verification fails, keep the corresponding section unavailable. This does not block the release of the system bar.

## 2. Build the bar shell — blocking stage for the first release

- A window across the full width of the primary monitor registers as an AppBar and occupies a strip negotiated with Windows.
- A setting switches between the top position and the bottom position above the Windows taskbar.
- On real Windows 11, verify start, exit, Explorer restart, position change, resolution change and scaling change. After the application exits, the work area is restored.
- Verify the bottom position with an auto-hidden taskbar.
- When the primary monitor changes, the bar moves to the new primary monitor and the previous monitor's work area is restored.
- While a fullscreen application runs on the primary monitor, the bar hides and returns after it exits; a fullscreen application on another monitor does not hide the bar. Verify manually with video, a game in exclusive fullscreen, and a game in a borderless fullscreen window.

## 3. Add system metrics — blocking stage for the first release

- First choose the CPU/GPU temperature source and record: whether it needs a kernel driver and administrator rights, Microsoft Defender's reaction on a clean system, and its license. A source that triggers an antivirus warning (for example, the WinRing0 driver, which Defender flags as HackTool) is not chosen.
- If no suitable CPU temperature source exists, CPU temperature is shown as unavailable and the limitation is documented; this does not block the release.
- If the chosen source needs administrator rights, the stage 5 autostart uses a Task Scheduler task with highest privileges instead of a `Run` key.
- Every 2 seconds, refresh CPU/GPU load and temperature and RAM usage.
- For a missing sensor, show a dash and keep the remaining working metrics.
- On the target PC, compare values with an independent system tool at idle and under load; compare GPU load with Task Manager. Record the CPU/GPU models and unavailable sensors. Define exact comparison tolerances after choosing the metrics source.

## 4. Add limit display — conditional stage

- For each service, show `short%/weekly%` on the bar using the binding weekly window; on hover, all windows with remaining limits and reset times, and on error, an explanation in the same tooltip.
- Poll every minute. A sign-in error makes the limit snapshot unavailable immediately. On a network error, the last snapshot is shown for at most 3 minutes after it was obtained, then `--/--`.
- Acceptance on a subscription: remaining limits match the official client's remaining value or `100 − used` from it, and reset times match the client within the same verification window; after access is lost, the error state appears. This is a manual check, separate from unit tests of data parsing.

## 5. Prepare personal use and public release

- Add an on/off start at Windows sign-in using the method chosen in stage 3, and an exit that releases the AppBar.
- Document installation, sensor and limit constraints, and how to disable autostart.
- Before public release, verify startup on a clean Windows 11 profile without Codex/Claude sign-in: the system bar works, subscription sections are explicitly unavailable, the antivirus shows no warnings, and a UAC prompt appears only if stage 3 chose a source that needs administrator rights.

## Release rule

The first release requires stages 2, 3 and 5 to pass. Stage 1 gates only the corresponding subscription section of stage 4. Unverified limits are never displayed as trustworthy.
