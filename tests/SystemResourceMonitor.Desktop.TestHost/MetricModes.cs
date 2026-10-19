using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Adapters.Metrics;
using SystemResourceMonitor.Desktop.Bar;
using SystemResourceMonitor.Desktop.Lifecycle;

namespace SystemResourceMonitor.Desktop.TestHost;

/// <summary>B06 desktop and fault evidence modes. They compose fake sources through the production startup seam.</summary>
public static partial class Program
{
    private static readonly TimeSpan ExpiryTolerance = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaximumUpdateGap = TimeSpan.FromMilliseconds(2_500);

    /// <summary>
    /// One source returns three values and then blocks forever; the other keeps updating. The blocked section must show
    /// the dash within <see cref="ExpiryTolerance"/> of its last successful timestamp plus the expiry age, the other
    /// section must keep changing, and exit with the read still blocked must stay within the shutdown deadline.
    /// </summary>
    private static int MetricFreshness(MetricKind blocked)
    {
        const int SuccessfulReads = 3;
        var blockedSource = new ScriptedSource(blocked, read => read < SuccessfulReads ? ScriptedSource.Measured(blocked, 10 * (read + 1)) : null);
        IMetricSource OtherSource(MetricKind kind) => new ScriptedSource(kind, read => ScriptedSource.Measured(kind, 20 + read % 50));
        var cpuSource = blocked == MetricKind.Cpu ? blockedSource : OtherSource(MetricKind.Cpu);
        var ramSource = blocked == MetricKind.Ram ? blockedSource : OtherSource(MetricKind.Ram);
        var gpuSource = blocked == MetricKind.Gpu ? blockedSource : OtherSource(MetricKind.Gpu);
        var run = new LifecycleRun(sources: (cpuSource, ramSource, gpuSource));
        var otherKinds = Enum.GetValues<MetricKind>().Where(kind => kind != blocked).ToArray();
        var otherChanges = otherKinds.ToDictionary(kind => kind, _ => new List<TimeSpan>());
        var blockedChanges = 0;
        TimeSpan? expiryOffset = null;
        TimeSpan? expiredAt = null;

        run.Run((bar, shutdown) =>
        {
            run.CheckPositionedBar(bar);
            var blockedSection = Section(bar, blocked);
            var otherSections = otherKinds.ToDictionary(kind => kind, kind => Section(bar, kind));
            var blockedText = blockedSection.Text;
            var otherTexts = otherSections.ToDictionary(pair => pair.Key, pair => pair.Value.Text);

            // Observe the rendered text once per frame, without a hook in the bar. The deadline starts at the blocked
            // source's last successful return, which the monitor stamps immediately afterwards.
            void OnRendering(object? sender, EventArgs e)
            {
                var now = Stopwatch.GetTimestamp();
                if (blockedSection.Text != blockedText)
                {
                    blockedText = blockedSection.Text;
                    blockedChanges++;
                    run.Record($"shown:{blocked}:{blockedText}");
                    if (expiryOffset is null && blockedText.EndsWith(MetricSectionFormat.Unavailable, StringComparison.Ordinal) && blockedSource.LastSuccess is { } last)
                    {
                        expiryOffset = Stopwatch.GetElapsedTime(last, now) - MetricMonitor.ExpiryAge;
                        expiredAt = run.Now;
                        run.Check(
                            (blockedSection.ToolTip as string)?.EndsWith("No update for 6 seconds.", StringComparison.Ordinal) == true,
                            $"the expired {blocked} tooltip is '{blockedSection.ToolTip}'");
                    }
                }

                foreach (var (kind, section) in otherSections)
                {
                    if (section.Text != otherTexts[kind])
                    {
                        otherTexts[kind] = section.Text;
                        otherChanges[kind].Add(run.Now);
                        run.Record($"shown:{kind}:{section.Text}");
                    }
                }
            }

            CompositionTarget.Rendering += OnRendering;
            _ = ObserveAsync();

            async Task ObserveAsync()
            {
                try
                {
                    var deadline = run.Now + TimeSpan.FromSeconds(30);
                    while (expiredAt is null && run.Now < deadline)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(50));
                    }

                    // Keep observing the other section after the expiry, then exit with the read still blocked.
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
                catch (Exception exception)
                {
                    run.Check(false, $"freshness probe failed with {exception.GetType().Name}");
                }
                finally
                {
                    CompositionTarget.Rendering -= OnRendering;
                    run.Check(DesktopProbe.RequestClose(bar.Handle), "WM_CLOSE was not processed after the freshness probe");
                }
            }
        });

        run.Check(blockedChanges > SuccessfulReads, $"the blocked {blocked} section changed {blockedChanges} times");
        if (expiryOffset is { } offset)
        {
            LifecycleRun.Info($"expiry-offset-ms {offset.TotalMilliseconds:F0}");
            run.Check(offset.Duration() <= ExpiryTolerance, $"{blocked} expired {offset.TotalMilliseconds:F0} ms from its deadline");
        }
        else
        {
            run.Check(false, $"the blocked {blocked} section never showed the dash");
        }

        foreach (var kind in otherKinds)
        {
            var changes = otherChanges[kind];
            var gaps = changes.Zip(changes.Skip(1), (previous, next) => next - previous).ToList();
            LifecycleRun.Info($"{kind}-changes {changes.Count}; max-gap-ms {(gaps.Count == 0 ? 0 : gaps.Max().TotalMilliseconds):F0}");
            run.Check(expiredAt is { } expired && changes.Count(at => at < expired) >= 2 && changes.Count(at => at > expired) >= 2,
                $"the {kind} section did not keep changing before and after the expiry");
            run.Check(gaps.All(gap => gap <= MaximumUpdateGap), $"the {kind} section paused longer than {MaximumUpdateGap.TotalMilliseconds:F0} ms");
        }

        var waited = run.Between("wait-start", "exit");
        LifecycleRun.Info($"wait-ms {waited.TotalMilliseconds:F0}");
        run.Check(waited <= ShutdownCoordinator.WaitDeadline, $"waited {waited.TotalMilliseconds:F0} ms with a blocked read");
        run.Check(run.MarkerCount("pending:metric-monitor") == 0, "the metric monitor was still pending at the deadline");
        run.Check(run.ExitCode == ExitCodes.Success, $"coordinator exit code {run.ExitCode}, expected {ExitCodes.Success}");
        run.CheckOrder("bar-positioned", "metrics-started", "remove", "restored", "wait-start", "exit");
        return run.Finish();
    }

    /// <summary>
    /// Shows 0%, 100%, and every unavailable reason in turn, holding each for <paramref name="hold"/>. In every state,
    /// all sections keep their fixed widths, their text fits unclipped within the width and the bar's height, and the
    /// reserved strip still matches the rendered bar.
    /// </summary>
    private static int MetricDisplay(TimeSpan hold)
    {
        var run = new LifecycleRun(sources: (ScriptedSource.Blocked(MetricKind.Cpu), ScriptedSource.Blocked(MetricKind.Ram), ScriptedSource.Steady(MetricKind.Gpu)));
        run.Run((bar, shutdown) =>
        {
            run.CheckPositionedBar(bar);
            _ = CycleAsync();

            async Task CycleAsync()
            {
                try
                {
                    var dpi = VisualTreeHelper.GetDpi(bar);
                    var cpuWidth = Section(bar, MetricKind.Cpu).Width;
                    var ramWidth = Section(bar, MetricKind.Ram).Width;
                    var gpuWidth = Section(bar, MetricKind.Gpu).Width;
                    LifecycleRun.Info($"dpi-scale {dpi.DpiScaleX.ToString("P0", CultureInfo.InvariantCulture)}");
                    LifecycleRun.Info($"section-widths CPU {cpuWidth:F2} DIP, RAM {ramWidth:F2} DIP, GPU {gpuWidth:F2} DIP; bar height {bar.ActualHeight:F2} DIP");
                    foreach (var (name, state) in DisplayStates())
                    {
                        run.Check(shutdown.TryPublish(() => bar.ShowMetrics(state)), $"the {name} state could not publish");
                        bar.UpdateLayout();
                        await bar.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                        run.Record($"display:{name}");
                        CheckSection(run, bar, Section(bar, MetricKind.Cpu), cpuWidth, name);
                        CheckSection(run, bar, Section(bar, MetricKind.Ram), ramWidth, name);
                        CheckSection(run, bar, Section(bar, MetricKind.Gpu), gpuWidth, name);
                        run.CheckPositionedBar(bar);
                        await Task.Delay(hold);
                    }
                }
                catch (Exception exception)
                {
                    run.Check(false, $"display probe failed with {exception.GetType().Name}");
                }
                finally
                {
                    run.Check(DesktopProbe.RequestClose(bar.Handle), "WM_CLOSE was not processed after the display probe");
                }
            }
        });

        run.CheckOrder("bar-positioned", "metrics-started", "display:0%", "display:100%", "display:ReadFailed", "remove", "restored");
        return run.Finish();
    }

    /// <summary>
    /// Calls the production native wrappers every 2 seconds on thread-pool workers for <paramref name="duration"/> and
    /// fails on any failed call or on two consecutive calls of one reader over 2 seconds.
    /// </summary>
    private static int NativeReads(TimeSpan duration)
    {
        var cpu = new Win32SystemTimesApi();
        var ram = new Win32MemoryStatusApi();
        using var gpu = new GpuLoadSource(new DxCoreAdapterApi(), new PdhGpuCounterApi());
        var results = Task.WhenAll(
            MeasureAsync("GetSystemTimes", () => cpu.TryRead(out _, out _), duration),
            MeasureAsync("GlobalMemoryStatusEx", () => ram.TryRead(out _, out _, out _), duration),
            MeasureAsync("GpuLoadSource.Read", () => { _ = gpu.Read(); return true; }, duration)).GetAwaiter().GetResult();

        var passed = true;
        foreach (var result in results)
        {
            Console.WriteLine(
                $"info: {result.Name} calls {result.Calls}; failed {result.Failed}; max-ms {result.Maximum.TotalMilliseconds:F3}; " +
                $"over-2s {result.Overruns}; longest-overrun-run {result.LongestOverrunRun}");
            if (result.LongestOverrunRun >= 2)
            {
                Console.WriteLine($"FAIL: {result.Name} overran 2 seconds on {result.LongestOverrunRun} consecutive calls");
                passed = false;
            }

            if (result.Failed > 0)
            {
                Console.WriteLine($"FAIL: {result.Name} failed on {result.Failed} of {result.Calls} calls");
                passed = false;
            }
        }

        Console.WriteLine(passed ? "PASS" : "FAILED");
        return passed ? Success : AssertionFailed;
    }

    private sealed record ReadStatistics(string Name, int Calls, int Failed, TimeSpan Maximum, int Overruns, int LongestOverrunRun);

    private static async Task<ReadStatistics> MeasureAsync(string name, Func<bool> read, TimeSpan duration)
    {
        var interval = MetricMonitor.PollInterval;
        var clock = Stopwatch.StartNew();
        var (calls, failed, overruns, run, longest) = (0, 0, 0, 0, 0);
        var maximum = TimeSpan.Zero;
        var due = TimeSpan.Zero;
        while (clock.Elapsed < duration)
        {
            var started = Stopwatch.GetTimestamp();
            var succeeded = await Task.Run(read).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(started);
            calls++;
            failed += succeeded ? 0 : 1;
            maximum = TimeSpan.FromTicks(Math.Max(maximum.Ticks, elapsed.Ticks));
            var overran = elapsed > interval;
            run = overran ? run + 1 : 0;
            overruns += overran ? 1 : 0;
            longest = Math.Max(longest, run);

            due += interval;
            var wait = due - clock.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait).ConfigureAwait(false);
            }
        }

        return new ReadStatistics(name, calls, failed, maximum, overruns, longest);
    }

    private static IEnumerable<(string Name, DashboardState State)> DisplayStates()
    {
        foreach (var percentage in new[] { 0, 100 })
        {
            yield return ($"{percentage}%", DashboardState.Initial
                .With(Available(MetricKind.Cpu, percentage))
                .With(Available(MetricKind.Ram, percentage))
                .WithGpu(Available(MetricKind.Gpu, percentage), ScriptedSource.TestAdapter));
        }

        foreach (var reason in new[] { UnavailabilityReason.NotYetSampled, UnavailabilityReason.Expired, UnavailabilityReason.Invalid, UnavailabilityReason.ReadFailed })
        {
            yield return (reason.ToString(), DashboardState.Initial
                .With(new UnavailableMetric(MetricKind.Cpu, reason))
                .With(new UnavailableMetric(MetricKind.Ram, reason))
                .WithGpu(new UnavailableMetric(MetricKind.Gpu, reason), null));
        }

        foreach (var reason in new[] { UnavailabilityReason.NotYetSampled, UnavailabilityReason.Expired, UnavailabilityReason.Invalid,
                     UnavailabilityReason.ReadFailed, UnavailabilityReason.Uncorrelated })
        {
            yield return ($"{reason}-adapter", DashboardState.Initial.WithGpu(
                new UnavailableMetric(MetricKind.Gpu, reason), ScriptedSource.TestAdapter));
        }

        foreach (var reason in new[] { UnavailabilityReason.NoDiscreteAdapter, UnavailabilityReason.AmbiguousAdapter })
        {
            yield return (reason.ToString(), DashboardState.Initial.WithGpu(new UnavailableMetric(MetricKind.Gpu, reason), null));
        }

        static MetricReading Available(MetricKind kind, double percentage) => AvailableMetric.Create(kind,
            ScriptedSource.Measured(kind, percentage), DateTimeOffset.UtcNow, TimeProvider.System.GetTimestamp());
    }

    private static void CheckSection(LifecycleRun run, BarWindow bar, TextBlock section, double fixedWidth, string state)
    {
        // Measure the shown text without the section's width constraint, so clipping cannot hide in DesiredSize, and at
        // the bar's per-monitor DPI rather than the system DPI.
        var text = new FormattedText(
            section.Text,
            CultureInfo.InvariantCulture,
            section.FlowDirection,
            new Typeface(section.FontFamily, section.FontStyle, section.FontWeight, section.FontStretch),
            section.FontSize,
            section.Foreground,
            VisualTreeHelper.GetDpi(bar).PixelsPerDip);
        var natural = new Size(text.WidthIncludingTrailingWhitespace, text.Height);
        var right = section.TranslatePoint(new Point(section.ActualWidth, 0), bar).X;
        LifecycleRun.Info(
            $"{state} '{section.Text}' natural {natural.Width:F2}x{natural.Height:F2}, section {section.ActualWidth:F2}x{section.ActualHeight:F2}, right {right:F2} of {bar.ActualWidth:F2}");
        run.Check(section.Width == fixedWidth && section.ActualWidth == fixedWidth, $"{state}: '{section.Text}' changed its section width");
        run.Check(natural.Width <= fixedWidth, $"{state}: '{section.Text}' is {natural.Width:F2} DIP wide in a {fixedWidth:F2} DIP section");
        run.Check(natural.Height <= bar.ActualHeight && section.ActualHeight <= bar.ActualHeight, $"{state}: '{section.Text}' is taller than the bar");
        run.Check(right <= bar.ActualWidth, $"{state}: '{section.Text}' ends outside the bar");
    }

    private static TextBlock Section(BarWindow bar, MetricKind kind) => kind switch
    {
        MetricKind.Cpu => LifecycleRun.Sections(bar).Cpu,
        MetricKind.Ram => LifecycleRun.Sections(bar).Ram,
        _ => LifecycleRun.Sections(bar).Gpu,
    };

    private static bool TryParseSeconds(string text, out TimeSpan duration)
    {
        var parsed = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds > 0;
        duration = parsed ? TimeSpan.FromSeconds(seconds) : default;
        return parsed;
    }
}
