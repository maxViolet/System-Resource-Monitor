using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Adapters.Metrics;

namespace SystemResourceMonitor.Desktop.Tests;

/// <summary>Maps native read results to metric samples through fake native seams.</summary>
public sealed class MetricSourceTests
{
    private const int ErrorAccessDenied = 5;

    [Fact]
    public void Cpu_source_reports_its_kind_and_waits_for_a_baseline()
    {
        var source = new SystemTimesCpuSource(new ScriptedSystemTimes(new CpuTimes(100, 300, 200)));

        Assert.Equal(MetricKind.Cpu, source.Kind);
        AssertUnavailable(UnavailabilityReason.NotYetSampled, source.Read());
    }

    [Fact]
    public void Failed_cpu_read_is_read_failed_and_keeps_the_original_baseline()
    {
        var source = new SystemTimesCpuSource(new ScriptedSystemTimes(
            new CpuTimes(100, 300, 200),
            null,
            new CpuTimes(200, 400, 300)));

        AssertUnavailable(UnavailabilityReason.NotYetSampled, source.Read());
        AssertUnavailable(UnavailabilityReason.ReadFailed, source.Read());

        Assert.Equal(50, Assert.IsType<MeasuredSample>(source.Read()).Percentage);
    }

    [Fact]
    public void Cpu_failure_before_the_first_success_leaves_no_baseline()
    {
        var source = new SystemTimesCpuSource(new ScriptedSystemTimes(null, new CpuTimes(100, 300, 200)));

        AssertUnavailable(UnavailabilityReason.ReadFailed, source.Read());
        AssertUnavailable(UnavailabilityReason.NotYetSampled, source.Read());
    }

    [Fact]
    public void Ram_source_reports_used_and_total_bytes()
    {
        var source = new GlobalMemoryRamSource(new ScriptedMemoryStatus((Total: 16UL << 30, Available: 6UL << 30)));

        Assert.Equal(MetricKind.Ram, source.Kind);
        var sample = Assert.IsType<MeasuredRamSample>(source.Read());
        Assert.Equal(10UL << 30, sample.UsedBytes);
        Assert.Equal(16UL << 30, sample.TotalBytes);
    }

    [Fact]
    public void Ram_with_all_memory_available_is_zero_used()
    {
        var source = new GlobalMemoryRamSource(new ScriptedMemoryStatus((Total: 1024, Available: 1024)));

        Assert.Equal(new MeasuredRamSample(0, 1024), source.Read());
    }

    [Fact]
    public void Failed_ram_read_is_read_failed()
    {
        var source = new GlobalMemoryRamSource(new ScriptedMemoryStatus(null));

        AssertUnavailable(UnavailabilityReason.ReadFailed, source.Read());
    }

    [Theory]
    [InlineData(0UL, 0UL)]
    [InlineData(0UL, 1UL)]
    [InlineData(1024UL, 1025UL)]
    public void Inconsistent_ram_totals_are_invalid_before_subtraction(ulong total, ulong available)
    {
        var source = new GlobalMemoryRamSource(new ScriptedMemoryStatus((total, available)));

        AssertUnavailable(UnavailabilityReason.Invalid, source.Read());
    }

    private static void AssertUnavailable(UnavailabilityReason expected, MetricSample sample) =>
        Assert.Equal(expected, Assert.IsType<SampleUnavailable>(sample).Reason);

    /// <summary>Returns each scripted read in turn; <see langword="null"/> is a failed call.</summary>
    private sealed class ScriptedSystemTimes(params CpuTimes?[] reads) : ISystemTimesApi
    {
        private int _next;

        public bool TryRead(out CpuTimes times, out int error)
        {
            var read = reads[_next++];
            times = read ?? default;
            error = read is null ? ErrorAccessDenied : 0;
            return read is not null;
        }
    }

    private sealed class ScriptedMemoryStatus((ulong Total, ulong Available)? read) : IMemoryStatusApi
    {
        public bool TryRead(out ulong totalPhys, out ulong availPhys, out int error)
        {
            totalPhys = read?.Total ?? 0;
            availPhys = read?.Available ?? 0;
            error = read is null ? ErrorAccessDenied : 0;
            return read is not null;
        }
    }
}
