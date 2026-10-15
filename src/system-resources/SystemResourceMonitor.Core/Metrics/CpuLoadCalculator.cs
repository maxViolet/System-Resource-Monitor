namespace SystemResourceMonitor.Core.Metrics;

/// <summary>
/// System-wide CPU time counters in 100-ns ticks, as Windows reports them: <see cref="Kernel"/> includes
/// <see cref="Idle"/>.
/// </summary>
public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);

/// <summary>
/// Turns successive successful <see cref="CpuTimes"/> reads into CPU load samples. The first read only stores a baseline
/// and is <see cref="UnavailabilityReason.NotYetSampled"/>; a counter reset or an inconsistent delta stores the new read
/// as the baseline and is <see cref="UnavailabilityReason.Invalid"/>. The caller passes only successful reads, so a
/// failed read leaves the baseline unchanged and the next delta covers the gap.
/// </summary>
/// <remarks>
/// Not thread-safe. Its single caller is one B05 polling loop, which never overlaps reads.
/// </remarks>
public sealed class CpuLoadCalculator
{
    private CpuTimes? _baseline;

    public MetricSample Next(CpuTimes current)
    {
        var previous = _baseline;
        _baseline = current;
        if (previous is not { } baseline)
        {
            return new SampleUnavailable(UnavailabilityReason.NotYetSampled);
        }

        if (current.Idle < baseline.Idle || current.Kernel < baseline.Kernel || current.User < baseline.User)
        {
            return new SampleUnavailable(UnavailabilityReason.Invalid);
        }

        var idle = current.Idle - baseline.Idle;
        ulong total;
        try
        {
            total = checked((current.Kernel - baseline.Kernel) + (current.User - baseline.User));
        }
        catch (OverflowException)
        {
            return new SampleUnavailable(UnavailabilityReason.Invalid);
        }

        if (total == 0 || idle > total)
        {
            return new SampleUnavailable(UnavailabilityReason.Invalid);
        }

        return new MeasuredSample((total - idle) / (double)total * 100);
    }
}
