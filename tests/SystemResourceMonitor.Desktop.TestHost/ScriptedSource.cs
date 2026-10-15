using System.Diagnostics;
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
    private long _lastSuccess;

    public MetricKind Kind => kind;

    /// <summary>
    /// The <see cref="Stopwatch"/> timestamp of the last returned sample, taken just before the monitor stamps it, or
    /// <see langword="null"/> before the first.
    /// </summary>
    public long? LastSuccess => Interlocked.Read(ref _lastSuccess) is var last and not 0 ? last : null;

    public MetricSample Read()
    {
        var sample = script(_reads++);
        if (sample is null)
        {
            Thread.Sleep(Timeout.Infinite);
        }

        Interlocked.Exchange(ref _lastSuccess, Stopwatch.GetTimestamp());
        return sample!;
    }

    /// <summary>Alternates between two values, so every accepted read changes the rendered text.</summary>
    public static ScriptedSource Steady(MetricKind kind) => new(kind, read => Measured(kind, read % 2 == 0 ? 12 : 64));

    /// <summary>A sample of <paramref name="kind"/> at <paramref name="percentage"/>, with 16 GiB total for RAM.</summary>
    public static MetricSample Measured(MetricKind kind, double percentage) => kind == MetricKind.Cpu
        ? new MeasuredSample(percentage)
        : new MeasuredRamSample((ulong)(16 * Gib * percentage / 100), 16 * Gib);

    /// <summary>Blocks every read, so the monitor publishes nothing after its initial state.</summary>
    public static ScriptedSource Blocked(MetricKind kind) => new(kind, _ => null);
}
