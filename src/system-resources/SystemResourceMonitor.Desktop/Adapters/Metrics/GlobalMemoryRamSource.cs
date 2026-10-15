using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Desktop.Adapters.Metrics;

/// <summary>
/// Physical memory in use from <c>GlobalMemoryStatusEx</c>. Core computes the percentage from the bytes.
/// </summary>
public sealed class GlobalMemoryRamSource : IMetricSource
{
    private readonly IMemoryStatusApi _api;

    public GlobalMemoryRamSource()
        : this(new Win32MemoryStatusApi())
    {
    }

    internal GlobalMemoryRamSource(IMemoryStatusApi api) => _api = api;

    public MetricKind Kind => MetricKind.Ram;

    public MetricSample Read()
    {
        if (!_api.TryRead(out var total, out var available, out _))
        {
            return new SampleUnavailable(UnavailabilityReason.ReadFailed);
        }

        return total == 0 || available > total
            ? new SampleUnavailable(UnavailabilityReason.Invalid)
            : new MeasuredRamSample(total - available, total);
    }
}
