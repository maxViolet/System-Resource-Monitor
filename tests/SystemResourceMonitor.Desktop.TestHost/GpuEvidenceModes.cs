using System.Diagnostics;
using System.Globalization;
using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Adapters.Metrics;

namespace SystemResourceMonitor.Desktop.TestHost;

public static partial class Program
{
    private static int GpuProbe()
    {
        using var adapters = new DxCoreAdapterApi();
        using var counters = new PdhGpuCounterApi();
        IReadOnlyList<GpuAdapterCandidate> candidates;
        try
        {
            candidates = adapters.Enumerate();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"FAIL: DXCore enumeration {exception.GetType().Name}");
            return AssertionFailed;
        }

        foreach (var candidate in candidates)
        {
            Console.WriteLine($"adapter description=\"{candidate.Description}\" luid={Luid(candidate.Luid)} hardware={candidate.IsHardware} integrated={candidate.IsIntegrated} detachable={candidate.IsDetachable}");
        }

        var selection = GpuAdapterSelector.Select(candidates);
        Console.WriteLine(selection.Adapter is null
            ? $"selection unavailable={selection.Reason}"
            : $"selection adapter=\"{selection.Adapter.Description}\" luid={Luid(selection.Adapter.Luid)}");

        var previous = new HashSet<string>(StringComparer.Ordinal);
        var initial = new HashSet<string>(StringComparer.Ordinal);
        var selectedLuidSeen = false;
        var newInstanceCount = 0;
        var failedCollections = 0;
        var durationTotal = TimeSpan.Zero;
        var engineTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var collection = 0; collection < 60; collection++)
        {
            if (collection == 30)
            {
                Console.WriteLine("probe checkpoint: start a process that creates GPU engine activity during the next 60 seconds");
            }

            var started = Stopwatch.GetTimestamp();
            var success = counters.TryCollect(out var items, out var status);
            var elapsed = Stopwatch.GetElapsedTime(started);
            durationTotal += elapsed;
            Console.WriteLine($"collection={collection + 1} duration-ms={elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)} status={status} instances={items.Count}");
            if (!success)
            {
                failedCollections++;
            }
            else
            {
                var names = new HashSet<string>(items.Select(item => item.InstanceName), StringComparer.Ordinal);
                var newNames = names.Except(previous, StringComparer.Ordinal).ToArray();
                if (collection < 30) initial.UnionWith(names);
                else newInstanceCount += newNames.Length;
                previous = names;
                foreach (var group in items.Select(item => GpuEngineInstance.TryParse(item.InstanceName, out var parsed) ? parsed : (GpuEngineInstance?)null)
                             .Where(parsed => parsed is not null).Select(parsed => parsed!.Value).GroupBy(parsed => parsed.Luid))
                {
                    var match = candidates.Any(candidate => candidate.Luid == group.Key);
                    Console.WriteLine($"counter-luid={Luid(group.Key)} adapter-match={match} instances={group.Count()}");
                    selectedLuidSeen |= selection.Adapter?.Luid == group.Key;
                }

                foreach (var item in items)
                {
                    var marker = item.InstanceName.LastIndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
                    if (marker >= 0 && marker + 9 < item.InstanceName.Length)
                    {
                        var engine = item.InstanceName[(marker + 9)..];
                        if (engine.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
                        {
                            engineTypes.Add(engine);
                        }
                    }
                }
                var unparseable = items.Count(item => !GpuEngineInstance.TryParse(item.InstanceName, out _));
                if (unparseable > 0) Console.WriteLine($"unparseable-instances={unparseable}");
                if (collection == 1 && unparseable > 0)
                {
                    foreach (var group in items.Where(item => !GpuEngineInstance.TryParse(item.InstanceName, out _))
                                 .GroupBy(item => InstanceShape(item.InstanceName)))
                    {
                        Console.WriteLine($"unparseable-shape={group.Key} count={group.Count()}");
                    }
                }
            }

            if (collection < 59) Thread.Sleep(TimeSpan.FromSeconds(2));
        }

        foreach (var engine in engineTypes.Order(StringComparer.OrdinalIgnoreCase)) Console.WriteLine($"engine-type={engine}");
        var appeared = previous.Except(initial, StringComparer.Ordinal).Count();
        Console.WriteLine($"summary selected-luid-seen={selectedLuidSeen} new-instance-names-after-checkpoint={newInstanceCount} distinct-new-at-end={appeared} failed-collections={failedCollections} average-call-ms={(durationTotal.TotalMilliseconds / 60):F3}");
        return failedCollections == 0 && (selection.Adapter is null || selectedLuidSeen) && newInstanceCount > 0 ? Success : AssertionFailed;
    }

    private static int GpuCorrelation(TimeSpan duration)
    {
        using var source = new GpuLoadSource(new DxCoreAdapterApi(), new PdhGpuCounterApi());
        var clock = Stopwatch.StartNew();
        var reads = 0;
        var failures = 0;
        var consecutiveOverruns = 0;
        var longestOverrunRun = 0;
        var maximumRead = TimeSpan.Zero;
        while (clock.Elapsed < duration)
        {
            var started = Stopwatch.GetTimestamp();
            var sample = source.Read();
            var elapsed = Stopwatch.GetElapsedTime(started);
            maximumRead = elapsed > maximumRead ? elapsed : maximumRead;
            consecutiveOverruns = elapsed > MetricMonitor.PollInterval ? consecutiveOverruns + 1 : 0;
            longestOverrunRun = Math.Max(longestOverrunRun, consecutiveOverruns);
            if (consecutiveOverruns >= 2) failures++;
            var diagnostic = source.LastDiagnostic;
            var selected = diagnostic?.Adapter;
            Console.WriteLine($"read={++reads} duration-ms={elapsed.TotalMilliseconds:F3} adapter={(selected is null ? "none" : $"\"{selected.Description}\" {Luid(selected.Luid)}")} result={sample}");
            if (diagnostic is null || diagnostic.UnparseableCount > 0)
            {
                Console.WriteLine($"FAIL: diagnostic missing or contains {diagnostic?.UnparseableCount ?? -1} unparseable instance names");
                failures++;
            }
            else if (selected is not null)
            {
                foreach (var instance in diagnostic.CountedInstances)
                {
                    Console.WriteLine($"counted-instance luid={Luid(instance.Luid)} physical={instance.PhysicalIndex} engine={instance.Engine} index={instance.EngineIndex}");
                    if (instance.Luid != selected.Luid || instance.PhysicalIndex != 0) failures++;
                }
            }
            else
            {
                Console.WriteLine("correlation unavailable: no unique discrete adapter is selected");
            }

            Thread.Sleep(MetricMonitor.PollInterval);
        }

        Console.WriteLine($"summary reads={reads} correlation-failures={failures} max-read-ms={maximumRead.TotalMilliseconds:F3} consecutive-overrun-run={longestOverrunRun}");
        return reads > 0 && failures == 0 ? Success : AssertionFailed;
    }

    private static string Luid(AdapterLuid luid) => $"{unchecked((uint)luid.HighPart):x8}:{luid.LowPart:x8}";

    private static string InstanceShape(string name) =>
        $"pid={System.Text.RegularExpressions.Regex.IsMatch(name, "^pid_[0-9]+_luid_", System.Text.RegularExpressions.RegexOptions.IgnoreCase)} " +
        $"luid={System.Text.RegularExpressions.Regex.IsMatch(name, "_luid_0x[0-9a-f]{8}_0x[0-9a-f]{8}_phys_", System.Text.RegularExpressions.RegexOptions.IgnoreCase)} " +
        $"phys={System.Text.RegularExpressions.Regex.IsMatch(name, "_phys_[0-9]+_eng_", System.Text.RegularExpressions.RegexOptions.IgnoreCase)} " +
        $"eng={System.Text.RegularExpressions.Regex.IsMatch(name, "_eng_[0-9]+_engtype_", System.Text.RegularExpressions.RegexOptions.IgnoreCase)} " +
        $"engtype={System.Text.RegularExpressions.Regex.IsMatch(name, "_engtype_.+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)} " +
        $"duplicate-suffix={System.Text.RegularExpressions.Regex.IsMatch(name, "#[0-9]+$")}";
}
