# System Resource Monitor

System Resource Monitor reserves a thin strip at the top or bottom of the primary monitor and shows the current CPU load and physical memory use. Windows keeps maximized windows out of the strip, as it does for the taskbar.

This release shows CPU and RAM only. It has no installer and does not start with Windows.

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
