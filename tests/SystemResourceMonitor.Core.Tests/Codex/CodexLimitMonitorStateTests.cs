using Microsoft.Extensions.Time.Testing;
using SystemResourceMonitor.AIResources.Core.Codex;

namespace SystemResourceMonitor.Core.Tests.Codex;

/// <summary>Applies completed polls to the monitor directly, without its polling loop, and drives expiry with fake time.</summary>
public sealed class CodexLimitMonitorStateTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);
    private static readonly TimeSpan Stale = CodexLimitMonitor.StaleLimit;

    private readonly FakeTimeProvider _time = new();
    private readonly CodexLimitMonitor _monitor;
    private readonly CodexStateRecorder _recorder;

    public CodexLimitMonitorStateTests()
    {
        _monitor = new CodexLimitMonitor(new NeverPolledSource(), _time);
        _recorder = new CodexStateRecorder(_monitor);
    }

    public static TheoryData<CodexUnavailableReason> SourceReasons =>
    [
        CodexUnavailableReason.SignedOut,
        CodexUnavailableReason.NetworkFailed,
        CodexUnavailableReason.UnknownFormat,
        CodexUnavailableReason.UnsupportedVersion,
        CodexUnavailableReason.ClientNotFound,
        CodexUnavailableReason.ClientUntrusted,
        CodexUnavailableReason.TimedOut,
        CodexUnavailableReason.ReadFailed,
    ];

    public static TheoryData<CodexUnavailableReason> NonNetworkReasons =>
    [
        CodexUnavailableReason.SignedOut,
        CodexUnavailableReason.UnknownFormat,
        CodexUnavailableReason.UnsupportedVersion,
        CodexUnavailableReason.ClientNotFound,
        CodexUnavailableReason.ClientUntrusted,
        CodexUnavailableReason.TimedOut,
        CodexUnavailableReason.ReadFailed,
    ];

    [Fact]
    public void Monitor_starts_not_yet_sampled()
    {
        Assert.Same(CodexLimitsUnavailable.Initial, _monitor.Current);
        Assert.Equal(CodexUnavailableReason.NotYetSampled, CodexLimitsUnavailable.Initial.Reason);
    }

    [Theory]
    [MemberData(nameof(SourceReasons))]
    public void First_failed_poll_is_unavailable_at_once_without_a_stale_value(CodexUnavailableReason reason)
    {
        Apply(CodexSamples.Failure(reason));

        AssertUnavailable(reason, _monitor.Current);
        Assert.Single(_recorder.All);
    }

    [Fact]
    public void Successful_poll_is_current_with_its_measurement_time()
    {
        var measuredAt = _time.GetUtcNow();
        var timestamp = _time.GetTimestamp();

        Apply(CodexSamples.Success);

        var current = Assert.IsType<CodexLimitsCurrent>(_monitor.Current);
        Assert.Same(CodexSamples.Snapshot, current.Snapshot);
        Assert.Equal(measuredAt, current.MeasuredAt);
        Assert.Equal(timestamp, current.Timestamp);
        Assert.Single(_recorder.All);
    }

    [Fact]
    public void Current_value_is_current_until_the_last_tick_before_three_minutes_and_expires_once_at_three()
    {
        Apply(CodexSamples.Success);

        _time.Advance(Stale - OneTick);
        Assert.IsType<CodexLimitsCurrent>(_monitor.Current);

        _time.Advance(OneTick);
        AssertUnavailable(CodexUnavailableReason.Expired, _monitor.Current);
        Assert.Equal(2, _recorder.All.Count);

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, _recorder.All.Count);
    }

    [Fact]
    public void Network_failure_after_a_success_is_stale_with_the_same_snapshot_until_three_minutes_after_the_success()
    {
        var measuredAt = _time.GetUtcNow();
        var timestamp = _time.GetTimestamp();
        Apply(CodexSamples.Success);
        _time.Advance(TimeSpan.FromMinutes(1));

        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));

        var stale = Assert.IsType<CodexLimitsStale>(_monitor.Current);
        Assert.Same(CodexSamples.Snapshot, stale.Snapshot);
        Assert.Equal(measuredAt, stale.MeasuredAt);
        Assert.Equal(timestamp, stale.Timestamp);
        Assert.Equal(measuredAt + Stale, stale.ExpiresAt);

        _time.Advance(Stale - TimeSpan.FromMinutes(1) - OneTick);
        Assert.Same(stale, _monitor.Current);

        _time.Advance(OneTick);
        AssertUnavailable(CodexUnavailableReason.NetworkFailed, _monitor.Current);
        Assert.Equal(3, _recorder.All.Count);

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(3, _recorder.All.Count);
    }

    [Fact]
    public void Repeated_network_failures_keep_one_stale_publication()
    {
        Apply(CodexSamples.Success);
        _time.Advance(TimeSpan.FromSeconds(30));
        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));
        var stale = _monitor.Current;

        _time.Advance(TimeSpan.FromSeconds(60));
        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));
        _time.Advance(TimeSpan.FromSeconds(60));
        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));

        Assert.Same(stale, _monitor.Current);
        Assert.Equal(2, _recorder.All.Count);
        Assert.IsType<CodexLimitsStale>(_recorder.All[1]);
    }

    [Theory]
    [MemberData(nameof(NonNetworkReasons))]
    public void Stale_value_ends_at_once_on_any_other_failure_and_does_not_expire_later(CodexUnavailableReason reason)
    {
        Apply(CodexSamples.Success);
        _time.Advance(TimeSpan.FromMinutes(1));
        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));

        Apply(CodexSamples.Failure(reason));

        AssertUnavailable(reason, _monitor.Current);
        Assert.Equal(3, _recorder.All.Count);

        _time.Advance(TimeSpan.FromMinutes(10));
        AssertUnavailable(reason, _monitor.Current);
        Assert.Equal(3, _recorder.All.Count);
    }

    [Theory]
    [MemberData(nameof(NonNetworkReasons))]
    public void Current_value_ends_at_once_on_a_failure_other_than_network(CodexUnavailableReason reason)
    {
        Apply(CodexSamples.Success);

        Apply(CodexSamples.Failure(reason));

        AssertUnavailable(reason, _monitor.Current);
        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(2, _recorder.All.Count);
    }

    [Fact]
    public void Success_after_stale_is_current_and_moves_the_deadline_to_three_minutes_after_the_new_success()
    {
        Apply(CodexSamples.Success);
        _time.Advance(TimeSpan.FromMinutes(1));
        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));
        _time.Advance(TimeSpan.FromMinutes(1));
        var measuredAt = _time.GetUtcNow();

        Apply(new CodexReadSucceeded(CodexSamples.OtherSnapshot));

        var current = Assert.IsType<CodexLimitsCurrent>(_monitor.Current);
        Assert.Same(CodexSamples.OtherSnapshot, current.Snapshot);
        Assert.Equal(measuredAt, current.MeasuredAt);

        // The first success's deadline passes without effect.
        _time.Advance(Stale - OneTick);
        Assert.Same(current, _monitor.Current);

        _time.Advance(OneTick);
        AssertUnavailable(CodexUnavailableReason.Expired, _monitor.Current);
        Assert.Equal(4, _recorder.All.Count);
    }

    [Fact]
    public void Each_success_publishes_even_with_an_equal_snapshot()
    {
        Apply(CodexSamples.Success);
        _time.Advance(TimeSpan.FromMinutes(1));

        Apply(CodexSamples.Success);

        Assert.Equal(2, _recorder.All.Count);
        Assert.Equal(_time.GetUtcNow(), Assert.IsType<CodexLimitsCurrent>(_monitor.Current).MeasuredAt);
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(60_000_000, false)]
    public void Network_failure_is_stale_only_before_the_success_reaches_three_minutes(long ticksFromLimit, bool stale)
    {
        // The success is applied already aged StaleLimit + ticksFromLimit, so no timer fires in between.
        var age = Stale + TimeSpan.FromTicks(ticksFromLimit);
        var ageInTimestampUnits = age.Ticks * _time.TimestampFrequency / TimeSpan.TicksPerSecond;
        _monitor.Apply(CodexSamples.Success, _time.GetUtcNow() - age, _time.GetTimestamp() - ageInTimestampUnits);

        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));

        if (stale)
        {
            Assert.IsType<CodexLimitsStale>(_monitor.Current);
        }
        else
        {
            AssertUnavailable(CodexUnavailableReason.NetworkFailed, _monitor.Current);
        }
    }

    [Fact]
    public void Network_failure_after_a_stale_value_reached_its_age_is_unavailable()
    {
        var age = Stale - TimeSpan.FromSeconds(1);
        var ageInTimestampUnits = age.Ticks * _time.TimestampFrequency / TimeSpan.TicksPerSecond;
        _monitor.Apply(CodexSamples.Success, _time.GetUtcNow() - age, _time.GetTimestamp() - ageInTimestampUnits);
        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));
        Assert.IsType<CodexLimitsStale>(_monitor.Current);

        // Applied directly, so the monitor's own timer (armed for three minutes after Apply) has not fired yet.
        _time.Advance(TimeSpan.FromSeconds(1));
        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));

        AssertUnavailable(CodexUnavailableReason.NetworkFailed, _monitor.Current);
        Assert.Equal(3, _recorder.All.Count);
    }

    [Fact]
    public void Network_failure_after_expiry_is_unavailable_not_stale()
    {
        Apply(CodexSamples.Success);
        _time.Advance(Stale);
        AssertUnavailable(CodexUnavailableReason.Expired, _monitor.Current);

        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));

        AssertUnavailable(CodexUnavailableReason.NetworkFailed, _monitor.Current);
        Assert.Equal(3, _recorder.All.Count);
    }

    [Theory]
    [MemberData(nameof(SourceReasons))]
    public void Identical_repeated_failures_publish_once(CodexUnavailableReason reason)
    {
        Apply(CodexSamples.Failure(reason));
        _time.Advance(TimeSpan.FromMinutes(1));
        Apply(CodexSamples.Failure(reason));
        _time.Advance(TimeSpan.FromMinutes(1));
        Apply(CodexSamples.Failure(reason));

        Assert.Single(_recorder.All);
        AssertUnavailable(reason, _monitor.Current);
    }

    [Fact]
    public void Different_consecutive_failures_each_publish()
    {
        Apply(CodexSamples.Failure(CodexUnavailableReason.SignedOut));
        Apply(CodexSamples.Failure(CodexUnavailableReason.NetworkFailed));
        Apply(CodexSamples.Failure(CodexUnavailableReason.SignedOut));

        Assert.Equal(
            [CodexUnavailableReason.SignedOut, CodexUnavailableReason.NetworkFailed, CodexUnavailableReason.SignedOut],
            _recorder.All.Select(state => Assert.IsType<CodexLimitsUnavailable>(state).Reason));
    }

    private void Apply(CodexReadResult result) => _monitor.Apply(result, _time.GetUtcNow(), _time.GetTimestamp());

    private static void AssertUnavailable(CodexUnavailableReason expected, CodexLimitState state) =>
        Assert.Equal(expected, Assert.IsType<CodexLimitsUnavailable>(state).Reason);

    private sealed class NeverPolledSource : ICodexLimitSource
    {
        public Task<CodexReadResult> ReadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The state tests never poll.");
    }
}
