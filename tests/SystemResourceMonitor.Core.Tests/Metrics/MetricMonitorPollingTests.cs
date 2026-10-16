using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Core.Tests.Metrics;

/// <summary>
/// Runs all polling loops against controllable sources and fake time. Before each move of fake time, a test waits with
/// explicit signals until the loops have published and armed their next timers, and it never moves past a due timer
/// without waiting for that timer's effects.
/// </summary>
public sealed class MetricMonitorPollingTests : IDisposable
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);
    private static readonly TimeSpan Poll = MetricMonitor.PollInterval;

    private readonly ObservedTimeProvider _time = new();
    private readonly FakeMetricSource _cpu = new(MetricKind.Cpu, Samples.Cpu);
    private readonly FakeMetricSource _ram = new(MetricKind.Ram, Samples.Ram);
    private readonly FakeMetricSource _gpu = new(MetricKind.Gpu, Samples.Gpu);
    private readonly CancellationTokenSource _cancellation = new();
    private readonly MetricMonitor _monitor;
    private readonly PublicationRecorder _recorder;
    private Task? _run;

    public MetricMonitorPollingTests()
    {
        _monitor = new MetricMonitor(_cpu, _ram, _gpu, _time);
        _recorder = new PublicationRecorder(_monitor);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _cpu.Release();
        _ram.Release();
        _gpu.Release();
        _cancellation.Dispose();
    }

    [Fact]
    public void Each_source_is_read_every_two_seconds()
    {
        Start();

        for (var poll = 2; poll <= 4; poll++)
        {
            _time.Advance(Poll - OneTick);
            Assert.Equal(poll - 1, _cpu.Reads);
            Assert.Equal(poll - 1, _ram.Reads);

            _time.Advance(OneTick);
            _recorder.WaitFor(MetricKind.Cpu, poll);
            _recorder.WaitFor(MetricKind.Ram, poll);
            _time.WaitForArmed(Seconds(2 * poll), 3);
        }

        Assert.Equal([0, 2, 4, 6], MeasuredSeconds(MetricKind.Cpu));
        Assert.Equal([0, 2, 4, 6], MeasuredSeconds(MetricKind.Ram));
        Assert.Equal(4, _cpu.Reads);
        Assert.Equal(4, _ram.Reads);
    }

    [Theory]
    [InlineData(MetricKind.Cpu)]
    [InlineData(MetricKind.Ram)]
    public void A_blocked_source_expires_at_six_seconds_while_the_other_keeps_updating(MetricKind blockedKind)
    {
        var blocked = Source(blockedKind);
        var other = Samples.Other(blockedKind);
        Start();
        blocked.Block();

        _time.Advance(Poll);
        blocked.WaitForReads(2);
        _recorder.WaitFor(other, 2);
        _time.WaitForArmed(Seconds(4), 2);

        _time.Advance(Poll);
        _recorder.WaitFor(other, 3);
        _time.WaitForArmed(Seconds(6), 3);

        _time.Advance(Poll - OneTick);
        Assert.IsType<AvailableMetric>(_monitor.Current.Get(blockedKind));

        _time.Advance(OneTick);
        _recorder.WaitFor(blockedKind, IsExpired, $"{blockedKind} expiry");
        _recorder.WaitFor(other, 4);
        _time.WaitForArmed(Seconds(8), 2);

        _time.Advance(Poll);
        _recorder.WaitFor(other, 5);

        Assert.Equal(2, blocked.Reads);
        Assert.Equal(1, blocked.MaxInFlight);
        Assert.Equal(2, _recorder.Of(blockedKind).Count);
        Assert.Equal([0, 2, 4, 6, 8], MeasuredSeconds(other));
    }

    [Fact]
    public void A_five_second_read_neither_overlaps_nor_bursts_and_leaves_the_other_cadence_unchanged()
    {
        Start();
        _cpu.Block();

        _time.Advance(Poll);
        _cpu.WaitForReads(2);
        _recorder.WaitFor(MetricKind.Ram, 2);
        _time.WaitForArmed(Seconds(4), 2);

        _time.Advance(Poll);
        _recorder.WaitFor(MetricKind.Ram, 3);
        _time.WaitForArmed(Seconds(6), 3);

        _time.Advance(Poll);
        _recorder.WaitFor(MetricKind.Cpu, IsExpired, "CPU expiry");
        _recorder.WaitFor(MetricKind.Ram, 4);
        _time.WaitForArmed(Seconds(8), 2);

        _time.Advance(Seconds(1));
        Assert.Equal(2, _cpu.Reads);

        // The read that started at 2 s returns at 7 s; the loop reads once more right away and skips the missed ticks.
        _cpu.Release();
        _recorder.WaitFor(MetricKind.Cpu, 4);
        _time.WaitForArmed(Seconds(9), 1);

        _time.Advance(Seconds(1));
        _recorder.WaitFor(MetricKind.Ram, 5);
        _time.WaitForArmed(Seconds(10), 2);
        Assert.Equal(3, _cpu.Reads);

        _time.Advance(Seconds(1));
        _recorder.WaitFor(MetricKind.Cpu, 5);

        Assert.Equal(4, _cpu.Reads);
        Assert.Equal(1, _cpu.MaxInFlight);
        Assert.Equal(1, _ram.MaxInFlight);
        Assert.Equal([0, 7, 7, 9], MeasuredSeconds(MetricKind.Cpu));
        Assert.Equal([0, 2, 4, 6, 8], MeasuredSeconds(MetricKind.Ram));
    }

    [Theory]
    [InlineData(MetricKind.Cpu)]
    [InlineData(MetricKind.Ram)]
    public void A_blocked_read_does_not_delay_the_expiry_of_the_other_source_whose_reads_fail(MetricKind blockedKind)
    {
        var blocked = Source(blockedKind);
        var failingKind = Samples.Other(blockedKind);
        var failing = Source(failingKind);
        Start();
        blocked.Block();
        failing.Throws();

        // A loop arms its next delay only after applying its read's result; both expiry timers are due at 6 s.
        _time.Advance(Poll);
        blocked.WaitForReads(2);
        _time.WaitForArmed(Seconds(4), 2);
        _time.Advance(Poll);
        _time.WaitForArmed(Seconds(6), 4);

        _time.Advance(Poll - OneTick);
        Assert.IsType<AvailableMetric>(_monitor.Current.Get(blockedKind));
        Assert.IsType<AvailableMetric>(_monitor.Current.Get(failingKind));

        _time.Advance(OneTick);
        _recorder.WaitFor(blockedKind, IsExpired, $"{blockedKind} expiry");
        _recorder.WaitFor(failingKind, IsExpired, $"{failingKind} expiry");

        // Expiry and the failed read due at 6 s may apply in either order; the expiry is the second publication either way.
        Assert.All([blockedKind, failingKind], kind => Assert.True(IsExpired(_recorder.Of(kind)[1])));
        Assert.Equal(2, blocked.Reads);
        Assert.Equal(1, blocked.MaxInFlight);
        Assert.Equal(2, _recorder.Of(blockedKind).Count);
    }

    [Fact]
    public async Task A_handler_failure_during_expiry_faults_the_run_instead_of_escaping_the_timer()
    {
        Start();
        _monitor.Changed += (_, state) =>
        {
            if (IsExpired(state.Cpu) || IsExpired(state.Ram))
            {
                throw new InvalidOperationException("Subscriber failed on expiry.");
            }
        };
        _cpu.Block();
        _ram.Block();
        _time.Advance(Poll);
        _cpu.WaitForReads(2);
        _ram.WaitForReads(2);
        _time.WaitForArmed(Seconds(6), 2);

        // Both expiry timers fire inside Advance; the handler exception must not escape it.
        _time.Advance(Seconds(4));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _run!.WaitAsync(Waits.Timeout, TestContext.Current.CancellationToken));
        Assert.Equal("Subscriber failed on expiry.", failure.Message);
        Assert.Single(_recorder.All, IsExpired);
    }

    [Fact]
    public void A_throwing_read_is_a_failed_read_that_does_not_renew_or_stop_the_source()
    {
        Start();
        var first = _monitor.Current.Cpu;
        _cpu.Throws();

        // A loop arms its next delay only after applying its read's result; the CPU expiry timer is also due at 6 s.
        for (var seconds = 2; seconds <= 4; seconds += 2)
        {
            _time.Advance(Poll);
            _recorder.WaitFor(MetricKind.Ram, seconds / 2 + 1);
            _time.WaitForArmed(Seconds(seconds + 2), seconds == 4 ? 4 : 3);
            Assert.Same(first, _monitor.Current.Cpu);
            Assert.Single(_recorder.Of(MetricKind.Cpu));
        }

        // At 6 s, expiry and the next failed read race; either order ends as Expired then ReadFailed by 8 s.
        _time.Advance(Poll);
        _recorder.WaitFor(MetricKind.Ram, 4);
        _time.WaitForArmed(Seconds(8), 3);
        _time.Advance(Poll);
        _recorder.WaitFor(MetricKind.Ram, 5);
        _time.WaitForArmed(Seconds(10), 3);

        Assert.Equal(5, _cpu.Reads);
        Assert.Equal(
            [UnavailabilityReason.Expired, UnavailabilityReason.ReadFailed],
            _recorder.Of(MetricKind.Cpu).Skip(1).Select(reading => Assert.IsType<UnavailableMetric>(reading).Reason));
    }

    [Fact]
    public async Task Cancellation_stops_new_reads_and_discards_late_results_and_expiry()
    {
        Start();
        _cpu.Block();
        _time.Advance(Poll);
        _cpu.WaitForReads(2);
        _recorder.WaitFor(MetricKind.Ram, 2);
        _time.WaitForArmed(Seconds(4), 2);

        await _cancellation.CancelAsync();
        await _run!.WaitAsync(Waits.Timeout, TestContext.Current.CancellationToken);
        var published = _recorder.All.Count;

        _cpu.Release();
        _cpu.WaitForReturns(2);
        _time.Advance(TimeSpan.FromSeconds(30));

        Assert.True(_run.IsCompletedSuccessfully);
        Assert.Equal(published, _recorder.All.Count);
        Assert.Equal(2, _cpu.Reads);
        Assert.Equal(2, _ram.Reads);
        Assert.IsType<AvailableMetric>(_monitor.Current.Cpu);
        Assert.IsType<AvailableMetric>(_monitor.Current.Ram);
    }

    [Fact]
    public async Task Cancellation_before_the_run_starts_no_read()
    {
        await _cancellation.CancelAsync();

        await _monitor.RunAsync(_cancellation.Token).WaitAsync(Waits.Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(0, _cpu.Reads);
        Assert.Equal(0, _ram.Reads);
        Assert.Empty(_recorder.All);
    }

    [Fact]
    public void The_monitor_runs_only_once()
    {
        Start();

        // The second call throws synchronously instead of returning a faulted task.
        Assert.Throws<InvalidOperationException>(() => { _ = _monitor.RunAsync(CancellationToken.None); });
    }

    [Fact]
    public async Task An_unexpected_failure_stops_both_loops_and_faults_the_run()
    {
        _monitor.Changed += (_, _) => throw new InvalidOperationException("Subscriber failed.");

        var run = _monitor.RunAsync(_cancellation.Token);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => run.WaitAsync(Waits.Timeout, TestContext.Current.CancellationToken));
        Assert.Equal("Subscriber failed.", failure.Message);
    }

    private static TimeSpan Seconds(int seconds) => TimeSpan.FromSeconds(seconds);

    private static bool IsExpired(MetricReading reading) => reading is UnavailableMetric { Reason: UnavailabilityReason.Expired };

    private FakeMetricSource Source(MetricKind kind) => kind == MetricKind.Cpu ? _cpu : _ram;

    private IEnumerable<double> MeasuredSeconds(MetricKind kind) =>
        _recorder.Of(kind).OfType<AvailableMetric>().Select(reading => (reading.MeasuredAt - _time.Start).TotalSeconds);

    private void Start()
    {
        _run = _monitor.RunAsync(_cancellation.Token);
        _recorder.WaitFor(MetricKind.Cpu, 1);
        _recorder.WaitFor(MetricKind.Ram, 1);
        _time.WaitForArmed(Poll, 3);
    }
}
