using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Core.Tests.Metrics;

public sealed class MetricReadingTests
{
    private const long Timestamp = 42;
    private static readonly DateTimeOffset MeasuredAt = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    public static TheoryData<double> ValidPercentages => [0, double.Epsilon, 50.5, 100];

    public static TheoryData<double> InvalidPercentages =>
        [Math.BitDecrement(0), -1, Math.BitIncrement(100), 101, double.NaN, double.PositiveInfinity, double.NegativeInfinity];

    [Theory]
    [MemberData(nameof(ValidPercentages))]
    public void Cpu_percentage_from_0_through_100_is_available_without_bytes(double percentage)
    {
        var reading = Assert.IsType<AvailableMetric>(Create(MetricKind.Cpu, new MeasuredSample(percentage)));

        Assert.Equal(MetricKind.Cpu, reading.Kind);
        Assert.Equal(percentage, reading.Percentage);
        Assert.Equal(MeasuredAt, reading.MeasuredAt);
        Assert.Equal(Timestamp, reading.Timestamp);
        Assert.Null(reading.RamBytes);
    }

    [Theory]
    [MemberData(nameof(InvalidPercentages))]
    public void Cpu_percentage_outside_0_through_100_or_not_finite_is_invalid(double percentage)
    {
        AssertUnavailable(MetricKind.Cpu, UnavailabilityReason.Invalid, Create(MetricKind.Cpu, new MeasuredSample(percentage)));
    }

    [Theory]
    [InlineData(0UL, 16UL, 0)]
    [InlineData(4UL, 16UL, 25)]
    [InlineData(16UL, 16UL, 100)]
    [InlineData(ulong.MaxValue, ulong.MaxValue, 100)]
    public void Ram_percentage_is_computed_from_consistent_bytes(ulong used, ulong total, double expected)
    {
        var reading = Assert.IsType<AvailableMetric>(Create(MetricKind.Ram, new MeasuredRamSample(used, total)));

        Assert.Equal(MetricKind.Ram, reading.Kind);
        Assert.Equal(expected, reading.Percentage);
        Assert.Equal(new RamBytes(used, total), reading.RamBytes);
    }

    [Theory]
    [InlineData(0UL, 0UL)]
    [InlineData(1UL, 0UL)]
    [InlineData(17UL, 16UL)]
    public void Ram_bytes_with_zero_total_or_used_above_total_are_invalid(ulong used, ulong total)
    {
        AssertUnavailable(MetricKind.Ram, UnavailabilityReason.Invalid, Create(MetricKind.Ram, new MeasuredRamSample(used, total)));
    }

    [Fact]
    public void Cpu_carrying_ram_bytes_is_invalid()
    {
        AssertUnavailable(MetricKind.Cpu, UnavailabilityReason.Invalid, Create(MetricKind.Cpu, new MeasuredRamSample(4, 16)));
    }

    [Fact]
    public void Ram_without_bytes_is_invalid()
    {
        AssertUnavailable(MetricKind.Ram, UnavailabilityReason.Invalid, Create(MetricKind.Ram, new MeasuredSample(25)));
        AssertUnavailable(MetricKind.Ram, UnavailabilityReason.Invalid, Create(MetricKind.Ram, null));
    }

    [Theory]
    [InlineData(UnavailabilityReason.NotYetSampled)]
    [InlineData(UnavailabilityReason.Invalid)]
    [InlineData(UnavailabilityReason.ReadFailed)]
    public void Source_unavailability_keeps_its_reason(UnavailabilityReason reason)
    {
        AssertUnavailable(MetricKind.Cpu, reason, Create(MetricKind.Cpu, new SampleUnavailable(reason)));
        AssertUnavailable(MetricKind.Ram, reason, Create(MetricKind.Ram, new SampleUnavailable(reason)));
    }

    [Fact]
    public void A_source_cannot_report_expired()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SampleUnavailable(UnavailabilityReason.Expired));
        AssertUnavailable(MetricKind.Cpu, UnavailabilityReason.Expired, new UnavailableMetric(MetricKind.Cpu, UnavailabilityReason.Expired));
    }

    [Fact]
    public void Valid_zero_is_available_and_distinct_from_unavailable()
    {
        MetricReading[] zeros = [Create(MetricKind.Cpu, new MeasuredSample(0)), Create(MetricKind.Ram, new MeasuredRamSample(0, 16))];

        Assert.All(zeros, zero =>
        {
            Assert.Equal(0, Assert.IsType<AvailableMetric>(zero).Percentage);
            Assert.IsNotType<UnavailableMetric>(zero);
        });
    }

    [Fact]
    public void Programmer_errors_throw_instead_of_becoming_unavailable()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create((MetricKind)7, new MeasuredSample(1)));
        Assert.Throws<ArgumentException>(() =>
            AvailableMetric.Create(MetricKind.Cpu, new MeasuredSample(1), MeasuredAt.ToOffset(TimeSpan.FromHours(5)), Timestamp));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnavailableMetric((MetricKind)7, UnavailabilityReason.Invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnavailableMetric(MetricKind.Cpu, (UnavailabilityReason)9));
    }

    [Fact]
    public void Initial_dashboard_state_has_not_sampled_either_metric()
    {
        AssertUnavailable(MetricKind.Cpu, UnavailabilityReason.NotYetSampled, DashboardState.Initial.Cpu);
        AssertUnavailable(MetricKind.Ram, UnavailabilityReason.NotYetSampled, DashboardState.Initial.Ram);
    }

    [Fact]
    public void Dashboard_state_replaces_only_the_reading_of_the_same_kind()
    {
        var cpu = Create(MetricKind.Cpu, new MeasuredSample(12));
        var ram = Create(MetricKind.Ram, new MeasuredRamSample(4, 16));

        var withCpu = DashboardState.Initial.With(cpu);
        var withBoth = withCpu.With(ram);

        Assert.Same(cpu, withCpu.Cpu);
        Assert.Same(DashboardState.Initial.Ram, withCpu.Ram);
        Assert.Same(cpu, withBoth.Cpu);
        Assert.Same(ram, withBoth.Ram);
        AssertUnavailable(MetricKind.Cpu, UnavailabilityReason.NotYetSampled, DashboardState.Initial.Cpu);
    }

    private static MetricReading Create(MetricKind kind, MetricSample? sample) =>
        AvailableMetric.Create(kind, sample, MeasuredAt, Timestamp);

    private static void AssertUnavailable(MetricKind kind, UnavailabilityReason reason, MetricReading reading)
    {
        var unavailable = Assert.IsType<UnavailableMetric>(reading);
        Assert.Equal(kind, unavailable.Kind);
        Assert.Equal(reason, unavailable.Reason);
    }
}
