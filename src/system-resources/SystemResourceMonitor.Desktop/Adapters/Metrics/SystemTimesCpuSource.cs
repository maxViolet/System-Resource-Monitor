using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Desktop.Adapters.Metrics;

/// <summary>
/// CPU load from <c>GetSystemTimes</c> deltas. A failed read is <see cref="UnavailabilityReason.ReadFailed"/> and leaves
/// the calculator's baseline unchanged. Not thread-safe: the monitor never overlaps its reads.
/// </summary>
public sealed class SystemTimesCpuSource : IMetricSource
{
    private readonly ISystemTimesApi _api;
    private readonly CpuLoadCalculator _calculator = new();

    public SystemTimesCpuSource()
        : this(new Win32SystemTimesApi())
    {
    }

    internal SystemTimesCpuSource(ISystemTimesApi api) => _api = api;

    public MetricKind Kind => MetricKind.Cpu;

    public MetricSample Read() =>
        _api.TryRead(out var times, out _)
            ? _calculator.Next(times)
            : new SampleUnavailable(UnavailabilityReason.ReadFailed);
}
