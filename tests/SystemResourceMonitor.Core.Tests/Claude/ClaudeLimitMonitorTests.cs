using Microsoft.Extensions.Time.Testing;
using SystemResourceMonitor.AIResources.Core.Claude;

namespace SystemResourceMonitor.Core.Tests.Claude;

public sealed class ClaudeLimitMonitorTests
{
    private static readonly ClaudeLimitSnapshot Snapshot = new(
        ClaudeLimitWindow.Create("Current session", 25, DateTimeOffset.UtcNow.AddHours(1))!,
        [ClaudeLimitWindow.Create("Current week (all models)", 5, DateTimeOffset.UtcNow.AddDays(1))!]);

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
