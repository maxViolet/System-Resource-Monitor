namespace SystemResourceMonitor.Core.Metrics;

/// <summary>An immutable snapshot of the CPU and RAM readings.</summary>
public sealed record DashboardState
{
    private DashboardState(MetricReading cpu, MetricReading ram)
    {
        Cpu = cpu;
        Ram = ram;
    }

    /// <summary>The state before any read: both metrics are <see cref="UnavailabilityReason.NotYetSampled"/>.</summary>
    public static DashboardState Initial { get; } = new(
        new UnavailableMetric(MetricKind.Cpu, UnavailabilityReason.NotYetSampled),
        new UnavailableMetric(MetricKind.Ram, UnavailabilityReason.NotYetSampled));

    public MetricReading Cpu { get; }

    public MetricReading Ram { get; }

    /// <summary>Returns a state with the reading of <paramref name="reading"/>'s kind replaced and the other unchanged.</summary>
    public DashboardState With(MetricReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        return reading.Kind switch
        {
            MetricKind.Cpu => new DashboardState(reading, Ram),
            MetricKind.Ram => new DashboardState(Cpu, reading),
            _ => throw new ArgumentOutOfRangeException(nameof(reading), reading.Kind, "Undefined metric kind."),
        };
    }

    internal MetricReading Get(MetricKind kind) => kind switch
    {
        MetricKind.Cpu => Cpu,
        MetricKind.Ram => Ram,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined metric kind."),
    };
}
