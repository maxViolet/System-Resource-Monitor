namespace SystemResourceMonitor.Core.Metrics;

/// <summary>The system metrics the bar monitors.</summary>
public enum MetricKind
{
    Cpu,
    Ram,
    Gpu,
}

/// <summary>Why a metric has no trustworthy current value. Unavailable is never shown as zero.</summary>
public enum UnavailabilityReason
{
    /// <summary>No measurement has been accepted yet, or the source needs a baseline first.</summary>
    NotYetSampled,

    /// <summary>The last accepted measurement reached its monotonic age limit.</summary>
    Expired,

    /// <summary>The source reported a value that cannot be a valid measurement.</summary>
    Invalid,

    /// <summary>The source could not read a value.</summary>
    ReadFailed,
    NoDiscreteAdapter,
    AmbiguousAdapter,
    Uncorrelated,
}

/// <summary>The Windows adapter identity used to correlate GPU counters.</summary>
public readonly record struct AdapterLuid(uint LowPart, int HighPart);

/// <summary>A selected discrete GPU adapter.</summary>
public sealed record GpuAdapter(string Description, AdapterLuid Luid);

/// <summary>Physical memory in use and in total, in bytes.</summary>
public readonly record struct RamBytes(ulong UsedBytes, ulong TotalBytes);

/// <summary>
/// The current state of one system metric: exactly one of <see cref="AvailableMetric"/> or <see cref="UnavailableMetric"/>.
/// </summary>
public abstract record MetricReading
{
    private protected MetricReading(MetricKind kind)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined metric kind.");
        }

        Kind = kind;
    }

    public MetricKind Kind { get; }
}

/// <summary>
/// A validated measurement. Construct it only through <see cref="Create"/>, so an instance always holds a finite
/// percentage from 0 through 100, and RAM bytes for RAM only.
/// </summary>
public sealed record AvailableMetric : MetricReading
{
    private AvailableMetric(MetricKind kind, double percentage, DateTimeOffset measuredAt, long timestamp, RamBytes? ramBytes, GpuAdapter? gpuAdapter)
        : base(kind)
    {
        Percentage = percentage;
        MeasuredAt = measuredAt;
        Timestamp = timestamp;
        RamBytes = ramBytes;
        GpuAdapter = gpuAdapter;
    }

    /// <summary>A finite value from 0 through 100 inclusive; 0 is a valid measurement.</summary>
    public double Percentage { get; }

    /// <summary>UTC measurement time for display and evidence only; never used for age.</summary>
    public DateTimeOffset MeasuredAt { get; }

    /// <summary>Monotonic measurement time from <see cref="TimeProvider.GetTimestamp"/>; the only basis for age and deadlines.</summary>
    public long Timestamp { get; }

    /// <summary>Used and total physical memory; present for RAM and absent for CPU.</summary>
    public RamBytes? RamBytes { get; }

    /// <summary>The adapter for a GPU measurement; absent for CPU and RAM.</summary>
    public GpuAdapter? GpuAdapter { get; }

    /// <summary>
    /// Validates a source sample stamped at <paramref name="measuredAt"/> and <paramref name="timestamp"/>.
    /// Returns an <see cref="AvailableMetric"/>, or an <see cref="UnavailableMetric"/> carrying the sample's reason or
    /// <see cref="UnavailabilityReason.Invalid"/> for a value that is out of range, inconsistent, or the wrong shape for
    /// <paramref name="kind"/>. RAM percentage is computed from the bytes. Throws only for programmer errors: an undefined
    /// kind or a non-UTC <paramref name="measuredAt"/>.
    /// </summary>
    public static MetricReading Create(MetricKind kind, MetricSample? sample, DateTimeOffset measuredAt, long timestamp)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined metric kind.");
        }

        if (measuredAt.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Measurement time must be UTC.", nameof(measuredAt));
        }

        return (kind, sample) switch
        {
            (MetricKind.Gpu, GpuUnavailableSample gpuUnavailable) => new UnavailableMetric(kind, gpuUnavailable.Reason),
            (MetricKind.Gpu, MeasuredGpuSample { Adapter: { } adapter, Percentage: var gpuPercentage })
                when double.IsFinite(gpuPercentage) && gpuPercentage is >= 0 and <= 100
                => new AvailableMetric(kind, gpuPercentage, measuredAt, timestamp, null, adapter),
            (_, SampleUnavailable unavailable) when unavailable.Reason is UnavailabilityReason.NotYetSampled or UnavailabilityReason.Invalid or UnavailabilityReason.ReadFailed
                => new UnavailableMetric(kind, unavailable.Reason),
            (MetricKind.Cpu, MeasuredSample { Percentage: var percentage }) when double.IsFinite(percentage) && percentage is >= 0 and <= 100
                => new AvailableMetric(kind, percentage, measuredAt, timestamp, null, null),
            (MetricKind.Ram, MeasuredRamSample { TotalBytes: > 0 } ram) when ram.UsedBytes <= ram.TotalBytes
                => new AvailableMetric(kind, (double)ram.UsedBytes / ram.TotalBytes * 100, measuredAt, timestamp,
                    new RamBytes(ram.UsedBytes, ram.TotalBytes), null),
            _ => new UnavailableMetric(kind, UnavailabilityReason.Invalid),
        };
    }
}

/// <summary>A metric without a trustworthy current value, and why.</summary>
public sealed record UnavailableMetric : MetricReading
{
    public UnavailableMetric(MetricKind kind, UnavailabilityReason reason)
        : base(kind)
    {
        if (!Enum.IsDefined(reason) || (kind != MetricKind.Gpu && reason is UnavailabilityReason.NoDiscreteAdapter or UnavailabilityReason.AmbiguousAdapter or UnavailabilityReason.Uncorrelated))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "Undefined unavailability reason.");
        }

        Reason = reason;
    }

    public UnavailabilityReason Reason { get; }
}
