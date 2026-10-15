using System.ComponentModel;
using System.Runtime.InteropServices;
using static SystemResourceMonitor.Desktop.Bar.NativeMethods;

namespace SystemResourceMonitor.Desktop.Bar;

internal enum FullscreenObservation
{
    Indeterminate,
    Normal,
    Fullscreen,
}

internal enum BarZOrderState
{
    Normal,
    Lowered,
}

internal readonly record struct ForegroundWindowSnapshot(
    bool IsValid,
    nint Window,
    bool IsVisible,
    bool IsShellWindow,
    nint Monitor,
    nint PrimaryMonitor,
    PixelRect WindowBounds,
    PixelRect MonitorBounds);

internal static class FullscreenClassifier
{
    public static FullscreenObservation Classify(in ForegroundWindowSnapshot snapshot, nint barWindow)
    {
        if (!snapshot.IsValid)
        {
            return FullscreenObservation.Indeterminate;
        }

        if (snapshot.Window == barWindow || !snapshot.IsVisible || snapshot.IsShellWindow)
        {
            return FullscreenObservation.Normal;
        }

        if (snapshot.Monitor == 0 || snapshot.Monitor != snapshot.PrimaryMonitor)
        {
            return FullscreenObservation.Normal;
        }

        if (!HasArea(snapshot.WindowBounds) || !HasArea(snapshot.MonitorBounds))
        {
            return FullscreenObservation.Indeterminate;
        }

        return snapshot.WindowBounds == snapshot.MonitorBounds
            ? FullscreenObservation.Fullscreen
            : FullscreenObservation.Normal;
    }

    public static bool IsShellDesktopWindow(
        nint window,
        nint shellWindow,
        nint desktopWindow,
        uint windowProcessId,
        uint shellProcessId,
        ReadOnlySpan<char> windowClass) =>
        window == desktopWindow ||
        (shellWindow != 0 && window == shellWindow) ||
        (windowProcessId != 0 &&
         windowProcessId == shellProcessId &&
         (windowClass.SequenceEqual("WorkerW") || windowClass.SequenceEqual("Progman")));

    private static bool HasArea(PixelRect bounds) => bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
}

internal interface IFullscreenStateReader
{
    FullscreenObservation Observe(nint barWindow);
}

internal interface IBarZOrderNativeApi
{
    void Apply(nint hwnd, BarZOrderState state);
}

/// <summary>
/// Applies z-order only after a conclusive foreground observation and only when the last successful state differs.
/// Invalidating the state after Explorer recovery forces the next conclusive observation to reapply z-order.
/// </summary>
internal sealed class FullscreenReconciler(
    nint barWindow,
    IFullscreenStateReader reader,
    IBarZOrderNativeApi native)
{
    private BarZOrderState _appliedState = BarZOrderState.Normal;
    private bool _zOrderKnown = true;

    public BarZOrderState AppliedState => _appliedState;

    public bool IsZOrderKnown => _zOrderKnown;

    public void InvalidateZOrder() => _zOrderKnown = false;

    public FullscreenObservation Reconcile()
    {
        var observation = reader.Observe(barWindow);
        if (observation == FullscreenObservation.Indeterminate)
        {
            return observation;
        }

        var desiredState = observation == FullscreenObservation.Fullscreen
            ? BarZOrderState.Lowered
            : BarZOrderState.Normal;
        if (_zOrderKnown && desiredState == _appliedState)
        {
            return observation;
        }

        native.Apply(barWindow, desiredState);
        _appliedState = desiredState;
        _zOrderKnown = true;
        return observation;
    }
}

internal sealed class ConsecutiveAbsenceTracker
{
    private bool _absenceSeen;

    public FullscreenObservation ObserveAbsence()
    {
        if (_absenceSeen)
        {
            return FullscreenObservation.Normal;
        }

        _absenceSeen = true;
        return FullscreenObservation.Indeterminate;
    }

    public void Reset() => _absenceSeen = false;
}

internal sealed class Win32FullscreenStateReader : IFullscreenStateReader
{
    private readonly ConsecutiveAbsenceTracker _absence = new();
    public FullscreenObservation Observe(nint barWindow)
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0)
        {
            if (GetForegroundWindow() == 0)
            {
                return _absence.ObserveAbsence();
            }

            _absence.Reset();
            return FullscreenObservation.Indeterminate;
        }

        var root = GetAncestor(foreground, GA_ROOT);
        if (root == 0 || !IsWindow(root))
        {
            _absence.Reset();
            return FullscreenObservation.Indeterminate;
        }

        var isVisible = IsWindowVisible(root);
        var isShellWindow = IsShellDesktopWindow(root);
        var monitor = MonitorFromWindow(root, MONITOR_DEFAULTTONULL);
        var primaryMonitor = MonitorFromPoint(new PixelPoint(0, 0), MONITOR_DEFAULTTOPRIMARY);
        if (primaryMonitor == 0)
        {
            _absence.Reset();
            return FullscreenObservation.Indeterminate;
        }

        if (monitor == 0)
        {
            if (ForegroundIdentityIsStable(foreground, root, monitor, primaryMonitor))
            {
                return _absence.ObserveAbsence();
            }

            _absence.Reset();
            return FullscreenObservation.Indeterminate;
        }

        _absence.Reset();

        if (!GetWindowRect(root, out var windowBounds))
        {
            return FullscreenObservation.Indeterminate;
        }

        var monitorInfo = NewMonitorInfo();
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return FullscreenObservation.Indeterminate;
        }

        var snapshot = new ForegroundWindowSnapshot(
            IsValid: ForegroundSnapshotIsStable(
                foreground,
                root,
                isVisible,
                monitor,
                primaryMonitor,
                windowBounds,
                monitorInfo.rcMonitor),
            Window: root,
            IsVisible: isVisible,
            IsShellWindow: isShellWindow,
            Monitor: monitor,
            PrimaryMonitor: primaryMonitor,
            WindowBounds: windowBounds,
            MonitorBounds: monitorInfo.rcMonitor);
        return FullscreenClassifier.Classify(snapshot, barWindow);
    }

    private static bool ForegroundIdentityIsStable(
        nint foreground,
        nint root,
        nint monitor,
        nint primaryMonitor) =>
        GetForegroundWindow() == foreground &&
        GetAncestor(foreground, GA_ROOT) == root &&
        IsWindow(root) &&
        MonitorFromWindow(root, MONITOR_DEFAULTTONULL) == monitor &&
        MonitorFromPoint(new PixelPoint(0, 0), MONITOR_DEFAULTTOPRIMARY) == primaryMonitor;

    private static bool ForegroundSnapshotIsStable(
        nint foreground,
        nint root,
        bool isVisible,
        nint monitor,
        nint primaryMonitor,
        PixelRect windowBounds,
        PixelRect monitorBounds)
    {
        if (!ForegroundIdentityIsStable(foreground, root, monitor, primaryMonitor) ||
            IsWindowVisible(root) != isVisible ||
            !GetWindowRect(root, out var currentWindowBounds) ||
            currentWindowBounds != windowBounds)
        {
            return false;
        }

        var currentMonitorInfo = NewMonitorInfo();
        return GetMonitorInfo(monitor, ref currentMonitorInfo) &&
            currentMonitorInfo.rcMonitor == monitorBounds;
    }

    private static unsafe bool IsShellDesktopWindow(nint window)
    {
        var shellWindow = GetShellWindow();
        var desktopWindow = GetDesktopWindow();
        _ = GetWindowThreadProcessId(window, out var windowProcessId);
        uint shellProcessId = 0;
        if (shellWindow != 0)
        {
            _ = GetWindowThreadProcessId(shellWindow, out shellProcessId);
        }

        Span<char> className = stackalloc char[64];
        int classNameLength;
        fixed (char* buffer = className)
        {
            classNameLength = GetClassName(window, buffer, className.Length);
        }

        return FullscreenClassifier.IsShellDesktopWindow(
            window,
            shellWindow,
            desktopWindow,
            windowProcessId,
            shellProcessId,
            className[..Math.Max(classNameLength, 0)]);
    }

    private static MONITORINFO NewMonitorInfo() => new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
}

internal sealed class Win32BarZOrderNativeApi : IBarZOrderNativeApi
{
    public void Apply(nint hwnd, BarZOrderState state)
    {
        var insertAfter = state switch
        {
            BarZOrderState.Normal => HWND_TOPMOST,
            BarZOrderState.Lowered => HWND_BOTTOM,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

        if (!SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowPos failed to change the bar z-order.");
        }
    }
}
