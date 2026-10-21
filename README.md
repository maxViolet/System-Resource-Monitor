# System Resource Monitor

System Resource Monitor reserves a thin strip at the top or bottom of the primary monitor and shows the current CPU load, physical memory use, and how much of your Codex subscription limits is used. Windows keeps maximized windows out of the strip, as it does for the taskbar.

This release has no installer and does not start with Windows.

## Requirements

- Windows 11 x64.
- No separately installed .NET runtime: the folder contains everything the app needs.
- No administrator rights.

## Extract and launch

1. Extract the whole release folder to a location you can write to, for example a folder in your user profile. Keep every file in the folder together.
2. Run `SystemResourceMonitor.Desktop.exe` from that folder.

The bar appears at the top of the primary monitor the first time. A second launch while the bar is running does nothing; only one bar runs per Windows session.

The app also adds a "System Resource Monitor" icon to the notification area. If Windows hides it, open the overflow arrow next to the clock.

## Read the bar

- `CPU 37%` is the share of total processor time in use across all logical processors.
- `RAM 52%` is the share of physical memory in use. Hover over it to see used and total memory, for example `RAM 8.3 / 15.9 GiB used`.
- Readings update about every two seconds.

A section shows a dash, such as `CPU —`, when no current reading exists. Hover over the section for the reason:

| Tooltip | Meaning |
|---|---|
| Waiting for the first measurement. | The app has just started. CPU load needs two measurements, so `CPU —` shows briefly after launch. |
| No update for 6 seconds. | Windows has not returned a reading for six seconds. The previous value is hidden so a stale value is never shown as current. |
| Windows returned an inconsistent measurement. | Windows returned values that cannot be correct. The next valid reading replaces the dash. |
| Windows did not return a measurement. | The Windows call failed. The next successful reading replaces the dash. |

The other section keeps updating while one section shows a dash.

## Read the Codex section

The Codex section reads your used Codex limits through the official standalone Codex client, version 0.159.0, with the login that client already has. The app never reads the client's login files and never contacts OpenAI itself. It asks the client once per minute; a request does not use any of your limits.

`Codex: 5h 27% in 01:42 | W 4%` means:

- `5h 27%`: 27 % of the 5-hour limit is used. `in 01:42` is the time until that limit resets, in hours and minutes, rounded up to the next minute. While the 5-hour window is unused (`5h 0%`), no countdown is shown, because the window starts only with your next use.
- `W 4%`: 4 % of the weekly limit is used.

The Codex client reports no monthly limit, so the section has no monthly value.

Hover over the section for the exact local reset times.

If the client cannot reach its usage service, the section shows the last values as `Codex (stale): …` for up to three minutes after the last successful reading. After that, and for any other problem, it shows `Codex: 5h -- | W --`. Hover over it for the reason:

| Tooltip | Meaning |
|---|---|
| Waiting for the first Codex reading. | The app has just started. The first reading can take several seconds, because the app first verifies the client's signature. |
| Codex is signed out. Sign in with the Codex client. | The client has no signed-in account. |
| The Codex usage service could not be reached. | The client had no network access to its usage service. |
| Codex answered in a format this bar does not recognise. | The client's answer did not match the qualified format. |
| The installed Codex client is not version 0.159.0, the only supported version. | The client has updated itself to a version this release does not support. |
| The standalone Codex client is not installed. | `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe` does not exist. The npm package and the Codex desktop app are not used. |
| The Codex client failed the path, permission, or signature check. | The client is not the signed OpenAI release in its installer folder, or other accounts can change it. |
| Codex did not answer in time. | The client did not answer within 10 seconds. |
| The Codex client could not be started. | Windows could not start the client. |
| No Codex update for 3 minutes. | No reading has succeeded for three minutes. |

## Change the position

1. Right-click the notification-area icon.
2. Open **Position** and select **Top** or **Bottom**.

The bar moves at once, and the check mark shows the current position. The app saves the choice in `%LOCALAPPDATA%\SystemResourceMonitor\settings.json` and uses it on the next launch.

While a fullscreen video or game is in the foreground on the primary monitor, the bar stays behind it. The bar returns when you switch to a normal window. Its strip stays reserved throughout.

## Exit

Right-click the notification-area icon and select **Exit**. The app releases the strip, and windows can use the full screen again.

## Settings errors

Settings problems never stop the bar. The app shows a notification from its notification-area icon and keeps monitoring:

| Notification | Meaning |
|---|---|
| Settings could not be read. Top is in use. | The settings file exists but could not be read. The bar uses the top position. |
| Invalid settings were preserved. Top is in use. | The settings file is invalid. The app renamed it to `settings.invalid-<time>.json` in the same folder and uses the top position. |
| Invalid settings could not be preserved. Top is in use. | The settings file is invalid, and the app could not rename it. The bar uses the top position. |
| The current position applies only until restart. | The new position is in use, but the app could not save it. The next launch uses the last saved position. |

To return to the default position, exit the app, delete `%LOCALAPPDATA%\SystemResourceMonitor\settings.json`, and launch the app again.

## Remove the app

1. Exit the app.
2. Delete the release folder.
3. Optionally, delete `%LOCALAPPDATA%\SystemResourceMonitor`.
