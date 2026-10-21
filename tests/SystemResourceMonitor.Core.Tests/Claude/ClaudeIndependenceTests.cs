using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Core.Tests.Codex;
using SystemResourceMonitor.Core.Tests.Metrics;

namespace SystemResourceMonitor.Core.Tests.Claude;

/// <summary>
/// Runs the system-metric, Codex, and Claude monitors on one fake clock. A blocked Claude poll must neither delay nor
/// stop CPU, RAM, GPU, or Codex polling, nor move Codex expiry, and Claude expiry keeps its own deadline.
/// </summary>
public sealed class ClaudeIndependenceTests : IDisposable
{
    private static readonly TimeSpan MetricPoll = MetricMonitor.PollInterval;
    private static readonly int End = (int)ClaudeLimitMonitor.StaleLimit.TotalSeconds;

    private readonly ObservedTimeProvider _time = new();
    private readonly FakeMetricSource _cpu = new(MetricKind.Cpu, Samples.Cpu);
    private readonly FakeMetricSource _ram = new(MetricKind.Ram, Samples.Ram);
    private readonly FakeMetricSource _gpu = new(MetricKind.Gpu, Samples.Gpu);
    private readonly FakeCodexSource _codex = new();
    private readonly FakeClaudeSource _claude = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly MetricMonitor _metrics;
    private readonly CodexLimitMonitor _codexLimits;
    private readonly ClaudeLimitMonitor _claudeLimits;
    private readonly PublicationRecorder _metricRecorder;
    private readonly CodexStateRecorder _codexRecorder;
    private readonly ClaudeStateRecorder _claudeRecorder;

    public ClaudeIndependenceTests()
    {
        _metrics = new MetricMonitor(_cpu, _ram, _gpu, _time);
        _codexLimits = new CodexLimitMonitor(_codex, _time);
        _claudeLimits = new ClaudeLimitMonitor(_claude, _time);
        _metricRecorder = new PublicationRecorder(_metrics);
        _codexRecorder = new CodexStateRecorder(_codexLimits);
        _claudeRecorder = new ClaudeStateRecorder(_claudeLimits);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _claude.Release();
        _codex.Release();
        _cpu.Release();
        _ram.Release();
        _gpu.Release();
        _cancellation.Dispose();
    }

    /// <summary>
    /// The Claude poll at 60 s blocks for the rest of the test. With <paramref name="codexBlocks"/> the Codex poll at
    /// 60 s blocks as well, so Codex must expire at its own deadline; otherwise Codex polls every minute.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Blocked_claude_poll_leaves_metric_and_codex_polling_and_expiry_on_schedule(bool codexBlocks)
    {
        _ = _metrics.RunAsync(_cancellation.Token);
        _ = _codexLimits.RunAsync(_cancellation.Token);
        _ = _claudeLimits.RunAsync(_cancellation.Token);
        _metricRecorder.WaitFor(MetricKind.Cpu, 1);
        _metricRecorder.WaitFor(MetricKind.Ram, 1);
        _codexRecorder.WaitFor(1);
        _claudeRecorder.WaitFor(1);
        _time.WaitForArmed(MetricPoll, 3);
        _time.WaitForArmed(ClaudeLimitMonitor.PollInterval, 2);
        var claudeCurrent = Assert.IsType<ClaudeLimitsCurrent>(_claudeLimits.Current);

        _claude.Block();
        if (codexBlocks)
        {
            _codex.Block();
        }

        var reads = 1;
        var codexPublications = 1;
        for (var seconds = 2; seconds <= End; seconds += 2)
        {
            if (seconds == End)
            {
                Assert.Same(claudeCurrent, _claudeLimits.Current);
            }

            _time.Advance(MetricPoll);
            reads++;
            _metricRecorder.WaitFor(MetricKind.Cpu, reads);
            _metricRecorder.WaitFor(MetricKind.Ram, reads);

            // The fake GPU reports no adapter, so its reading never changes; its loop is counted by reads and timers.
            _gpu.WaitForReads(reads);

            if (seconds == 60)
            {
                _claude.WaitForReads(2);
                _codex.WaitForReads(2);
            }

            if (seconds % 60 == 0 && !codexBlocks)
            {
                _codex.WaitForReads(seconds / 60 + 1);
                _codexRecorder.WaitFor(++codexPublications);
            }

            if (seconds == End)
            {
                _claudeRecorder.WaitFor(2);
                if (codexBlocks)
                {
                    _codexRecorder.WaitFor(++codexPublications);
                }
            }

            // Metric polls arm three timers. The polls at 60 s share their due time with the next Codex and Claude
            // polls; both expiries of the 0 s successes are due at 180 s, and an unblocked Codex polls every minute.
            var next = seconds + 2;
            var armed = 3 + (next == 60 ? 2 : 0) + (next == End ? 1 : 0)
                + (codexBlocks ? (next == End ? 1 : 0) : (next % 60 == 0 && next != 60 ? 1 : 0));
            _time.WaitForArmed(TimeSpan.FromSeconds(next), armed);
        }

        var everyTwoSeconds = Enumerable.Range(0, End / 2 + 1).Select(step => step * 2.0).ToArray();
        Assert.Equal(everyTwoSeconds, MeasuredSeconds(MetricKind.Cpu));
        Assert.Equal(everyTwoSeconds, MeasuredSeconds(MetricKind.Ram));
        Assert.Equal(everyTwoSeconds.Length, _gpu.Reads);
        Assert.Equal(1, _claude.MaxInFlight);
        Assert.Equal(2, _claude.Reads);
        Assert.True(ClaudeSamples.IsUnavailable(_claudeLimits.Current, ClaudeUnavailableReason.Expired));
        if (codexBlocks)
        {
            Assert.Equal(2, _codex.Reads);
            Assert.True(CodexSamples.IsUnavailable(_codexLimits.Current, CodexUnavailableReason.Expired));
        }
        else
        {
            Assert.Equal([0.0, 60, 120, 180], _codexRecorder.All.OfType<CodexLimitsCurrent>()
                .Select(state => (state.MeasuredAt - _time.Start).TotalSeconds));
            Assert.IsType<CodexLimitsCurrent>(_codexLimits.Current);
        }
    }

    private IEnumerable<double> MeasuredSeconds(MetricKind kind) =>
        _metricRecorder.Of(kind).OfType<AvailableMetric>().Select(reading => (reading.MeasuredAt - _time.Start).TotalSeconds);
}
