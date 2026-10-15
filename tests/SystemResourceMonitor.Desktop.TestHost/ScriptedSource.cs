using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Desktop.TestHost;

/// <summary>
/// A handle-free fake <see cref="IMetricSource"/>. The script receives the zero-based read number and returns a sample,
/// or <see langword="null"/> to block that read forever without a kernel handle.
/// </summary>
internal sealed class ScriptedSource(MetricKind kind, Func<int, MetricSample?> script) : IMetricSource
{
    private const ulong Gib = 1UL << 30;
    private int _reads;

    public MetricKind Kind => kind;

    public MetricSample Read()
    {
        var sample = script(_reads++);
        if (sample is null)
        {
            Thread.Sleep(Timeout.Infinite);
        }

        return sample!;
    }

    public static ScriptedSource Steady(MetricKind kind) => new(kind, _ => kind == MetricKind.Cpu
        ? new MeasuredSample(12)
        : new MeasuredRamSample(10 * Gib + Gib / 4, 16 * Gib));

    /// <summary>A sample of <paramref name="kind"/> at <paramref name="percentage"/>, with 16 GiB total for RAM.</summary>
    public static MetricSample Measured(MetricKind kind, double percentage) => kind == MetricKind.Cpu
        ? new MeasuredSample(percentage)
        : new MeasuredRamSample((ulong)(16 * Gib * percentage / 100), 16 * Gib);

    /// <summary>Blocks every read, so the monitor publishes nothing after its initial state.</summary>
    public static ScriptedSource Blocked(MetricKind kind) => new(kind, _ => null);
}
