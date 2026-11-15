using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Windows.Media;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Lifecycle;
using SystemResourceMonitor.Desktop.Settings;
using static SystemResourceMonitor.Desktop.Bar.NativeMethods;

namespace SystemResourceMonitor.Desktop.Bar;

/// <summary>
/// The non-activating AppBar. Its fixed left region holds the CPU, RAM, and GPU sections, followed by a section for
/// each composed (qualified) provider; the centre and right hosts stay empty until B019 and B020. All HWND, tray, placement, and reconciliation members run on its WPF thread.
/// </summary>
public sealed class BarWindow : Window, IShutdownBar
{
    public const int ThicknessDips = 24;
    private const string CallbackMessageName = "SystemResourceMonitor.AppBarCallback";
    private const string TaskbarCreatedMessageName = "TaskbarCreated";
    private const double SectionFontSize = 12;
    private const double SectionGapDips = 8;
    private static readonly TimeSpan FullscreenPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly FontFamily SectionFont = new("Segoe UI");
    private static readonly Typeface SectionTypeface = new(SectionFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Brush SectionForeground = Frozen(new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)));

    private readonly BarEdge _initialEdge;
    private readonly Action<BarEdge> _edgeApplied;
    private readonly Action _closeRequested;
    private readonly Action _shellIntegrationFailed;
    private readonly Action<string>? _record;
    private HwndSourceHook? _hook;
    private AppBarSession? _appBar;
    private ITrayIcon? _tray;
    private ReconciliationCoordinator? _reconciliation;
    private BarReconciliationController? _barReconciliation;
    private FullscreenReconciler? _fullscreen;
    private DispatcherTimer? _fullscreenTimer;
    private nint _hwnd;
    private uint _callbackMessage;
    private uint _taskbarCreatedMessage;
    private bool _finalClose;
    private bool _shuttingDown;
    private MenuReconciliationDeferral? _menuDeferral;
    private readonly TextBlock _cpuSection = CreateSection();
    private readonly TextBlock _ramSection = CreateSection();
    private readonly TextBlock _gpuSection = CreateSection();
    private readonly TextBlock _codexSection = CreateSection();
    private readonly TextBlock _claudeSection = CreateSection();
    private readonly StackPanel _leftRegion = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Left,
        Focusable = false,
    };
    private readonly Grid _centerRegion = new() { Focusable = false, ClipToBounds = true };
    private readonly Grid _rightRegion = new() { Focusable = false, ClipToBounds = true };
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _codexCountdown;
    private readonly DispatcherTimer _claudeCountdown;
    private CodexLimitState _codex = CodexLimitsUnavailable.Initial;
    private ClaudeLimitState _claude = ClaudeLimitsUnavailable.Initial;

    public BarWindow(
        BarEdge initialEdge,
        Action<BarEdge> edgeApplied,
        Action closeRequested,
        Action shellIntegrationFailed,
        bool showCodex,
        bool showClaude,
        Action<string>? record = null,
        TimeProvider? time = null)
    {
        _initialEdge = initialEdge;
        _edgeApplied = edgeApplied;
        _closeRequested = closeRequested;
        _shellIntegrationFailed = shellIntegrationFailed;
        _record = record;
        _time = time ?? TimeProvider.System;
        _codexCountdown = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _codexCountdown.Tick += (_, _) => RenderCodex();
        _claudeCountdown = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        _claudeCountdown.Tick += (_, _) => RenderClaude();
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        AllowsTransparency = false;
        Topmost = true;
        Focusable = false;
        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
        UseLayoutRounding = true;
        _leftRegion.Children.Add(_cpuSection);
        _leftRegion.Children.Add(_ramSection);
        _leftRegion.Children.Add(_gpuSection);
        // An unqualified provider is not composed and has no section; a composed provider keeps its section even
        // when its client is missing or its reads fail.
        if (showCodex)
        {
            _leftRegion.Children.Add(_codexSection);
        }

        if (showClaude)
        {
            _leftRegion.Children.Add(_claudeSection);
        }

        Content = CreateRegions();
        ShowMetrics(DashboardState.Initial);
        ShowCodex(CodexLimitsUnavailable.Initial);
        ShowClaude(ClaudeLimitsUnavailable.Initial);

        // Create the handle on the primary monitor, which always contains (0, 0).
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
    }

    public int ThicknessPixels => _appBar?.ThicknessPixels ?? 0;

    public PixelRect ReservedBounds => _appBar?.ReservedBounds ?? default;

    public BarEdge AppliedEdge => _appBar?.AppliedEdge ?? _initialEdge;

    public nint Handle => _hwnd;

    /// <summary>The fixed left region holding the metric and provider sections.</summary>
    internal StackPanel LeftRegion => _leftRegion;

    /// <summary>The flexible centre host; empty in the published app until B019.</summary>
    internal Grid CenterRegion => _centerRegion;

    /// <summary>The right-hand host; empty in the published app until B020.</summary>
    internal Grid RightRegion => _rightRegion;

    internal TextBlock CpuSection => _cpuSection;

    internal TextBlock RamSection => _ramSection;

    internal TextBlock GpuSection => _gpuSection;

    /// <summary>The Codex section control; it is in the bar only when Codex is composed.</summary>
    internal TextBlock CodexSection => _codexSection;

    /// <summary>The Claude section control; it is in the bar only when Claude is composed.</summary>
    internal TextBlock ClaudeSection => _claudeSection;

    /// <summary>Registers and positions the AppBar, then adds and versions the tray icon.</summary>
    public void Start()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        // No WS_EX_TOOLWINDOW: with it, the bar kept its start DPI after a live scale change (observed, ADR 0027).
        // ShowInTaskbar = false gives the window a hidden owner instead; KeepReservation stops the shell moving it.
        AddExtendedStyle(WS_EX_NOACTIVATE);
        FixSectionWidths(VisualTreeHelper.GetDpi(this).PixelsPerDip);

        _callbackMessage = RegisterRequiredMessage(CallbackMessageName);
        _taskbarCreatedMessage = RegisterRequiredMessage(TaskbarCreatedMessageName);
        _hook = OnWindowMessage;
        var source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("The bar has no HwndSource.");
        source.AddHook(_hook);

        _appBar = new AppBarSession(_hwnd, _callbackMessage, new Win32AppBarNativeApi());
        _fullscreen = new FullscreenReconciler(
            _hwnd,
            new Win32FullscreenStateReader(),
            new Win32BarZOrderNativeApi());
        _reconciliation = new ReconciliationCoordinator(
            action => Dispatcher.BeginInvoke(action),
            Reconcile);
        _menuDeferral = new MenuReconciliationDeferral(() => _tray?.IsMenuOpen == true, _reconciliation.Request);
        _appBar.Register();
        _appBar.Place(_initialEdge);
        Show();

        _tray = new NativeTrayIcon(
            _hwnd,
            edge => SelectEdge(edge),
            _closeRequested,
            () => _menuDeferral!.MenuClosed(),
            new TrayMenuOwner(new Win32TrayMenuNativeApi()));
        _tray.SetAppliedEdge(_initialEdge);
        _tray.Start();
        _barReconciliation = new BarReconciliationController(
            _appBar,
            _tray,
            _fullscreen,
            _shellIntegrationFailed,
            ReportStatus,
            _record);
        _fullscreenTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = FullscreenPollInterval,
        };
        _fullscreenTimer.Tick += OnFullscreenTimerTick;
        _fullscreenTimer.Start();
        _reconciliation.Request(ReconciliationReason.Fullscreen);
    }

    /// <summary>Applies an edge transaction and persists only a successfully applied edge.</summary>
    public bool SelectEdge(BarEdge edge)
    {
        Dispatcher.VerifyAccess();
        if (_appBar is null || _tray is null)
        {
            throw new InvalidOperationException("The bar has not started.");
        }

        if (_shuttingDown)
        {
            // A menu command can return after shutdown ran inside the menu's modal loop.
            return false;
        }

        if (_appBar.AppliedEdge == edge && _barReconciliation!.IsAppBarReady)
        {
            return true;
        }

        try
        {
            _appBar.Place(edge);
            if (_barReconciliation!.PlacementSucceeded())
            {
                _reconciliation!.Request(ReconciliationReason.Fullscreen);
            }
        }
        catch (Exception exception)
        {
            _record?.Invoke($"placement-failed:{exception.GetType().Name}");
            ReportStatus("The bar could not change position.");
            return false;
        }

        try
        {
            _tray.SetAppliedEdge(edge);
        }
        catch (Exception exception)
        {
            _record?.Invoke($"tray-check-failed:{exception.GetType().Name}");
            ReportStatus("The tray could not update its position check.");
        }

        _record?.Invoke($"edge-applied:{edge}");
        _edgeApplied(edge);
        return true;
    }

    /// <summary>Updates section text and tooltips only; section widths never depend on the text.</summary>
    public void ShowMetrics(DashboardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Dispatcher.VerifyAccess();
        Show(_cpuSection, MetricSectionFormat.Format(state.Cpu));
        Show(_ramSection, MetricSectionFormat.Format(state.Ram));
        Show(_gpuSection, MetricSectionFormat.Format(state.Gpu, state.GpuAdapter));

        static void Show(TextBlock section, MetricSection content)
        {
            section.Text = content.Text;
            section.ToolTip = content.ToolTip;
        }
    }

    /// <summary>
    /// Shows the Codex limits and keeps their 5-hour countdown current: a one-shot timer re-renders the section when
    /// the displayed whole minute changes, without waiting for the next poll.
    /// </summary>
    public void ShowCodex(CodexLimitState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Dispatcher.VerifyAccess();
        _codex = state;
        RenderCodex();
    }

    public void ShowClaude(ClaudeLimitState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Dispatcher.VerifyAccess();
        _claude = state;
        RenderClaude();
    }

    public void ReportStatus(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => ReportStatus(message));
            return;
        }

        try
        {
            if (_tray?.ShowStatus(message) != true)
            {
                _record?.Invoke("status-unavailable");
            }
        }
        catch (Exception exception)
        {
            _record?.Invoke($"status-failed:{exception.GetType().Name}");
        }
    }

    public void RemoveAppBar()
    {
        _shuttingDown = true;
        _fullscreenTimer?.Stop();
        _codexCountdown.Stop();
        _claudeCountdown.Stop();
        _appBar?.Remove();
    }

    public void RemoveTrayIcon() => _tray?.Remove();

    public void CloseFinal()
    {
        _finalClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_finalClose)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(_closeRequested);
        }

        base.OnClosing(e);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _record?.Invoke($"dpi-changed:{oldDpi.PixelsPerInchX}->{newDpi.PixelsPerInchX}");
        FixSectionWidths(newDpi.PixelsPerDip);
    }

    /// <summary>
    /// Fixed left sections, a flexible centre, and a right-hand host. The Auto left column always gets the sections'
    /// fixed widths, so content in the centre or right never moves or resizes them; each host clips to its column, and
    /// the right host never extends past the bar.
    /// </summary>
    private Grid CreateRegions()
    {
        var regions = new Grid { Focusable = false };
        regions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        regions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        regions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_leftRegion, 0);
        Grid.SetColumn(_centerRegion, 1);
        Grid.SetColumn(_rightRegion, 2);
        regions.Children.Add(_leftRegion);
        regions.Children.Add(_centerRegion);
        regions.Children.Add(_rightRegion);
        // An Auto column takes its content's full desired width, so cap the right host at the space the fixed left
        // region leaves; wider right content is clipped inside the bar instead of extending past it.
        void CapRightHost() => _rightRegion.MaxWidth = Math.Max(0, regions.ActualWidth - _leftRegion.ActualWidth);
        regions.SizeChanged += (_, _) => CapRightHost();
        _leftRegion.SizeChanged += (_, _) => CapRightHost();
        return regions;
    }

    private static TextBlock CreateSection() => new()
    {
        FontFamily = SectionFont,
        FontSize = SectionFontSize,
        Foreground = SectionForeground,
        // Transparent, not null, so hovering anywhere in the fixed-width section shows its tooltip.
        Background = Brushes.Transparent,
        Margin = new Thickness(SectionGapDips, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Left,
        TextTrimming = TextTrimming.None,
        TextWrapping = TextWrapping.NoWrap,
        Focusable = false,
    };

    /// <summary>Fixes each section to the widest text it can show, rounded up to whole device pixels.</summary>
    private void FixSectionWidths(double pixelsPerDip)
    {
        _cpuSection.Width = WidestCandidate(MetricSectionFormat.WidthCandidates(MetricKind.Cpu), pixelsPerDip);
        _ramSection.Width = WidestCandidate(MetricSectionFormat.WidthCandidates(MetricKind.Ram), pixelsPerDip);
        _gpuSection.Width = WidestCandidate(MetricSectionFormat.WidthCandidates(MetricKind.Gpu), pixelsPerDip);
        _codexSection.Width = WidestCandidate(CodexSectionFormat.WidthCandidates(), pixelsPerDip);
        _claudeSection.Width = WidestCandidate(ClaudeSectionFormat.WidthCandidates(), pixelsPerDip);
    }

    /// <summary>The section width for <paramref name="candidates"/> at <paramref name="pixelsPerDip"/>, in DIPs.</summary>
    internal static double WidestCandidate(IEnumerable<string> candidates, double pixelsPerDip) =>
        Math.Ceiling(candidates.Max(text => MeasureWidth(text, pixelsPerDip)) * pixelsPerDip) / pixelsPerDip;

    /// <summary>The rendered width of <paramref name="text"/> in the section font, in DIPs.</summary>
    internal static double MeasureWidth(string text, double pixelsPerDip) => new FormattedText(
        text,
        CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight,
        SectionTypeface,
        SectionFontSize,
        SectionForeground,
        pixelsPerDip).WidthIncludingTrailingWhitespace;

    private void RenderCodex()
    {
        _codexCountdown.Stop();
        if (_shuttingDown)
        {
            return;
        }

        var now = _time.GetUtcNow();
        var content = CodexSectionFormat.Format(_codex, now, TimeZoneInfo.Local);
        _codexSection.Text = content.Text;
        _codexSection.ToolTip = content.ToolTip;
        if (CodexSectionFormat.UntilCountdownChanges(_codex, now) is { } untilChange)
        {
            _codexCountdown.Interval = untilChange;
            _codexCountdown.Start();
        }
    }

    private void RenderClaude()
    {
        _claudeCountdown.Stop();
        if (_shuttingDown) return;
        var now = _time.GetUtcNow();
        var content = ClaudeSectionFormat.Format(_claude, now, TimeZoneInfo.Local);
        _claudeSection.Text = content.Text;
        _claudeSection.ToolTip = content.ToolTip;
        if (ClaudeSectionFormat.UntilCountdownChanges(_claude, now) is { } untilChange)
        {
            _claudeCountdown.Interval = untilChange;
            _claudeCountdown.Start();
        }
    }

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void Reconcile(ReconciliationReason reason)
    {
        if (_shuttingDown || _barReconciliation is null)
        {
            return;
        }

        if (_menuDeferral!.TryDefer(reason))
        {
            return;
        }

        _barReconciliation.Reconcile(reason, AppliedEdge);
    }

    private void OnFullscreenTimerTick(object? sender, EventArgs e)
    {
        if (!_shuttingDown)
        {
            _reconciliation?.Request(ReconciliationReason.Fullscreen);
        }
    }

    private void AddExtendedStyle(nint flags)
    {
        // SetLastError stubs clear the error before each call, so zero with no error is a valid style.
        var style = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        if (style == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "GetWindowLongPtr failed.");
        }

        if (SetWindowLongPtr(_hwnd, GWL_EXSTYLE, style | flags) == 0 && Marshal.GetLastPInvokeError() != 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetWindowLongPtr failed.");
        }
    }

    private nint OnWindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return MA_NOACTIVATE;
        }

        if (msg == WM_WINDOWPOSCHANGING)
        {
            KeepReservation(lParam);
        }

        try
        {
            if (_tray?.HandleWindowMessage(msg, wParam, lParam) == true)
            {
                handled = true;
                return 0;
            }
        }
        catch (Exception exception)
        {
            _record?.Invoke($"tray-action-failed:{exception.GetType().Name}");
            ReportStatus("The tray action failed.");
            handled = true;
            return 0;
        }

        var reason = _shuttingDown
            ? ReconciliationReason.None
            : ShellMessageClassifier.Classify(msg, wParam, _callbackMessage, _taskbarCreatedMessage);
        _reconciliation?.Request(reason);
        if ((uint)msg == _taskbarCreatedMessage || (uint)msg == _callbackMessage)
        {
            handled = true;
        }

        return 0;
    }

    /// <summary>Rewrites a move or resize that would leave the AppBar reservation back onto it.</summary>
    private void KeepReservation(nint lParam)
    {
        if (_appBar is null || _shuttingDown)
        {
            return;
        }

        var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        var current = DesktopRect();
        var requested = new PixelRect(
            (pos.flags & SWP_NOMOVE) != 0 ? current.Left : pos.x,
            (pos.flags & SWP_NOMOVE) != 0 ? current.Top : pos.y,
            ((pos.flags & SWP_NOMOVE) != 0 ? current.Left : pos.x) + ((pos.flags & SWP_NOSIZE) != 0 ? current.Width : pos.cx),
            ((pos.flags & SWP_NOMOVE) != 0 ? current.Top : pos.y) + ((pos.flags & SWP_NOSIZE) != 0 ? current.Height : pos.cy));
        if (_appBar.CorrectForeignMove(requested) is not { } reserved)
        {
            return;
        }

        pos.x = reserved.Left;
        pos.y = reserved.Top;
        pos.cx = reserved.Width;
        pos.cy = reserved.Height;
        pos.flags &= ~(SWP_NOMOVE | SWP_NOSIZE);
        Marshal.StructureToPtr(pos, lParam, false);
        _record?.Invoke("foreign-move-corrected");
    }

    private PixelRect DesktopRect() =>
        GetWindowRect(_hwnd, out var rect) ? rect : ReservedBounds;

    private static uint RegisterRequiredMessage(string name)
    {
        var message = RegisterWindowMessage(name);
        return message == 0
            ? throw new Win32Exception(Marshal.GetLastPInvokeError(), $"RegisterWindowMessage failed for {name}.")
            : message;
    }
}
