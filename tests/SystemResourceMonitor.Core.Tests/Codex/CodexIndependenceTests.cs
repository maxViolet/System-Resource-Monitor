using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Core.Tests.Metrics;

namespace SystemResourceMonitor.Core.Tests.Codex;

/// <summary>
/// Runs the system-metric monitor and the Codex monitor on one fake clock. A blocked Codex poll must neither delay nor
/// stop CPU and RAM polling or expiry, and Codex expiry keeps its own deadline.
/// </summary>
public sealed class CodexIndependenceTests : IDisposable
{
    private static readonly TimeSpan MetricPoll = MetricMonitor.PollInterval;

    private readonly ObservedTimeProvider _time = new();
    private readonly FakeMetricSource _cpu = new(MetricKind.Cpu, Samples.Cpu);
    private readonly FakeMetricSource _ram = new(MetricKind.Ram, Samples.Ram);
    private readonly FakeMetricSource _gpu = new(MetricKind.Gpu, Samples.Gpu);
    private readonly FakeCodexSource _codex = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly MetricMonitor _metrics;
    private readonly CodexLimitMonitor _limits;
    private readonly PublicationRecorder _metricRecorder;
    private readonly CodexStateRecorder _codexRecorder;

    public CodexIndependenceTests()
    {
        _metrics = new MetricMonitor(_cpu, _ram, _gpu, _time);
        _limits = new CodexLimitMonitor(_codex, _time);
        _metricRecorder = new PublicationRecorder(_metrics);
        _codexRecorder = new CodexStateRecorder(_limits);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _codex.Release();
        _cpu.Release();
        _ram.Release();
        _gpu.Release();
        _cancellation.Dispose();
    }

    [Fact]
    public void Blocked_codex_poll_leaves_metric_polling_and_expiry_on_schedule_and_expires_at_its_own_deadline()
    {
        _ = _metrics.RunAsync(_cancellation.Token);
        _ = _limits.RunAsync(_cancellation.Token);
        _metricRecorder.WaitFor(MetricKind.Cpu, 1);
        _metricRecorder.WaitFor(MetricKind.Ram, 1);
        _time.WaitForArmed(MetricPoll, 3);
        _codexRecorder.WaitFor(1);
        _time.WaitForArmed(CodexLimitMonitor.PollInterval, 1);
        var codexCurrent = Assert.IsType<CodexLimitsCurrent>(_limits.Current);

        // Every Codex poll after the first stays blocked for the rest of the test.
        _codex.Block();

        // The last CPU read succeeds at 60 s; the CPU read at 62 s blocks, so CPU expires at 66 s.
        var cpuReads = 1;
        var ramReads = 1;
        for (var seconds = 2; seconds <= 180; seconds += 2)
        {
            if (seconds == 62)
            {
                _cpu.Block();
            }

            if (seconds == 66)
            {
                Assert.IsType<AvailableMetric>(_metrics.Current.Cpu);
            }

            if (seconds == 180)
            {
                Assert.Same(codexCurrent, _limits.Current);
            }

            _time.Advance(MetricPoll);
            ramReads++;
            _metricRecorder.WaitFor(MetricKind.Ram, ramReads);
            if (seconds <= 60)
            {
                cpuReads++;
                _metricRecorder.WaitFor(MetricKind.Cpu, cpuReads);
            }
            else if (seconds == 62)
            {
                _cpu.WaitForReads(cpuReads + 1);
            }

            if (seconds == 60)
            {
                _codex.WaitForReads(2);
            }

            if (seconds == 66)
            {
                _metricRecorder.WaitFor(MetricKind.Cpu, IsExpired, "CPU expiry");
            }

            if (seconds == 180)
            {
                _codexRecorder.WaitFor(2);
            }

            // The blocked CPU loop arms no delay; RAM and GPU always do. The CPU expiry (66 s), the second Codex poll
            // (60 s), and the Codex expiry (180 s) share their due times with metric polls, so count them as well.
            var next = seconds + 2;
            var armed = (next > 62 ? 2 : 3) + (next is 60 or 66 or 180 ? 1 : 0);
            _time.WaitForArmed(TimeSpan.FromSeconds(next), armed);
        }

        Assert.Equal(Enumerable.Range(0, 91).Select(step => step * 2.0), MeasuredSeconds(MetricKind.Ram));
        Assert.Equal(Enumerable.Range(0, 31).Select(step => step * 2.0), MeasuredSeconds(MetricKind.Cpu));
        Assert.True(IsExpired(_metricRecorder.Of(MetricKind.Cpu)[^1]));
        Assert.Equal(2, _codex.Reads);
        Assert.Equal(1, _codex.MaxInFlight);
        Assert.Equal(2, _codexRecorder.All.Count);
        Assert.True(CodexSamples.IsUnavailable(_limits.Current, CodexUnavailableReason.Expired));
    }

    private static bool IsExpired(MetricReading reading) => reading is UnavailableMetric { Reason: UnavailabilityReason.Expired };

    private IEnumerable<double> MeasuredSeconds(MetricKind kind) =>
        _metricRecorder.Of(kind).OfType<AvailableMetric>().Select(reading => (reading.MeasuredAt - _time.Start).TotalSeconds);
}
