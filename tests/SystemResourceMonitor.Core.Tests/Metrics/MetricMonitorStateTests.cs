using Microsoft.Extensions.Time.Testing;
using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Core.Tests.Metrics;

/// <summary>Applies completed reads to the monitor directly, without its polling loops, and drives expiry with fake time.</summary>
public sealed class MetricMonitorStateTests
{
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);

    private readonly FakeTimeProvider _time = new();
    private readonly MetricMonitor _monitor;
    private readonly PublicationRecorder _recorder;

    public MetricMonitorStateTests()
    {
        _monitor = new MetricMonitor(
            new FakeMetricSource(MetricKind.Cpu, Samples.Cpu), new FakeMetricSource(MetricKind.Ram, Samples.Ram),
            new FakeMetricSource(MetricKind.Gpu, Samples.Gpu), _time);
        _recorder = new PublicationRecorder(_monitor);
    }

    [Theory]
    [InlineData(MetricKind.Cpu)]
    [InlineData(MetricKind.Ram)]
    public void Reading_is_available_until_the_last_tick_before_six_seconds_and_expires_once_at_six(MetricKind kind)
    {
        var reading = Accept(kind, Samples.For(kind));

        _time.Advance(MetricMonitor.ExpiryAge - OneTick);
        Assert.Same(reading, Current(kind));
        Assert.Single(_recorder.All);

        _time.Advance(OneTick);
        AssertReason(UnavailabilityReason.Expired, Current(kind));
        Assert.Equal(2, _recorder.All.Count);

        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(2, _recorder.All.Count);
        Assert.Same(DashboardState.Initial.Get(Samples.Other(kind)), Current(Samples.Other(kind)));
    }

    [Fact]
    public void Gpu_adapter_identity_survives_expiry_for_its_tooltip()
    {
        var adapter = new GpuAdapter("Discrete GPU", new AdapterLuid(1, 2));
        _monitor.Apply(MetricKind.Gpu, new MeasuredGpuSample(adapter, 42), _time.GetUtcNow(), _time.GetTimestamp());

        Assert.Same(adapter, _monitor.Current.GpuAdapter);
        _time.Advance(MetricMonitor.ExpiryAge);

        Assert.Equal(UnavailabilityReason.Expired, Assert.IsType<UnavailableMetric>(_monitor.Current.Gpu).Reason);
        Assert.Same(adapter, _monitor.Current.GpuAdapter);
    }

    [Fact]
    public void Gpu_adapter_change_discards_the_previous_adapters_available_value()
    {
        var previous = new GpuAdapter("Previous", new AdapterLuid(1, 2));
        var current = new GpuAdapter("Current", new AdapterLuid(3, 4));
        _monitor.Apply(MetricKind.Gpu, new MeasuredGpuSample(previous, 42), _time.GetUtcNow(), _time.GetTimestamp());

        _monitor.Apply(MetricKind.Gpu, new GpuUnavailableSample(current, UnavailabilityReason.ReadFailed),
            _time.GetUtcNow(), _time.GetTimestamp());

        Assert.Equal(UnavailabilityReason.ReadFailed, Assert.IsType<UnavailableMetric>(_monitor.Current.Gpu).Reason);
        Assert.Same(current, _monitor.Current.GpuAdapter);
        _time.Advance(MetricMonitor.ExpiryAge);
        Assert.Equal(UnavailabilityReason.ReadFailed, Assert.IsType<UnavailableMetric>(_monitor.Current.Gpu).Reason);
        Assert.Equal(2, _recorder.All.Count);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-3)]
    public void Wall_clock_jumps_neither_expire_nor_extend_a_reading(int hours)
    {
        var time = new SplitClockTimeProvider();
        var monitor = new MetricMonitor(
            new FakeMetricSource(MetricKind.Cpu, Samples.Cpu), new FakeMetricSource(MetricKind.Ram, Samples.Ram),
            new FakeMetricSource(MetricKind.Gpu, Samples.Gpu), time);
        var recorder = new PublicationRecorder(monitor);
        monitor.Apply(MetricKind.Cpu, Samples.Cpu, time.GetUtcNow(), time.GetTimestamp());
        var reading = Assert.IsType<AvailableMetric>(monitor.Current.Cpu);

        time.WallClockJump = TimeSpan.FromHours(hours);
        Assert.Equal(TimeSpan.FromHours(hours), time.GetUtcNow() - reading.MeasuredAt);
        time.Advance(MetricMonitor.ExpiryAge - OneTick);
        Assert.Same(reading, monitor.Current.Cpu);

        time.Advance(OneTick);
        AssertReason(UnavailabilityReason.Expired, monitor.Current.Cpu);
        Assert.Equal(2, recorder.All.Count);
    }

    [Fact]
    public void A_newer_reading_moves_the_deadline_to_six_seconds_after_it()
    {
        Accept(MetricKind.Cpu, Samples.Cpu);
        _time.Advance(TimeSpan.FromSeconds(4));
        var newer = Accept(MetricKind.Cpu, Samples.Cpu);

        _time.Advance(MetricMonitor.ExpiryAge - OneTick);
        Assert.Same(newer, _monitor.Current.Cpu);

        _time.Advance(OneTick);
        AssertReason(UnavailabilityReason.Expired, _monitor.Current.Cpu);
    }

    [Theory]
    [InlineData(UnavailabilityReason.ReadFailed)]
    [InlineData(UnavailabilityReason.NotYetSampled)]
    public void Failed_read_keeps_the_available_reading_and_its_original_deadline(UnavailabilityReason reason)
    {
        var reading = Accept(MetricKind.Cpu, Samples.Cpu);
        _time.Advance(TimeSpan.FromSeconds(2));

        Apply(MetricKind.Cpu, new SampleUnavailable(reason));
        Assert.Same(reading, _monitor.Current.Cpu);
        Assert.Single(_recorder.All);

        _time.Advance(TimeSpan.FromSeconds(4) - OneTick);
        Assert.Same(reading, _monitor.Current.Cpu);

        _time.Advance(OneTick);
        AssertReason(UnavailabilityReason.Expired, _monitor.Current.Cpu);
        Assert.Equal(2, _recorder.All.Count);
    }

    [Fact]
    public void Failed_read_after_expiry_publishes_once_and_a_later_success_makes_the_reading_available_again()
    {
        Accept(MetricKind.Ram, Samples.Ram);
        _time.Advance(MetricMonitor.ExpiryAge);

        Apply(MetricKind.Ram, new SampleUnavailable(UnavailabilityReason.ReadFailed));
        Apply(MetricKind.Ram, new SampleUnavailable(UnavailabilityReason.ReadFailed));
        Assert.Equal(
            [UnavailabilityReason.Expired, UnavailabilityReason.ReadFailed],
            _recorder.All.Skip(1).Select(reading => Assert.IsType<UnavailableMetric>(reading).Reason));

        var renewed = Accept(MetricKind.Ram, Samples.Ram);
        Assert.Same(renewed, _monitor.Current.Ram);
        _time.Advance(MetricMonitor.ExpiryAge - OneTick);
        Assert.Same(renewed, _monitor.Current.Ram);
        _time.Advance(OneTick);
        AssertReason(UnavailabilityReason.Expired, _monitor.Current.Ram);
        Assert.Equal(5, _recorder.All.Count);
    }

    public static TheoryData<MetricKind, MetricSample> KnownInvalidSamples => new()
    {
        { MetricKind.Cpu, new MeasuredSample(double.NaN) },
        { MetricKind.Cpu, new MeasuredSample(double.PositiveInfinity) },
        { MetricKind.Cpu, new MeasuredSample(-1) },
        { MetricKind.Cpu, new MeasuredSample(100.5) },
        { MetricKind.Cpu, new MeasuredRamSample(4, 16) },
        { MetricKind.Cpu, new SampleUnavailable(UnavailabilityReason.Invalid) },
        { MetricKind.Ram, new MeasuredRamSample(17, 16) },
        { MetricKind.Ram, new MeasuredRamSample(0, 0) },
        { MetricKind.Ram, new MeasuredSample(25) },
        { MetricKind.Ram, new SampleUnavailable(UnavailabilityReason.Invalid) },
    };

    [Theory]
    [MemberData(nameof(KnownInvalidSamples))]
    public void Known_invalid_reading_replaces_an_available_one_immediately_and_leaves_the_other_source_alone(
        MetricKind kind, MetricSample sample)
    {
        var other = Samples.Other(kind);
        Accept(kind, Samples.For(kind));
        var otherReading = Accept(other, Samples.For(other));
        _time.Advance(TimeSpan.FromSeconds(1));

        Apply(kind, sample);
        AssertReason(UnavailabilityReason.Invalid, Current(kind));
        Assert.Same(otherReading, Current(other));
        Assert.Equal(3, _recorder.All.Count);

        Apply(kind, sample);
        Assert.Equal(3, _recorder.All.Count);

        _time.Advance(TimeSpan.FromSeconds(30));
        AssertReason(UnavailabilityReason.Invalid, Current(kind));
        AssertReason(UnavailabilityReason.Expired, Current(other));
        Assert.Equal(4, _recorder.All.Count);
    }

    [Fact]
    public void Each_accepted_reading_publishes_even_when_its_value_is_unchanged()
    {
        var first = Accept(MetricKind.Cpu, Samples.Cpu);
        _time.Advance(TimeSpan.FromSeconds(2));
        var second = Accept(MetricKind.Cpu, Samples.Cpu);

        Assert.Equal(first.Percentage, second.Percentage);
        Assert.Equal([first, second], _recorder.All);
    }

    [Fact]
    public void Unavailable_readings_publish_only_when_the_reason_changes()
    {
        UnavailabilityReason[] applied =
        [
            UnavailabilityReason.NotYetSampled, // unchanged from the initial state
            UnavailabilityReason.ReadFailed,
            UnavailabilityReason.ReadFailed,
            UnavailabilityReason.NotYetSampled,
            UnavailabilityReason.Invalid,
            UnavailabilityReason.Invalid,
            UnavailabilityReason.ReadFailed,
        ];

        foreach (var reason in applied)
        {
            Apply(MetricKind.Cpu, new SampleUnavailable(reason));
        }

        Assert.Equal(
            [UnavailabilityReason.ReadFailed, UnavailabilityReason.NotYetSampled, UnavailabilityReason.Invalid, UnavailabilityReason.ReadFailed],
            _recorder.All.Select(reading => Assert.IsType<UnavailableMetric>(reading).Reason));
    }

    [Fact]
    public void Sources_of_the_wrong_kind_are_rejected()
    {
        var cpu = new FakeMetricSource(MetricKind.Cpu, Samples.Cpu);
        var ram = new FakeMetricSource(MetricKind.Ram, Samples.Ram);

        var gpu = new FakeMetricSource(MetricKind.Gpu, Samples.Gpu);
        Assert.Throws<ArgumentException>(() => new MetricMonitor(ram, ram, gpu, _time));
        Assert.Throws<ArgumentException>(() => new MetricMonitor(cpu, cpu, gpu, _time));
    }

    private MetricReading Current(MetricKind kind) => _monitor.Current.Get(kind);

    private AvailableMetric Accept(MetricKind kind, MetricSample sample)
    {
        Apply(kind, sample);
        return Assert.IsType<AvailableMetric>(Current(kind));
    }

    private void Apply(MetricKind kind, MetricSample sample) =>
        _monitor.Apply(kind, sample, _time.GetUtcNow(), _time.GetTimestamp());

    private static void AssertReason(UnavailabilityReason reason, MetricReading reading) =>
        Assert.Equal(reason, Assert.IsType<UnavailableMetric>(reading).Reason);
}
