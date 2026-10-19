using System.Globalization;
using System.Text.RegularExpressions;

namespace SystemResourceMonitor.Core.Metrics;

public sealed record GpuAdapterCandidate(string Description, AdapterLuid Luid, bool IsHardware, bool IsIntegrated, bool IsDetachable = false);

public sealed record GpuAdapterSelection(GpuAdapter? Adapter, UnavailabilityReason? Reason);

public static class GpuAdapterSelector
{
    public static GpuAdapterSelection Select(IReadOnlyList<GpuAdapterCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var discrete = candidates.Where(candidate => candidate.IsHardware && !candidate.IsIntegrated).ToArray();
        return discrete.Length switch
        {
            0 => new GpuAdapterSelection(null, UnavailabilityReason.NoDiscreteAdapter),
            1 => new GpuAdapterSelection(new GpuAdapter(discrete[0].Description, discrete[0].Luid), null),
            _ => new GpuAdapterSelection(null, UnavailabilityReason.AmbiguousAdapter),
        };
    }
}

public enum GpuEngineType
{
    ThreeD,
    Compute,
    Copy,
    Video,
    Other,
    Unspecified,
}

public readonly partial record struct GpuEngineInstance(AdapterLuid Luid, int PhysicalIndex, int EngineIndex, GpuEngineType Engine)
{
    [GeneratedRegex("^pid_[0-9]+_luid_0x(?<high>[0-9a-f]{8})_0x(?<low>[0-9a-f]{8})_phys_(?<physical>[0-9]+)_eng_(?<index>[0-9]+)_engtype_(?<engine>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstancePattern();

    public static bool TryParse(string instanceName, out GpuEngineInstance instance)
    {
        instance = default;
        if (instanceName is null)
        {
            return false;
        }

        var match = InstancePattern().Match(instanceName);
        if (!match.Success
            || !uint.TryParse(match.Groups["low"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low)
            || !int.TryParse(match.Groups["high"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high)
            || !int.TryParse(match.Groups["physical"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var physical)
            || !int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            return false;
        }

        var engineName = match.Groups["engine"].Value;
        var engine = engineName.Length == 0 ? GpuEngineType.Unspecified
            : engineName.StartsWith("3d", StringComparison.OrdinalIgnoreCase) ? GpuEngineType.ThreeD
            : engineName.StartsWith("compute", StringComparison.OrdinalIgnoreCase) ? GpuEngineType.Compute
            : engineName.StartsWith("copy", StringComparison.OrdinalIgnoreCase) ? GpuEngineType.Copy
            : engineName.StartsWith("video", StringComparison.OrdinalIgnoreCase) ? GpuEngineType.Video
            : GpuEngineType.Other;

        instance = new GpuEngineInstance(new AdapterLuid(low, high), physical, index, engine);
        return true;
    }
}

public sealed record GpuCounterItem(string InstanceName, double Value, bool IsValid);

public static class GpuLoadCalculator
{
    public static MetricSample Calculate(GpuAdapter selected, IReadOnlyList<GpuCounterItem> items)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(items);
        var sums = new Dictionary<(GpuEngineType Type, int Index), double>();
        var foundSelectedEngine = false;
        var foundValidSelectedEngine = false;

        foreach (var item in items)
        {
            if (!GpuEngineInstance.TryParse(item.InstanceName, out var parsed))
            {
                return new GpuUnavailableSample(selected, UnavailabilityReason.Uncorrelated);
            }

            if (parsed.Luid != selected.Luid)
            {
                continue;
            }

            if (parsed.PhysicalIndex != 0)
            {
                return new GpuUnavailableSample(selected, UnavailabilityReason.Uncorrelated);
            }

            if (parsed.Engine == GpuEngineType.Unspecified)
            {
                return new GpuUnavailableSample(selected, UnavailabilityReason.Uncorrelated);
            }

            if (parsed.Engine == GpuEngineType.Other)
            {
                continue;
            }

            foundSelectedEngine = true;
            if (!item.IsValid)
            {
                continue;
            }

            foundValidSelectedEngine = true;
            if (!double.IsFinite(item.Value) || item.Value is < 0 or > 100)
            {
                return new GpuUnavailableSample(selected, UnavailabilityReason.Invalid);
            }

            var engine = (parsed.Engine, parsed.EngineIndex);
            sums[engine] = sums.GetValueOrDefault(engine) + item.Value;
        }

        if (!foundSelectedEngine)
        {
            return new GpuUnavailableSample(selected, UnavailabilityReason.Uncorrelated);
        }

        if (!foundValidSelectedEngine)
        {
            return new SampleUnavailable(UnavailabilityReason.NotYetSampled);
        }

        return new MeasuredGpuSample(selected, Math.Min(100, sums.Values.Max()));
    }
}
