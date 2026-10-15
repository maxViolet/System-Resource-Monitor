# Draw own volume, network, and battery indicators

Status: accepted, 2026-10-15. Applies to [ADR 0008](0008-replace-taskbar-buttons-in-phases.md) phase B.

## Decision

Before the Windows taskbar is hidden, the bar shows its own shell indicators for volume, network, and battery between the tray and the clock.

- **Volume:** the default output device's level and mute state from the documented Core Audio endpoint-volume API. Scrolling over the indicator changes the level; clicking toggles mute.
- **Network:** connected, limited, or disconnected state and the connection type from documented Windows network-status APIs.
- **Battery:** charge percentage and charging state from `GetSystemPowerStatus`; the indicator is absent when the system reports no battery.
- Each indicator's tooltip names its current state. A right click opens the matching documented `ms-settings:` page. An indicator whose source fails shows the unavailable state rather than a guessed value.
- The indicators live in Desktop. Their state may cross into Core only through ports that keep the [Core boundary](0002-appbar-first-cpu-ram-release.md).

## Reason and alternatives

On Windows 11, volume, network, and battery are part of the shell's own tray area rather than ordinary `Shell_NotifyIcon` icons, so the replacement tray from [ADR 0009](0009-managedshell-for-tasks-and-tray.md) cannot be relied on to show them. Hiding the Windows taskbar without another way to see and change them removes a daily function.

- **Rely on `Win+A` and `Win+N` with the taskbar hidden:** no new code, but nothing is visible on the bar, and whether the flyouts still open with a hidden taskbar is unverified.
- **Leave the Windows taskbar visible for these controls:** contradicts the replacement goal of ADR 0008.
- **Own indicators with documented APIs (chosen):** visible state and the common actions on the bar, at the cost of about 2–4 days and three more sources to verify.

Volume device selection, Wi-Fi network selection, brightness, and power-mode controls stay in Windows Settings and Quick Settings.
