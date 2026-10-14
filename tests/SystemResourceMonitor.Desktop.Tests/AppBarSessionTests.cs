using SystemResourceMonitor.Desktop.Bar;
using SystemResourceMonitor.Desktop.Settings;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class AppBarSessionTests
{
    [Theory]
    [InlineData(BarEdge.Top, -1920, -200, 0, 880, 96, -200, -176)]
    [InlineData(BarEdge.Bottom, -1920, -200, 0, 880, 96, 856, 880)]
    [InlineData(BarEdge.Top, 0, 0, 2560, 1440, 144, 0, 36)]
    [InlineData(BarEdge.Bottom, 0, 0, 3840, 2160, 192, 2112, 2160)]
    public void Rectangle_calculation_handles_edges_origins_and_DPI(
        BarEdge edge,
        int left,
        int top,
        int right,
        int bottom,
        uint dpi,
        int expectedTop,
        int expectedBottom)
    {
        var thickness = AppBarPlacement.PhysicalThickness(BarWindow.ThicknessDips, dpi);
        var result = AppBarPlacement.ApplyThickness(new PixelRect(left, top, right, bottom), edge, thickness);

        Assert.Equal(expectedTop, result.Top);
        Assert.Equal(expectedBottom, result.Bottom);
        AppBarPlacement.Validate(result, new PixelRect(left, top, right, bottom), thickness);
    }

    [Fact]
    public void Ten_edge_changes_use_one_registration_and_leave_the_final_edge_applied()
    {
        var native = new FakeNative();
        var appBar = new AppBarSession(123, 456, native);
        appBar.Register();

        for (var index = 0; index < 10; index++)
        {
            appBar.Place(index % 2 == 0 ? BarEdge.Top : BarEdge.Bottom);
        }

        Assert.Equal(1, native.RegisterCount);
        Assert.Equal(0, native.RemoveCount);
        Assert.Equal(10, native.SetPositionCount);
        Assert.Equal(BarEdge.Bottom, appBar.AppliedEdge);
        appBar.Remove();
        Assert.Equal(1, native.RemoveCount);
    }

    [Fact]
    public void Failed_window_move_reapplies_the_previous_edge_with_fresh_geometry()
    {
        var native = new FakeNative();
        var appBar = new AppBarSession(123, 456, native);
        appBar.Register();
        appBar.Place(BarEdge.Top);
        native.MonitorBounds = new PixelRect(0, 0, 2560, 1440);
        native.FailWindowPosition = true;

        Assert.Throws<InvalidOperationException>(() => appBar.Place(BarEdge.Bottom));
        Assert.Equal(BarEdge.Top, appBar.AppliedEdge);
        Assert.Equal(new PixelRect(0, 0, 2560, 36), appBar.ReservedBounds);
    }

    [Fact]
    public void Explorer_recovery_registers_and_positions_before_recreating_the_tray()
    {
        var events = new List<string>();
        var native = new FakeNative(events);
        var appBar = new AppBarSession(123, 456, native);
        appBar.Register();
        appBar.Place(BarEdge.Top);
        events.Clear();
        var tray = new FakeTray(events);

        var result = ShellRecovery.Recover(appBar, tray, BarEdge.Top);

        Assert.True(result.Succeeded);
        Assert.Equal(["abm-new", "abm-querypos", "abm-setpos", "setwindowpos", "nim-add", "nim-setversion"], events);
        Assert.Equal(0, native.RemoveCount);
    }

    [Fact]
    public void Spurious_TaskbarCreated_with_a_surviving_registration_still_recovers_placement()
    {
        var events = new List<string>();
        var native = new FakeNative(events);
        var appBar = new AppBarSession(123, 456, native);
        appBar.Register();
        appBar.Place(BarEdge.Top);
        events.Clear();
        native.FailRegister = true;
        var tray = new FakeTray(events);

        var result = ShellRecovery.Recover(appBar, tray, BarEdge.Top);

        Assert.True(result.Succeeded);
        Assert.Equal(["abm-new", "abm-querypos", "abm-setpos", "setwindowpos", "nim-add", "nim-setversion"], events);
    }

    [Fact]
    public void Lost_AppBar_recovery_is_reported_retains_cleanup_responsibility_and_still_attempts_the_tray()
    {
        var events = new List<string>();
        var native = new FakeNative(events);
        var appBar = new AppBarSession(123, 456, native);
        appBar.Register();
        appBar.Place(BarEdge.Top);
        events.Clear();
        native.FailRegister = true;
        native.FailQuery = true;
        var tray = new FakeTray(events);

        var result = ShellRecovery.Recover(appBar, tray, BarEdge.Top);

        Assert.False(result.AppBarRecovered);
        Assert.True(result.TrayRecovered);
        Assert.Contains("nim-add", events);
        appBar.Remove();
        Assert.Equal(1, native.RemoveCount);
    }

    [Theory]
    [InlineData(true, true, nameof(ShellRecoveryAction.None))]
    [InlineData(false, true, nameof(ShellRecoveryAction.ReportAppBarFailure))]
    [InlineData(true, false, nameof(ShellRecoveryAction.FailureShutdown))]
    [InlineData(false, false, nameof(ShellRecoveryAction.FailureShutdown))]
    public void Lost_tray_forces_failure_shutdown_and_lost_AppBar_alone_is_reported(
        bool appBarRecovered,
        bool trayRecovered,
        string expected)
    {
        Assert.Equal(
            Enum.Parse<ShellRecoveryAction>(expected),
            new ShellRecoveryResult(appBarRecovered, trayRecovered).NextAction);
    }

    [Fact]
    public void Explorer_recovery_exposes_partial_tray_failure_after_restoring_the_AppBar()
    {
        var events = new List<string>();
        var native = new FakeNative(events);
        var appBar = new AppBarSession(123, 456, native);
        appBar.Register();
        appBar.Place(BarEdge.Top);
        events.Clear();
        var tray = new FakeTray(events) { FailRecreate = true };

        var result = ShellRecovery.Recover(appBar, tray, BarEdge.Top);

        Assert.True(result.AppBarRecovered);
        Assert.False(result.TrayRecovered);
        Assert.Equal(["abm-new", "abm-querypos", "abm-setpos", "setwindowpos", "nim-add"], events);
    }

    [Fact]
    public void Placement_reads_fresh_primary_monitor_geometry_and_DPI_each_time()
    {
        var native = new FakeNative();
        var appBar = new AppBarSession(123, 456, native);
        appBar.Register();
        appBar.Place(BarEdge.Top);
        native.MonitorBounds = new PixelRect(0, -1080, 1920, 0);
        native.Dpi = 192;

        appBar.Place(BarEdge.Top);

        Assert.Equal(48, appBar.ThicknessPixels);
        Assert.Equal(new PixelRect(0, -1080, 1920, -1032), appBar.ReservedBounds);
    }

    [Fact]
    public void Shell_messages_route_display_DPI_AppBar_and_Explorer_without_a_fullscreen_transition()
    {
        const uint appBarCallback = 0xC123;
        const uint taskbarCreated = 0xC124;
        Assert.Equal(ReconciliationReason.Placement, ShellMessageClassifier.Classify(NativeMethods.WM_DISPLAYCHANGE, 0, appBarCallback, taskbarCreated));
        Assert.Equal(ReconciliationReason.Placement, ShellMessageClassifier.Classify(NativeMethods.WM_DPICHANGED, 0, appBarCallback, taskbarCreated));
        Assert.Equal(ReconciliationReason.Placement, ShellMessageClassifier.Classify((int)appBarCallback, (nint)NativeMethods.ABN_POSCHANGED, appBarCallback, taskbarCreated));
        Assert.Equal(ReconciliationReason.Placement, ShellMessageClassifier.Classify((int)appBarCallback, (nint)NativeMethods.ABN_STATECHANGE, appBarCallback, taskbarCreated));
        Assert.Equal(ReconciliationReason.ExplorerRestart, ShellMessageClassifier.Classify((int)taskbarCreated, 0, appBarCallback, taskbarCreated));
        Assert.Equal(ReconciliationReason.None, ShellMessageClassifier.Classify((int)appBarCallback, (nint)NativeMethods.ABN_FULLSCREENAPP, appBarCallback, taskbarCreated));
    }

    [Fact]
    public void Reconciliation_coalesces_a_burst_and_allows_only_one_immediate_follow_up()
    {
        var scheduled = new Queue<Action>();
        var passes = new List<ReconciliationReason>();
        ReconciliationCoordinator? coordinator = null;
        coordinator = new ReconciliationCoordinator(scheduled.Enqueue, reason =>
        {
            passes.Add(reason);
            if (passes.Count == 1)
            {
                coordinator!.Request(ReconciliationReason.Placement);
                coordinator.Request(ReconciliationReason.ExplorerRestart);
            }
        });

        coordinator.Request(ReconciliationReason.Placement);
        coordinator.Request(ReconciliationReason.Placement);
        Assert.Single(scheduled);
        scheduled.Dequeue()();

        Assert.Equal([ReconciliationReason.Placement, ReconciliationReason.Placement | ReconciliationReason.ExplorerRestart], passes);
        Assert.Empty(scheduled);
    }

    [Fact]
    public void Tray_deletion_is_idempotent()
    {
        var events = new List<string>();
        var registration = new TrayIconRegistration(
            () => events.Add("nim-add"),
            () => events.Add("nim-setversion"),
            () => events.Add("nim-delete"));

        registration.Add();
        registration.Remove();
        registration.Remove();

        Assert.Equal(["nim-add", "nim-setversion", "nim-delete"], events);
        Assert.False(registration.IsAdded);
    }

    [Fact]
    public void Tray_readd_deletes_a_surviving_icon_before_adding_it_again()
    {
        var events = new List<string>();
        var registration = new TrayIconRegistration(
            () => events.Add("nim-add"),
            () => events.Add("nim-setversion"),
            () => events.Add("nim-delete"));
        registration.Add();
        events.Clear();

        registration.Readd(() => events.Add("rebuild"));

        Assert.Equal(["nim-delete", "rebuild", "nim-add", "nim-setversion"], events);
        Assert.True(registration.IsAdded);
    }

    [Fact]
    public void Failed_tray_version_deletes_the_partial_icon()
    {
        var events = new List<string>();
        var registration = new TrayIconRegistration(
            () => events.Add("nim-add"),
            () =>
            {
                events.Add("nim-setversion");
                throw new InvalidOperationException("version");
            },
            () => events.Add("nim-delete"));

        Assert.Throws<InvalidOperationException>(registration.Add);
        Assert.Equal(["nim-add", "nim-setversion", "nim-delete"], events);
        Assert.False(registration.IsAdded);
    }

    private sealed class FakeNative(List<string>? events = null) : IAppBarNativeApi
    {
        private readonly List<string> _events = events ?? [];

        public int RegisterCount { get; private set; }

        public int RemoveCount { get; private set; }

        public int SetPositionCount { get; private set; }

        public bool FailRegister { get; set; }

        public bool FailWindowPosition { get; set; }

        public bool FailQuery { get; set; }

        public PixelRect MonitorBounds { get; set; } = new(-1920, -100, 0, 1340);
        public uint Dpi { get; set; } = 144;

        public void Register(nint hwnd, uint callbackMessage)
        {
            RegisterCount++;
            _events.Add("abm-new");
            if (FailRegister)
            {
                throw new InvalidOperationException("register");
            }
        }

        public void Remove(nint hwnd)
        {
            RemoveCount++;
            _events.Add("abm-remove");
        }

        public uint GetWindowDpi(nint hwnd) => Dpi;

        public PixelRect GetPrimaryMonitorBounds() => MonitorBounds;

        public void QueryPosition(nint hwnd, BarEdge edge, ref PixelRect bounds)
        {
            _events.Add("abm-querypos");
            if (FailQuery)
            {
                throw new InvalidOperationException("ABM_QUERYPOS failed.");
            }
        }

        public void SetPosition(nint hwnd, BarEdge edge, ref PixelRect bounds)
        {
            SetPositionCount++;
            _events.Add("abm-setpos");
        }

        public void SetWindowPosition(nint hwnd, PixelRect bounds)
        {
            _events.Add("setwindowpos");
            if (FailWindowPosition)
            {
                FailWindowPosition = false;
                throw new InvalidOperationException("setwindowpos");
            }
        }
    }

    private sealed class FakeTray(List<string> events) : ITrayIcon
    {
        public bool FailRecreate { get; init; }

        public bool IsMenuOpen => false;

        public void Start()
        {
        }

        public void Recreate()
        {
            events.Add("nim-add");
            if (FailRecreate)
            {
                throw new InvalidOperationException("nim-add");
            }

            events.Add("nim-setversion");
        }

        public void Remove()
        {
        }

        public void SetAppliedEdge(BarEdge edge)
        {
        }

        public bool ShowStatus(string message) => true;

        public bool HandleWindowMessage(int message, nint wParam, nint lParam) => false;
    }
}
