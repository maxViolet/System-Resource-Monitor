namespace SystemResourceMonitor.Core.Metrics;

/// <summary>
/// Reads one system metric. <see cref="Read"/> is synchronous because the accepted first-release reads are short
/// synchronous native calls; adapters must not wrap them in fake asynchrony. The monitor runs <see cref="Read"/> on a
/// thread-pool worker, never starts a second read while one is pending, stamps the result's time itself, and treats any
/// exception as <see cref="UnavailabilityReason.ReadFailed"/>.
/// </summary>
public interface IMetricSource
{
    MetricKind Kind { get; }

    MetricSample Read();
}

/// <summary>
/// The result of one <see cref="IMetricSource.Read"/>. A sample carries no time; a sample whose shape does
/// not match the source's kind is <see cref="UnavailabilityReason.Invalid"/>.
/// </summary>
public abstract record MetricSample
{
    private protected MetricSample()
    {
    }
}

/// <summary>A CPU load percentage.</summary>
public sealed record MeasuredSample(double Percentage) : MetricSample;

/// <summary>Used and total physical memory; Core computes the percentage from these bytes.</summary>
public sealed record MeasuredRamSample(ulong UsedBytes, ulong TotalBytes) : MetricSample;

/// <summary>A GPU load percentage correlated to one discrete adapter.</summary>
public sealed record MeasuredGpuSample(GpuAdapter Adapter, double Percentage) : MetricSample;

/// <summary>A GPU reading unavailable for an adapter-specific reason.</summary>
public sealed record GpuUnavailableSample : MetricSample
{
    public GpuUnavailableSample(GpuAdapter? adapter, UnavailabilityReason reason)
    {
        if (reason is not (UnavailabilityReason.NotYetSampled or UnavailabilityReason.Invalid or UnavailabilityReason.ReadFailed
            or UnavailabilityReason.NoDiscreteAdapter or UnavailabilityReason.AmbiguousAdapter or UnavailabilityReason.Uncorrelated))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A GPU source cannot report Expired.");
        }

        if ((adapter is null && reason is not (UnavailabilityReason.NoDiscreteAdapter or UnavailabilityReason.AmbiguousAdapter))
            || (adapter is not null && reason is (UnavailabilityReason.NoDiscreteAdapter or UnavailabilityReason.AmbiguousAdapter)))
        {
            throw new ArgumentException("Only no-discrete and ambiguous-adapter results omit the adapter.", nameof(adapter));
        }

        Adapter = adapter;
        Reason = reason;
    }

    public GpuAdapter? Adapter { get; }

    public UnavailabilityReason Reason { get; }
}

/// <summary>
/// No measurement: <see cref="UnavailabilityReason.NotYetSampled"/> (for example, the CPU first sample),
/// <see cref="UnavailabilityReason.Invalid"/> (for example, a counter reset or invalid delta), or
/// <see cref="UnavailabilityReason.ReadFailed"/>. A source never reports <see cref="UnavailabilityReason.Expired"/>.
/// </summary>
public sealed record SampleUnavailable : MetricSample
{
    public SampleUnavailable(UnavailabilityReason reason)
    {
        if (reason is not (UnavailabilityReason.NotYetSampled or UnavailabilityReason.Invalid or UnavailabilityReason.ReadFailed))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A source reports only NotYetSampled, Invalid, or ReadFailed.");
        }

        Reason = reason;
    }

    public UnavailabilityReason Reason { get; }
}
