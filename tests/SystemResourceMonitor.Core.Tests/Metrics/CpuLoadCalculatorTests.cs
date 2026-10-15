using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Core.Tests.Metrics;

public sealed class CpuLoadCalculatorTests
{
    private static readonly CpuTimes Baseline = new(Idle: 1_000, Kernel: 3_000, User: 2_000);

    [Fact]
    public void First_sample_stores_the_baseline_and_is_not_yet_sampled()
    {
        var calculator = new CpuLoadCalculator();

        var sample = Assert.IsType<SampleUnavailable>(calculator.Next(Baseline));

        Assert.Equal(UnavailabilityReason.NotYetSampled, sample.Reason);
        Assert.Equal(50, Measured(calculator.Next(Advance(Baseline, idle: 100, kernel: 100, user: 100))));
    }

    [Theory]
    [InlineData(1_000UL, 1_000UL, 0UL, 0.0)]
    [InlineData(500UL, 500UL, 500UL, 50.0)]
    [InlineData(0UL, 400UL, 600UL, 100.0)]
    [InlineData(250UL, 500UL, 500UL, 75.0)]
    public void Delta_is_busy_share_of_kernel_plus_user(ulong idle, ulong kernel, ulong user, double expected)
    {
        var calculator = Started();

        Assert.Equal(expected, Measured(calculator.Next(Advance(Baseline, idle, kernel, user))));
    }

    [Fact]
    public void Each_valid_delta_moves_the_baseline()
    {
        var calculator = Started();
        var second = Advance(Baseline, idle: 0, kernel: 100, user: 100);

        Assert.Equal(100, Measured(calculator.Next(second)));
        Assert.Equal(0, Measured(calculator.Next(Advance(second, idle: 100, kernel: 100, user: 0))));
    }

    public static TheoryData<CpuTimes> ResetCounters =>
    [
        Baseline with { Idle = Baseline.Idle - 1 },
        Baseline with { Kernel = Baseline.Kernel - 1 },
        Baseline with { User = Baseline.User - 1 },
    ];

    [Theory]
    [MemberData(nameof(ResetCounters))]
    public void A_lower_counter_is_a_reset_that_rebases_and_is_invalid(CpuTimes reset)
    {
        var calculator = Started();

        AssertInvalid(calculator.Next(reset));

        Assert.Equal(50, Measured(calculator.Next(Advance(reset, idle: 100, kernel: 100, user: 100))));
    }

    [Fact]
    public void Zero_total_is_invalid_and_rebases()
    {
        var calculator = Started();

        AssertInvalid(calculator.Next(Baseline));

        Assert.Equal(100, Measured(calculator.Next(Advance(Baseline, idle: 0, kernel: 0, user: 10))));
    }

    [Fact]
    public void Idle_greater_than_total_is_invalid_and_rebases()
    {
        var calculator = Started();
        var inconsistent = Advance(Baseline, idle: 300, kernel: 100, user: 100);

        AssertInvalid(calculator.Next(inconsistent));

        Assert.Equal(0, Measured(calculator.Next(Advance(inconsistent, idle: 200, kernel: 200, user: 0))));
    }

    [Fact]
    public void Overflowing_total_is_invalid_and_rebases()
    {
        var calculator = new CpuLoadCalculator();
        var start = new CpuTimes(Idle: 0, Kernel: 0, User: 0);
        calculator.Next(start);
        var overflow = new CpuTimes(Idle: 0, Kernel: ulong.MaxValue, User: 1);

        AssertInvalid(calculator.Next(overflow));

        Assert.Equal(50, Measured(calculator.Next(overflow with { Idle = 1, User = 3 })));
    }

    private static CpuLoadCalculator Started()
    {
        var calculator = new CpuLoadCalculator();
        calculator.Next(Baseline);
        return calculator;
    }

    private static CpuTimes Advance(CpuTimes from, ulong idle, ulong kernel, ulong user) =>
        // Windows counts idle time inside kernel time.
        new(from.Idle + idle, from.Kernel + kernel, from.User + user);

    private static double Measured(MetricSample sample) => Assert.IsType<MeasuredSample>(sample).Percentage;

    private static void AssertInvalid(MetricSample sample) =>
        Assert.Equal(UnavailabilityReason.Invalid, Assert.IsType<SampleUnavailable>(sample).Reason);
}
