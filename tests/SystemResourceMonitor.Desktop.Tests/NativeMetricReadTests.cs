using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Adapters.Metrics;

namespace SystemResourceMonitor.Desktop.Tests;

/// <summary>
/// Windows integration checks of the production native wrappers. <c>GetSystemTimes</c> has no safe way to fail, so its
/// failure path is covered in <see cref="MetricSourceTests"/> through a fake behind the production source.
/// </summary>
public sealed class NativeMetricReadTests
{
    private const int ErrorInvalidParameter = 87;

    [Fact]
    public async Task System_times_never_decrease_and_give_a_delta_from_0_through_100()
    {
        var api = new Win32SystemTimesApi();
        var calculator = new CpuLoadCalculator();

        Assert.True(api.TryRead(out var first, out var firstError), $"GetSystemTimes failed with {firstError}");
        Assert.True(first.Kernel >= first.Idle, $"kernel {first.Kernel} does not include idle {first.Idle}");
        calculator.Next(first);

        // Let the counters advance by at least one scheduler tick.
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        Assert.True(api.TryRead(out var second, out var secondError), $"GetSystemTimes failed with {secondError}");

        Assert.True(second.Idle >= first.Idle && second.Kernel >= first.Kernel && second.User >= first.User, $"{first} -> {second}");
        var load = Assert.IsType<MeasuredSample>(calculator.Next(second)).Percentage;
        Assert.InRange(load, 0, 100);
    }

    [Fact]
    public void Memory_status_reports_available_within_total()
    {
        var api = new Win32MemoryStatusApi();

        Assert.True(api.TryRead(out var total, out var available, out var error), $"GlobalMemoryStatusEx failed with {error}");

        Assert.True(available > 0 && available <= total, $"available {available} of total {total}");
        Assert.IsType<MeasuredRamSample>(new GlobalMemoryRamSource(api).Read());
    }

    [Fact]
    public void Memory_status_with_a_zero_length_fails_with_invalid_parameter_and_maps_to_read_failed()
    {
        var api = new ZeroLengthMemoryStatus();

        Assert.False(api.Production.TryRead(0, out _, out _, out var error));
        Assert.Equal(ErrorInvalidParameter, error);
        Assert.Equal(
            UnavailabilityReason.ReadFailed,
            Assert.IsType<SampleUnavailable>(new GlobalMemoryRamSource(api).Read()).Reason);
    }

    /// <summary>The production wrapper called with a zero <c>dwLength</c>, which Windows rejects.</summary>
    private sealed class ZeroLengthMemoryStatus : IMemoryStatusApi
    {
        public Win32MemoryStatusApi Production { get; } = new();

        public bool TryRead(out ulong totalPhys, out ulong availPhys, out int error) =>
            Production.TryRead(0, out totalPhys, out availPhys, out error);
    }
}
