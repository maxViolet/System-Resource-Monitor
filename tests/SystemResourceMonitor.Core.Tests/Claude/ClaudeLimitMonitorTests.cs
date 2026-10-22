using Microsoft.Extensions.Time.Testing;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Core.Tests.Metrics;

namespace SystemResourceMonitor.Core.Tests.Claude;

/// <summary>
/// State transitions through <see cref="ClaudeLimitMonitor.Apply"/> and the polling loop against a controllable source
/// and fake time. Before each move of fake time, a polling test waits with explicit signals until the loop has applied
/// its read and armed its next timer, and it never moves past a due timer without waiting for that timer's effects.
/// </summary>
public sealed class ClaudeLimitMonitorTests : IDisposable
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);
    private static readonly TimeSpan Poll = ClaudeLimitMonitor.PollInterval;
    private static readonly TimeSpan Stale = ClaudeLimitMonitor.StaleLimit;

    private static readonly ClaudeLimitSnapshot Snapshot = ClaudeSamples.Snapshot;

    private readonly ObservedTimeProvider _time = new();
    private readonly FakeClaudeSource _source = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ClaudeLimitMonitor _monitor;
    private readonly ClaudeStateRecorder _recorder;

    public ClaudeLimitMonitorTests()
    {
        _monitor = new ClaudeLimitMonitor(_source, _time);
        _recorder = new ClaudeStateRecorder(_monitor);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _source.Release();
        _cancellation.Dispose();
    }

    [Fact]
    public void Sign_out_is_unavailable_on_the_first_poll()
    {
        var time = new FakeTimeProvider();
        var monitor = new ClaudeLimitMonitor(new NeverRead(), time);

        monitor.Apply(new ClaudeReadFailed(ClaudeUnavailableReason.SignedOut), time.GetUtcNow(), time.GetTimestamp());

        Assert.Equal(ClaudeUnavailableReason.SignedOut,
            Assert.IsType<ClaudeLimitsUnavailable>(monitor.Current).Reason);
    }

    [Fact]
    public void Network_staleness_expires_without_another_poll_but_unknown_format_drops_immediately()
    {
        var time = new FakeTimeProvider();
        var monitor = new ClaudeLimitMonitor(new NeverRead(), time);
        monitor.Apply(new ClaudeReadSucceeded(Snapshot), time.GetUtcNow(), time.GetTimestamp());
        time.Advance(TimeSpan.FromMinutes(1));
        monitor.Apply(new ClaudeReadFailed(ClaudeUnavailableReason.NetworkFailed), time.GetUtcNow(), time.GetTimestamp());
        Assert.IsType<ClaudeLimitsStale>(monitor.Current);

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(ClaudeUnavailableReason.NetworkFailed,
            Assert.IsType<ClaudeLimitsUnavailable>(monitor.Current).Reason);

        monitor.Apply(new ClaudeReadSucceeded(Snapshot), time.GetUtcNow(), time.GetTimestamp());
        monitor.Apply(new ClaudeReadFailed(ClaudeUnavailableReason.UnknownFormat), time.GetUtcNow(), time.GetTimestamp());
        Assert.Equal(ClaudeUnavailableReason.UnknownFormat,
            Assert.IsType<ClaudeLimitsUnavailable>(monitor.Current).Reason);
    }

    [Fact]
    public async Task Slow_poll_never_overlaps_and_a_late_result_is_not_published_after_shutdown()
    {
        var time = new FakeTimeProvider();
        var source = new BlockingSource();
        var monitor = new ClaudeLimitMonitor(source, time);
        using var lifetime = new CancellationTokenSource();
        var publications = 0;
        monitor.Changed += (_, _) => publications++;
        var run = monitor.RunAsync(lifetime.Token);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, source.Reads);
        lifetime.Cancel();
        source.Complete(new ClaudeReadSucceeded(Snapshot));
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(0, publications);
        Assert.Same(ClaudeLimitsUnavailable.Initial, monitor.Current);
    }

    [Fact]
    public void Second_poll_starts_at_one_minute_and_a_blocked_poll_is_never_overlapped()
    {
        Start();

        _time.Advance(Poll - OneTick);
        Assert.Equal(1, _source.Reads);

        _time.Advance(OneTick);
        _source.WaitForReads(2);
        _recorder.WaitFor(2);
        _time.WaitForArmed(Poll * 2, 1);
        Assert.Equal(_time.Start + Poll, Assert.IsType<ClaudeLimitsCurrent>(_monitor.Current).MeasuredAt);

        // The read at 120 s blocks; the expiry of the success at 60 s is due at 240 s, after 210 s.
        _source.Block();
        _time.Advance(Poll);
        _source.WaitForReads(3);
        _time.Advance(TimeSpan.FromSeconds(90));

        Assert.Equal(3, _source.Reads);
        Assert.Equal(1, _source.MaxInFlight);
    }

    [Theory]
    [InlineData(ClaudeUnavailableReason.SignedOut)]
    [InlineData(ClaudeUnavailableReason.UnknownFormat)]
    [InlineData(ClaudeUnavailableReason.NetworkFailed)]
    public void Failure_on_the_first_poll_is_unavailable_at_once(ClaudeUnavailableReason reason)
    {
        // A network failure without an earlier success has nothing to keep as stale.
        _source.Returns(ClaudeSamples.Failure(reason));

        _ = _monitor.RunAsync(_cancellation.Token);
        _recorder.WaitFor(1);

        Assert.True(ClaudeSamples.IsUnavailable(_monitor.Current, reason));
        Assert.Single(_recorder.All);
    }

    [Fact]
    public void Repeated_network_failures_keep_the_deadline_of_the_last_success()
    {
        Start();
        _source.Returns(ClaudeSamples.Failure(ClaudeUnavailableReason.NetworkFailed));

        _time.Advance(Poll);
        _source.WaitForReads(2);
        _recorder.WaitFor(2);
        _time.WaitForArmed(Poll * 2, 1);
        var stale = Assert.IsType<ClaudeLimitsStale>(_monitor.Current);
        Assert.Equal(_time.Start + Stale, stale.ExpiresAt);

        // The failure at 120 s neither publishes nor moves the expiry armed at 180 s.
        _time.Advance(Poll);
        _source.WaitForReads(3);
        _time.WaitForArmed(Stale, 2);
        Assert.Same(stale, _monitor.Current);

        // The read at 180 s blocks, so only the expiry timer can end the stale value.
        _source.Block();
        _time.Advance(Poll - OneTick);
        Assert.Same(stale, _monitor.Current);

        _time.Advance(OneTick);
        _recorder.WaitFor(3);

        Assert.True(ClaudeSamples.IsUnavailable(_monitor.Current, ClaudeUnavailableReason.NetworkFailed));
        Assert.Equal(3, _recorder.All.Count);
        Assert.Equal(3, _source.Returned);
    }

    [Theory]
    [InlineData(ClaudeUnavailableReason.SignedOut)]
    [InlineData(ClaudeUnavailableReason.UnknownFormat)]
    public void Sign_out_or_unknown_format_after_staleness_is_unavailable_at_once(ClaudeUnavailableReason reason)
    {
        Start();
        _source.Returns(ClaudeSamples.Failure(ClaudeUnavailableReason.NetworkFailed));
        _time.Advance(Poll);
        _source.WaitForReads(2);
        _recorder.WaitFor(2);
        _time.WaitForArmed(Poll * 2, 1);
        Assert.IsType<ClaudeLimitsStale>(_monitor.Current);

        _source.Returns(ClaudeSamples.Failure(reason));
        _time.Advance(Poll);
        _source.WaitForReads(3);
        _recorder.WaitFor(3);

        Assert.True(ClaudeSamples.IsUnavailable(_monitor.Current, reason));
    }

    [Fact]
    public void Current_value_with_a_blocked_poll_expires_three_minutes_after_the_success_without_another_poll()
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

        Assert.True(ClaudeSamples.IsUnavailable(_monitor.Current, ClaudeUnavailableReason.Expired));
        Assert.Equal(2, _source.Reads);
        Assert.Equal(1, _source.Returned);
    }

    /// <summary>Starts the run and waits until the first read at 0 s is current and the next poll is armed.</summary>
    private void Start()
    {
        _ = _monitor.RunAsync(_cancellation.Token);
        _recorder.WaitFor(1);
        _time.WaitForArmed(Poll, 1);
        Assert.IsType<ClaudeLimitsCurrent>(_monitor.Current);
    }

    private sealed class NeverRead : IClaudeLimitSource
    {
        public Task<ClaudeReadResult> ReadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The test applies completed results directly.");
    }

    private sealed class BlockingSource : IClaudeLimitSource
    {
        private readonly TaskCompletionSource<ClaudeReadResult> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads { get; private set; }

        public Task<ClaudeReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            Started.TrySetResult();
            return _result.Task;
        }

        public void Complete(ClaudeReadResult result) => _result.TrySetResult(result);
    }
}
