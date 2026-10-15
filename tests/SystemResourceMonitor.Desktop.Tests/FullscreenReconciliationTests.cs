using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class FullscreenReconciliationTests
{
    private const nint BarWindow = 10;
    private const nint ForegroundWindow = 20;
    private const nint PrimaryMonitor = 30;
    private static readonly PixelRect MonitorBounds = new(0, 0, 1920, 1200);

    [Fact]
    public void Exact_visible_foreground_bounds_on_the_primary_monitor_are_fullscreen()
    {
        var snapshot = Snapshot(windowBounds: MonitorBounds);

        Assert.Equal(FullscreenObservation.Fullscreen, FullscreenClassifier.Classify(snapshot, BarWindow));
    }

    [Fact]
    public void Fullscreen_bounds_on_another_monitor_do_not_lower_the_primary_bar()
    {
        var snapshot = Snapshot(
            monitor: 31,
            primaryMonitor: PrimaryMonitor,
            windowBounds: new PixelRect(1920, 0, 3840, 1080),
            monitorBounds: new PixelRect(1920, 0, 3840, 1080));

        Assert.Equal(FullscreenObservation.Normal, FullscreenClassifier.Classify(snapshot, BarWindow));
    }

    [Fact]
    public void Ordinary_maximized_work_area_bounds_are_not_fullscreen()
    {
        var snapshot = Snapshot(windowBounds: new PixelRect(0, 24, 1920, 1168));

        Assert.Equal(FullscreenObservation.Normal, FullscreenClassifier.Classify(snapshot, BarWindow));
    }

    [Fact]
    public void Empty_or_invalid_geometry_is_indeterminate()
    {
        var snapshot = Snapshot(windowBounds: new PixelRect());

        Assert.Equal(FullscreenObservation.Indeterminate, FullscreenClassifier.Classify(snapshot, BarWindow));
    }

    [Fact]
    public void Foreground_absence_requires_two_consecutive_observations_before_restoration()
    {
        var absence = new ConsecutiveAbsenceTracker();

        Assert.Equal(FullscreenObservation.Indeterminate, absence.ObserveAbsence());
        Assert.Equal(FullscreenObservation.Normal, absence.ObserveAbsence());
        absence.Reset();
        Assert.Equal(FullscreenObservation.Indeterminate, absence.ObserveAbsence());
    }

    [Theory]
    [InlineData("WorkerW", 42U, 42U, true)]
    [InlineData("Progman", 42U, 42U, true)]
    [InlineData("WorkerW", 42U, 43U, false)]
    [InlineData("ApplicationWindow", 42U, 42U, false)]
    public void Explorer_desktop_window_classes_are_excluded_only_for_the_shell_process(
        string windowClass,
        uint windowProcessId,
        uint shellProcessId,
        bool expected)
    {
        Assert.Equal(
            expected,
            FullscreenClassifier.IsShellDesktopWindow(
                ForegroundWindow,
                shellWindow: 21,
                desktopWindow: 22,
                windowProcessId,
                shellProcessId,
                windowClass));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(true, false, 1)]
    public void Bar_shell_and_invisible_foreground_windows_are_excluded(
        bool isVisible,
        bool isShellWindow,
        int windowKind)
    {
        var window = windowKind == 1 ? BarWindow : ForegroundWindow;
        var snapshot = Snapshot(
            isValid: true,
            window: window,
            isVisible: isVisible,
            isShellWindow: isShellWindow,
            windowBounds: MonitorBounds);

        Assert.Equal(FullscreenObservation.Normal, FullscreenClassifier.Classify(snapshot, BarWindow));
    }

    [Fact]
    public void Invalid_or_inconsistent_foreground_observations_are_indeterminate()
    {
        var snapshot = Snapshot(isValid: false, windowBounds: MonitorBounds);

        Assert.Equal(FullscreenObservation.Indeterminate, FullscreenClassifier.Classify(snapshot, BarWindow));
    }

    [Fact]
    public void Lower_restore_and_repeated_observations_change_z_order_only_on_state_transitions()
    {
        var reader = new FakeReader(
            FullscreenObservation.Fullscreen,
            FullscreenObservation.Fullscreen,
            FullscreenObservation.Normal,
            FullscreenObservation.Normal);
        var native = new FakeZOrderNative();
        var reconciler = new FullscreenReconciler(BarWindow, reader, native);

        reconciler.Reconcile();
        reconciler.Reconcile();
        reconciler.Reconcile();
        reconciler.Reconcile();

        Assert.Equal([BarZOrderState.Lowered, BarZOrderState.Normal], native.AppliedStates);
        Assert.Equal(BarZOrderState.Normal, reconciler.AppliedState);
        Assert.True(reconciler.IsZOrderKnown);
    }

    [Fact]
    public void Failed_lowering_is_retried_without_committing_the_lowered_state()
    {
        var reader = new FakeReader(FullscreenObservation.Fullscreen, FullscreenObservation.Fullscreen);
        var native = new FakeZOrderNative { FailNext = true };
        var reconciler = new FullscreenReconciler(BarWindow, reader, native);

        Assert.Throws<InvalidOperationException>(() => reconciler.Reconcile());
        Assert.Equal(BarZOrderState.Normal, reconciler.AppliedState);

        reconciler.Reconcile();

        Assert.Equal([BarZOrderState.Lowered], native.AppliedStates);
        Assert.Equal(2, native.AttemptCount);
    }

    [Fact]
    public void Failed_restoration_is_retried_while_the_fullscreen_process_remains_alive()
    {
        var reader = new FakeReader(
            FullscreenObservation.Fullscreen,
            FullscreenObservation.Normal,
            FullscreenObservation.Normal);
        var native = new FakeZOrderNative();
        var reconciler = new FullscreenReconciler(BarWindow, reader, native);
        reconciler.Reconcile();
        native.FailNext = true;

        Assert.Throws<InvalidOperationException>(() => reconciler.Reconcile());
        Assert.Equal(BarZOrderState.Lowered, reconciler.AppliedState);

        reconciler.Reconcile();

        Assert.Equal([BarZOrderState.Lowered, BarZOrderState.Normal], native.AppliedStates);
    }

    [Fact]
    public void Indeterminate_observation_while_lowered_preserves_the_lowered_state()
    {
        var reader = new FakeReader(FullscreenObservation.Fullscreen, FullscreenObservation.Indeterminate);
        var native = new FakeZOrderNative();
        var reconciler = new FullscreenReconciler(BarWindow, reader, native);
        reconciler.Reconcile();

        var observation = reconciler.Reconcile();

        Assert.Equal(FullscreenObservation.Indeterminate, observation);
        Assert.Equal([BarZOrderState.Lowered], native.AppliedStates);
        Assert.Equal(BarZOrderState.Lowered, reconciler.AppliedState);
    }

    [Fact]
    public void Explorer_recovery_invalidation_reapplies_the_current_state_after_a_fresh_observation()
    {
        var reader = new FakeReader(FullscreenObservation.Fullscreen, FullscreenObservation.Fullscreen);
        var native = new FakeZOrderNative();
        var reconciler = new FullscreenReconciler(BarWindow, reader, native);
        reconciler.Reconcile();

        reconciler.InvalidateZOrder();
        reconciler.Reconcile();

        Assert.Equal([BarZOrderState.Lowered, BarZOrderState.Lowered], native.AppliedStates);
        Assert.True(reconciler.IsZOrderKnown);
    }

    [Fact]
    public void Indeterminate_observation_after_recovery_keeps_z_order_unknown_until_a_retry_succeeds()
    {
        var reader = new FakeReader(
            FullscreenObservation.Fullscreen,
            FullscreenObservation.Indeterminate,
            FullscreenObservation.Fullscreen);
        var native = new FakeZOrderNative();
        var reconciler = new FullscreenReconciler(BarWindow, reader, native);
        reconciler.Reconcile();
        reconciler.InvalidateZOrder();

        reconciler.Reconcile();
        Assert.False(reconciler.IsZOrderKnown);

        reconciler.Reconcile();

        Assert.Equal([BarZOrderState.Lowered, BarZOrderState.Lowered], native.AppliedStates);
        Assert.True(reconciler.IsZOrderKnown);
    }

    private static ForegroundWindowSnapshot Snapshot(
        bool isValid = true,
        nint window = ForegroundWindow,
        bool isVisible = true,
        bool isShellWindow = false,
        nint monitor = PrimaryMonitor,
        nint primaryMonitor = PrimaryMonitor,
        PixelRect? windowBounds = null,
        PixelRect? monitorBounds = null) =>
        new(
            isValid,
            window,
            isVisible,
            isShellWindow,
            monitor,
            primaryMonitor,
            windowBounds ?? new PixelRect(100, 100, 800, 600),
            monitorBounds ?? MonitorBounds);

    private sealed class FakeReader(params FullscreenObservation[] observations) : IFullscreenStateReader
    {
        private readonly Queue<FullscreenObservation> _observations = new(observations);

        public FullscreenObservation Observe(nint barWindow)
        {
            Assert.Equal(BarWindow, barWindow);
            return _observations.Dequeue();
        }
    }

    private sealed class FakeZOrderNative : IBarZOrderNativeApi
    {
        public List<BarZOrderState> AppliedStates { get; } = [];

        public bool FailNext { get; set; }

        public int AttemptCount { get; private set; }

        public void Apply(nint hwnd, BarZOrderState state)
        {
            Assert.Equal(BarWindow, hwnd);
            AttemptCount++;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Injected z-order failure.");
            }

            AppliedStates.Add(state);
        }
    }
}
