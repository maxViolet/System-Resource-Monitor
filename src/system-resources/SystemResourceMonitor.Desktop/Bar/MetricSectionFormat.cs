using System.Globalization;
using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Desktop.Bar;

/// <summary>The text and optional tooltip of one bar section.</summary>
public readonly record struct MetricSection(string Text, string? ToolTip);

/// <summary>
/// Maps a <see cref="MetricReading"/> to its bar section with invariant culture. Tooltips never carry exception text or
/// error codes.
/// </summary>
public static class MetricSectionFormat
{
    public const string Unavailable = "—";
    private const double BytesPerGib = 1UL << 30;

    // Derived from the monitor's expiry age, so the tooltip cannot state a different age.
    private static readonly string ExpiredExplanation =
        string.Create(CultureInfo.InvariantCulture, $"No update for {MetricMonitor.ExpiryAge.TotalSeconds:0} seconds.");

    public static MetricSection Format(MetricReading reading, GpuAdapter? gpuAdapter = null)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var label = Label(reading.Kind);
        return reading switch
        {
            AvailableMetric available => new MetricSection(
                string.Create(CultureInfo.InvariantCulture, $"{label} {Math.Round(available.Percentage, MidpointRounding.AwayFromZero):0}%"),
                available.Kind == MetricKind.Gpu ? Description(gpuAdapter ?? available.GpuAdapter)
                    : available.RamBytes is { } bytes
                    ? string.Create(CultureInfo.InvariantCulture, $"RAM {bytes.UsedBytes / BytesPerGib:0.0} / {bytes.TotalBytes / BytesPerGib:0.0} GiB used")
                    : null),
            UnavailableMetric unavailable => new MetricSection($"{label} {Unavailable}", Explain(unavailable.Reason,
                unavailable.Kind == MetricKind.Gpu ? gpuAdapter : null)),
            _ => throw new ArgumentOutOfRangeException(nameof(reading), reading, "Unknown metric reading."),
        };
    }

    /// <summary>Every text a section of <paramref name="kind"/> can show: 0% through 100% and the dash.</summary>
    public static IEnumerable<string> WidthCandidates(MetricKind kind)
    {
        var label = Label(kind);
        for (var percentage = 0; percentage <= 100; percentage++)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"{label} {percentage}%");
        }

        yield return $"{label} {Unavailable}";
    }

    private static string Label(MetricKind kind) => kind switch
    {
        MetricKind.Cpu => "CPU",
        MetricKind.Ram => "RAM",
        MetricKind.Gpu => "GPU",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined metric kind."),
    };

    /// <summary>The safe tooltip for <paramref name="reason"/>; an undefined reason throws.</summary>
    internal static string Explain(UnavailabilityReason reason, GpuAdapter? adapter = null)
    {
        var explanation = reason switch
        {
            UnavailabilityReason.NoDiscreteAdapter => "No discrete GPU found.",
            UnavailabilityReason.AmbiguousAdapter => "More than one discrete GPU found; none is chosen.",
            UnavailabilityReason.Uncorrelated => $"{Description(adapter) ?? "GPU"}: GPU counters could not be matched to this adapter.",
            UnavailabilityReason.NotYetSampled => "Waiting for the first measurement.",
            UnavailabilityReason.Expired => ExpiredExplanation,
            UnavailabilityReason.Invalid => "Windows returned an inconsistent measurement.",
            UnavailabilityReason.ReadFailed => "Windows did not return a measurement.",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Undefined reason."),
        };
        return adapter is not null && reason is UnavailabilityReason.NotYetSampled or UnavailabilityReason.Expired
            or UnavailabilityReason.Invalid or UnavailabilityReason.ReadFailed
            ? $"{Description(adapter)}: {explanation}"
            : explanation;
    }

    private static string? Description(GpuAdapter? adapter) => adapter is null
        ? null
        : adapter.Description.Length <= 64 ? adapter.Description : adapter.Description[..64];
}
