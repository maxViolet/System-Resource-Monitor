namespace SystemResourceMonitor.Core.Metrics;

/// <summary>An immutable snapshot of CPU, RAM, and GPU readings.</summary>
public sealed record DashboardState
{
    private DashboardState(MetricReading cpu, MetricReading ram, MetricReading gpu, GpuAdapter? gpuAdapter)
    {
        Cpu = cpu;
        Ram = ram;
        Gpu = gpu;
        GpuAdapter = gpuAdapter;
    }

    /// <summary>The state before any read: all metrics are <see cref="UnavailabilityReason.NotYetSampled"/>.</summary>
    public static DashboardState Initial { get; } = new(
        new UnavailableMetric(MetricKind.Cpu, UnavailabilityReason.NotYetSampled),
        new UnavailableMetric(MetricKind.Ram, UnavailabilityReason.NotYetSampled),
        new UnavailableMetric(MetricKind.Gpu, UnavailabilityReason.NotYetSampled), null);

    public MetricReading Cpu { get; }

    public MetricReading Ram { get; }

    public MetricReading Gpu { get; }

    /// <summary>The adapter from the latest GPU sample, retained after its reading expires.</summary>
    public GpuAdapter? GpuAdapter { get; }

    /// <summary>Replaces a CPU or RAM reading. Use <see cref="WithGpu"/> for GPU so its adapter is explicit.</summary>
    public DashboardState With(MetricReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return reading.Kind switch
        {
            MetricKind.Cpu => new DashboardState(reading, Ram, Gpu, GpuAdapter),
            MetricKind.Ram => new DashboardState(Cpu, reading, Gpu, GpuAdapter),
            MetricKind.Gpu => throw new ArgumentException("Use WithGpu to provide the GPU adapter.", nameof(reading)),
            _ => throw new ArgumentOutOfRangeException(nameof(reading), reading.Kind, "Undefined metric kind."),
        };
    }

    public DashboardState WithGpu(MetricReading reading, GpuAdapter? adapter)
    {
        ArgumentNullException.ThrowIfNull(reading);
        if (reading.Kind != MetricKind.Gpu)
        {
            throw new ArgumentException("The reading must be for GPU.", nameof(reading));
        }

        return new DashboardState(Cpu, Ram, reading, adapter);
    }

    internal MetricReading Get(MetricKind kind) => kind switch
    {
        MetricKind.Cpu => Cpu,
        MetricKind.Ram => Ram,
        MetricKind.Gpu => Gpu,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined metric kind."),
    };
}
