using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Core.Tests.Metrics;

namespace SystemResourceMonitor.Core.Tests.Codex;

/// <summary>
/// Runs the Codex polling loop against a controllable source and fake time. Before each move of fake time, a test waits
/// with explicit signals until the loop has applied its read and armed its next timer, and it never moves past a due
/// timer without waiting for that timer's effects. Reads run on the thread pool, so a test waits for the source's read
/// counter rather than assuming a read started synchronously.
/// </summary>
public sealed class CodexLimitMonitorPollingTests : IDisposable
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);
    private static readonly TimeSpan Poll = CodexLimitMonitor.PollInterval;
    private static readonly TimeSpan Stale = CodexLimitMonitor.StaleLimit;

    private readonly ObservedTimeProvider _time = new();
    private readonly FakeCodexSource _source = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly CodexLimitMonitor _monitor;
    private readonly CodexStateRecorder _recorder;
    private Task? _run;

    public CodexLimitMonitorPollingTests()
    {
        _monitor = new CodexLimitMonitor(_source, _time);
        _recorder = new CodexStateRecorder(_monitor);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _source.Release();
        _cancellation.Dispose();
    }

    [Fact]
    public void Source_is_polled_at_zero_sixty_and_one_hundred_twenty_seconds()
    {
        Start();

        for (var poll = 2; poll <= 3; poll++)
        {
            _time.Advance(Poll - OneTick);
            Assert.Equal(poll - 1, _source.Reads);

            _time.Advance(OneTick);
            _source.WaitForReads(poll);
            _recorder.WaitFor(poll);
            _time.WaitForArmed(Poll * poll, 1);
        }

        Assert.Equal([0, 60, 120], MeasuredSeconds());
        Assert.Equal(3, _source.Reads);
        Assert.Equal(1, _source.MaxInFlight);
    }

    [Fact]
    public void Blocked_read_never_overlaps_and_later_polls_skip_missed_ticks_instead_of_bursting()
    {
        Start();
        _source.Block();

        _time.Advance(Poll);
        _source.WaitForReads(2);

        // The expiry timer is due at 180 s; 150 s stays before it.
        _time.Advance(TimeSpan.FromSeconds(90));
        Assert.Equal(2, _source.Reads);

        // The read that started at 60 s returns at 150 s; the loop reads once more right away and skips 120 s and 180 s.
        _source.Release();
        _source.WaitForReads(3);
        _recorder.WaitFor(3);
        _time.WaitForArmed(TimeSpan.FromSeconds(210), 1);

        _time.Advance(Poll - OneTick);
        Assert.Equal(3, _source.Reads);

        _time.Advance(OneTick);
        _source.WaitForReads(4);
        _recorder.WaitFor(4);
        _time.WaitForArmed(TimeSpan.FromSeconds(270), 1);

        Assert.Equal(4, _source.Reads);
        Assert.Equal(1, _source.MaxInFlight);
        Assert.Equal([0, 150, 150, 210], MeasuredSeconds());
    }

    [Fact]
    public void Stale_value_ends_as_network_failure_exactly_three_minutes_after_the_success_without_another_poll()
    {
        Start();
        _source.Returns(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));

        _time.Advance(Poll);
        _source.WaitForReads(2);
        _recorder.WaitFor(2);
        _time.WaitForArmed(Poll * 2, 1);
        var stale = Assert.IsType<CodexLimitsStale>(_monitor.Current);
        Assert.Equal(_time.Start + Stale, stale.ExpiresAt);

        _source.Block();
        _time.Advance(Poll);
        _source.WaitForReads(3);
        _time.WaitForArmed(Stale, 1);

        _time.Advance(Poll - OneTick);
        Assert.Same(stale, _monitor.Current);

        _time.Advance(OneTick);
        _recorder.WaitFor(3);

        Assert.True(CodexSamples.IsUnavailable(_monitor.Current, CodexUnavailableReason.NetworkFailed));
        Assert.Equal(3, _recorder.All.Count);
        Assert.Equal(3, _source.Reads);
        Assert.Equal(2, _source.Returned);
    }

    [Fact]
    public void Current_value_with_a_blocked_read_expires_exactly_three_minutes_after_the_success()
    {
        Start();
        var current = _monitor.Current;
        _source.Block();

        _time.Advance(Poll);
        _source.WaitForReads(2);
        _time.WaitForArmed(Stale, 1);

        _time.Advance(Stale - Poll - OneTick);
        Assert.Same(current, _monitor.Current);

        _time.Advance(OneTick);
        _recorder.WaitFor(2);

        Assert.True(CodexSamples.IsUnavailable(_monitor.Current, CodexUnavailableReason.Expired));
        Assert.Equal(2, _recorder.All.Count);
        Assert.Equal(2, _source.Reads);
    }

    [Fact]
    public async Task Cancellation_during_a_blocked_read_ends_the_run_only_after_the_read_and_discards_its_late_result()
    {
        Start();
        _source.Block();
        _source.Returns(new CodexReadSucceeded(CodexSamples.OtherSnapshot));
        _time.Advance(Poll);
        _source.WaitForReads(2);
        var current = _monitor.Current;

        await _cancellation.CancelAsync();

        Assert.True(_source.Tokens[1].IsCancellationRequested);
        Assert.False(_run!.IsCompleted);

        _source.Release();
        await _run.WaitAsync(Waits.Timeout, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.True(_run.IsCompletedSuccessfully);
        Assert.Single(_recorder.All);
        Assert.Same(current, _monitor.Current);
        Assert.Equal(2, _source.Reads);
    }

    [Fact]
    public async Task Cancellation_completes_the_run_without_fault_when_the_source_completes_as_cancelled()
    {
        Start();
        _source.Block(honorCancellation: true);
        _time.Advance(Poll);
        _source.WaitForReads(2);

        await _cancellation.CancelAsync();
        await _run!.WaitAsync(Waits.Timeout, TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.True(_run.IsCompletedSuccessfully);
        Assert.Single(_recorder.All);
        Assert.IsType<CodexLimitsCurrent>(_monitor.Current);
        Assert.Equal(2, _source.Reads);
    }

    [Fact]
    public async Task Cancellation_before_the_run_starts_no_read()
    {
        await _cancellation.CancelAsync();

        await _monitor.RunAsync(_cancellation.Token).WaitAsync(Waits.Timeout, TestContext.Current.CancellationToken);

        Assert.Equal(0, _source.Reads);
        Assert.Empty(_recorder.All);
    }

    [Theory]
    [InlineData("faulted task")]
    [InlineData("synchronous throw")]
    [InlineData("cancellation without request")]
    public void Throwing_source_is_a_read_failure_and_polling_continues(string failure)
    {
        Start();
        switch (failure)
        {
            case "faulted task":
                _source.Throws(new InvalidOperationException("The fake read failed."));
                break;
            case "synchronous throw":
                _source.ThrowsSynchronously(new InvalidOperationException("The fake read failed."));
                break;
            default:
                _source.Throws(new OperationCanceledException("The fake read was cancelled on its own."));
                break;
        }

        _time.Advance(Poll);
        _source.WaitForReads(2);
        _recorder.WaitFor(2);
        _time.WaitForArmed(Poll * 2, 1);
        Assert.True(CodexSamples.IsUnavailable(_monitor.Current, CodexUnavailableReason.ReadFailed));

        _source.Returns(CodexSamples.Success);
        _time.Advance(Poll);
        _source.WaitForReads(3);
        _recorder.WaitFor(3);

        var current = Assert.IsType<CodexLimitsCurrent>(_monitor.Current);
        Assert.Equal(_time.Start + Poll * 2, current.MeasuredAt);
        Assert.False(_run!.IsCompleted);
    }

    [Fact]
    public async Task A_handler_failure_faults_the_run()
    {
        _monitor.Changed += (_, _) => throw new InvalidOperationException("Subscriber failed.");

        var run = _monitor.RunAsync(_cancellation.Token);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => run.WaitAsync(Waits.Timeout, TestContext.Current.CancellationToken));
        Assert.Equal("Subscriber failed.", failure.Message);
        Assert.Equal(1, _source.Reads);
    }

    [Fact]
    public async Task A_handler_failure_during_expiry_faults_the_run_instead_of_escaping_the_timer()
    {
        Start();
        _monitor.Changed += (_, state) =>
        {
            if (CodexSamples.IsUnavailable(state, CodexUnavailableReason.Expired))
            {
                throw new InvalidOperationException("Subscriber failed on expiry.");
            }
        };
        _source.Block(honorCancellation: true);
        _time.Advance(Poll);
        _source.WaitForReads(2);
        _time.WaitForArmed(Stale, 1);

        // The expiry timer fires inside Advance; the handler exception must not escape it.
        _time.Advance(Stale - Poll);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _run!.WaitAsync(Waits.Timeout, TestContext.Current.CancellationToken));
        Assert.Equal("Subscriber failed on expiry.", failure.Message);
        Assert.True(_source.Tokens[1].IsCancellationRequested);
    }

    [Fact]
    public void The_monitor_runs_only_once()
    {
        Start();

        // The second call throws synchronously instead of returning a faulted task.
        Assert.Throws<InvalidOperationException>(() => { _ = _monitor.RunAsync(CancellationToken.None); });
    }

    private IEnumerable<double> MeasuredSeconds() =>
        _recorder.All.OfType<CodexLimitsCurrent>().Select(state => (state.MeasuredAt - _time.Start).TotalSeconds);

    /// <summary>Starts the run and waits until the first read at 0 s is current and the next poll is armed.</summary>
    private void Start()
    {
        _run = _monitor.RunAsync(_cancellation.Token);
        _recorder.WaitFor(1);
        _time.WaitForArmed(Poll, 1);
        Assert.IsType<CodexLimitsCurrent>(_monitor.Current);
    }
}
