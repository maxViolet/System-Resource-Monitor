using System.Diagnostics;
using System.Windows.Media;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Bar;
using SystemResourceMonitor.Desktop.Lifecycle;

namespace SystemResourceMonitor.Desktop.TestHost;

/// <summary>B013 independence evidence through the production startup seam.</summary>
public static partial class Program
{
    /// <summary>The limit monitors' clock runs this many times faster than real time.</summary>
    private const int LimitClockSpeed = 60;

    private static readonly TimeSpan LimitExpiryTolerance = TimeSpan.FromMilliseconds(750);

    /// <summary>
    /// The Claude source blocks on its first read until shutdown. CPU, RAM, and GPU keep changing; Codex succeeds once,
    /// then fails with a network error, turns stale, and expires three of its accelerated minutes after the success,
    /// all while Claude's read is still pending. Exit with the read blocked must stay within the shutdown deadline.
    /// </summary>
    private static int ClaudeIndependence()
    {
        var limitClock = new AcceleratedTimeProvider(LimitClockSpeed);
        var claudeSource = new BlockedClaudeSource();
        var codexSource = new OnceCodexSource(limitClock);
        IMetricSource Changing(MetricKind kind) => new ScriptedSource(kind, read => ScriptedSource.Measured(kind, 20 + read % 50));
        var run = new LifecycleRun(
            sources: (Changing(MetricKind.Cpu), Changing(MetricKind.Ram), Changing(MetricKind.Gpu)),
            codex: new CodexLimitMonitor(codexSource, limitClock),
            claude: new ClaudeLimitMonitor(claudeSource, limitClock));
        var metricChanges = Enum.GetValues<MetricKind>().ToDictionary(kind => kind, _ => new List<TimeSpan>());
        var codexTexts = new List<string>();
        var claudeTexts = new List<string>();
        TimeSpan? codexExpiredAt = null;

        run.Run((bar, shutdown) =>
        {
            run.CheckPositionedBar(bar);
            var (cpu, ram, gpu) = LifecycleRun.Sections(bar);
            var (codex, claude) = LifecycleRun.ProviderSections(bar);
            var metricSections = new Dictionary<MetricKind, System.Windows.Controls.TextBlock>
            {
                [MetricKind.Cpu] = cpu,
                [MetricKind.Ram] = ram,
                [MetricKind.Gpu] = gpu,
            };
            var metricTexts = metricSections.ToDictionary(pair => pair.Key, pair => pair.Value.Text);
            codexTexts.Add(codex.Text);
            claudeTexts.Add(claude.Text);

            // Observe the rendered text once per frame, without a hook in the bar.
            void OnRendering(object? sender, EventArgs e)
            {
                foreach (var (kind, section) in metricSections)
                {
                    if (section.Text != metricTexts[kind])
                    {
                        metricTexts[kind] = section.Text;
                        metricChanges[kind].Add(run.Now);
                    }
                }

                if (codex.Text != codexTexts[^1])
                {
                    codexTexts.Add(codex.Text);
                    run.Record($"shown:codex:{codex.Text}");
                    if (codexExpiredAt is null && codex.Text == "Codex: 5h -- | W --" && codexTexts.Count > 1)
                        codexExpiredAt = run.Now;
                }

                if (claude.Text != claudeTexts[^1])
                {
                    claudeTexts.Add(claude.Text);
                    run.Record($"shown:claude:{claude.Text}");
                }
            }

            CompositionTarget.Rendering += OnRendering;
            _ = ObserveAsync();

            async Task ObserveAsync()
            {
                try
                {
                    var deadline = run.Now + TimeSpan.FromSeconds(30);
                    while (codexExpiredAt is null && run.Now < deadline)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(50));
                    }

                    // Keep observing after the Codex expiry, then exit with the Claude read still blocked.
                    await Task.Delay(TimeSpan.FromSeconds(3));
                }
                catch (Exception exception)
                {
                    run.Check(false, $"independence probe failed with {exception.GetType().Name}");
                }
                finally
                {
                    CompositionTarget.Rendering -= OnRendering;
                    run.Check(DesktopProbe.RequestClose(bar.Handle), "WM_CLOSE was not processed after the independence probe");
                }
            }
        });

        run.Check(claudeSource.Reads == 1, $"the blocked Claude source was read {claudeSource.Reads} times");
        run.Check(claudeSource.Cancelled, "shutdown did not cancel the blocked Claude read");
        run.Check(claudeTexts.SequenceEqual(["Claude: 5h -- | W --"]), $"the Claude section showed [{string.Join(", ", claudeTexts)}]");
        run.Check(codexTexts.Count >= 4 && codexTexts[1].StartsWith("Codex: 5h 24% in ", StringComparison.Ordinal)
            && codexTexts[2].StartsWith("Codex (stale): ", StringComparison.Ordinal),
            $"the Codex section showed [{string.Join(", ", codexTexts)}]");

        if (codexExpiredAt is { } expiredAt && codexSource.SucceededAt is { } succeededAt)
        {
            var expected = succeededAt + CodexLimitMonitor.StaleLimit / LimitClockSpeed;
            var offset = expiredAt - expected;
            LifecycleRun.Info($"codex-expiry-offset-ms {offset.TotalMilliseconds:F0}");
            run.Check(offset.Duration() <= LimitExpiryTolerance, $"Codex expired {offset.TotalMilliseconds:F0} ms from its deadline");
        }
        else
        {
            run.Check(false, "the Codex section never expired");
        }

        foreach (var (kind, changes) in metricChanges)
        {
            var gaps = changes.Zip(changes.Skip(1), (previous, next) => next - previous).ToList();
            LifecycleRun.Info($"{kind}-changes {changes.Count}; max-gap-ms {(gaps.Count == 0 ? 0 : gaps.Max().TotalMilliseconds):F0}");
            run.Check(codexExpiredAt is { } expired && changes.Count(at => at < expired) >= 2 && changes.Count(at => at > expired) >= 2,
                $"the {kind} section did not keep changing before and after the Codex expiry");
            run.Check(gaps.All(gap => gap <= MaximumUpdateGap), $"the {kind} section paused longer than {MaximumUpdateGap.TotalMilliseconds:F0} ms");
        }

        var waited = run.Between("wait-start", "exit");
        LifecycleRun.Info($"wait-ms {waited.TotalMilliseconds:F0}");
        run.Check(waited <= ShutdownCoordinator.WaitDeadline, $"waited {waited.TotalMilliseconds:F0} ms with a blocked Claude read");
        run.Check(run.MarkerCount("pending:claude-limits") == 0, "the Claude monitor was still pending at the deadline");
        run.Check(run.ExitCode == ExitCodes.Success, $"coordinator exit code {run.ExitCode}, expected {ExitCodes.Success}");
        run.CheckOrder("bar-positioned", "metrics-started", "codex-started", "claude-started", "remove", "restored", "wait-start", "exit");
        return run.Finish();
    }

    /// <summary>Blocks its first read until cancellation.</summary>
    private sealed class BlockedClaudeSource : IClaudeLimitSource
    {
        private int _reads;
        private volatile bool _cancelled;

        public int Reads => Volatile.Read(ref _reads);

        public bool Cancelled => _cancelled;

        public async Task<ClaudeReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _reads);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _cancelled = true;
                throw;
            }

            throw new UnreachableException();
        }
    }

    /// <summary>Succeeds on the first read and reports a network failure on every later one.</summary>
    private sealed class OnceCodexSource(TimeProvider time) : ICodexLimitSource
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _reads;

        /// <summary>Real time of the successful read, on the stopwatch the test host's <see cref="LifecycleRun.Now"/> uses.</summary>
        public TimeSpan? SucceededAt { get; private set; }

        public Task<CodexReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _reads) > 1)
                return Task.FromResult<CodexReadResult>(new CodexReadFailed(CodexUnavailableReason.NetworkFailed));
            var now = time.GetUtcNow();
            var snapshot = new CodexLimitSnapshot(CodexLimitWindow.Create(24, now.AddHours(2))!,
                CodexLimitWindow.Create(18, now.AddDays(3))!);
            SucceededAt = _clock.Elapsed;
            return Task.FromResult<CodexReadResult>(new CodexReadSucceeded(snapshot));
        }
    }

    /// <summary>
    /// A clock whose timestamps, wall time, and timers run <c>speed</c> times faster than <see cref="TimeProvider.System"/>,
    /// so minute-scale limit polling and expiry finish in seconds.
    /// </summary>
    private sealed class AcceleratedTimeProvider(int speed) : TimeProvider
    {
        private readonly long _originTimestamp = System.GetTimestamp();
        private readonly DateTimeOffset _originUtc = System.GetUtcNow();

        public override long TimestampFrequency => System.TimestampFrequency;

        public override long GetTimestamp() => _originTimestamp + (System.GetTimestamp() - _originTimestamp) * speed;

        public override DateTimeOffset GetUtcNow() => _originUtc + System.GetElapsedTime(_originTimestamp) * speed;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new AcceleratedTimer(System.CreateTimer(callback, state, Scale(dueTime), Scale(period)), this);

        private TimeSpan Scale(TimeSpan value) => value == Timeout.InfiniteTimeSpan ? value : value / speed;

        private sealed class AcceleratedTimer(ITimer inner, AcceleratedTimeProvider clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => inner.Change(clock.Scale(dueTime), clock.Scale(period));

            public void Dispose() => inner.Dispose();

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
