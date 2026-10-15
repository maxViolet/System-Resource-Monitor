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
/// The result of one <see cref="IMetricSource.Read"/>: exactly one of <see cref="MeasuredSample"/>,
/// <see cref="MeasuredRamSample"/>, or <see cref="SampleUnavailable"/>. A sample carries no time; a sample whose shape does
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
