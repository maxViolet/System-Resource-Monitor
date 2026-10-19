using SystemResourceMonitor.Core.Metrics;
using SystemResourceMonitor.Desktop.Adapters.Metrics;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class GpuMetricSourceTests
{
    private static readonly GpuAdapterCandidate Candidate = new("GPU", new AdapterLuid(10, 20), true, false);

    [Fact]
    public void Source_reuses_adapter_list_and_maps_selection_and_counter_failures()
    {
        var adapters = new FakeAdapters([Candidate]);
        var counters = new FakeCounters { Success = false };
        using var source = new GpuLoadSource(adapters, counters);

        Assert.Equal(UnavailabilityReason.ReadFailed, Assert.IsType<GpuUnavailableSample>(source.Read()).Reason);
        Assert.Equal(UnavailabilityReason.ReadFailed, Assert.IsType<GpuUnavailableSample>(source.Read()).Reason);
        Assert.Equal(1, adapters.Enumerations);
        Assert.Equal(2, counters.Collections);

        adapters.Stale = true;
        _ = source.Read();
        Assert.Equal(2, adapters.Enumerations);

        var noDiscrete = new GpuLoadSource(new FakeAdapters([new("iGPU", default, true, true)]), new FakeCounters());
        Assert.Equal(UnavailabilityReason.NoDiscreteAdapter, Assert.IsType<GpuUnavailableSample>(noDiscrete.Read()).Reason);
        noDiscrete.Dispose();
    }

    [Fact]
    public void Source_changes_all_skipped_samples_from_not_yet_sampled_to_invalid()
    {
        var adapters = new FakeAdapters([Candidate]);
        var source = new GpuLoadSource(adapters, new FakeCounters
        {
            Items = [new("pid_1_luid_0x00000014_0x0000000a_phys_0_eng_1_engtype_3D", 0, false)],
        });

        Assert.Equal(UnavailabilityReason.NotYetSampled, Assert.IsType<GpuUnavailableSample>(source.Read()).Reason);
        Assert.Equal(UnavailabilityReason.Invalid, Assert.IsType<GpuUnavailableSample>(source.Read()).Reason);
        adapters.Stale = true;
        Assert.Equal(UnavailabilityReason.NotYetSampled, Assert.IsType<GpuUnavailableSample>(source.Read()).Reason);
        source.Dispose();
    }

    [Fact]
    public void Diagnostics_include_other_adapter_instances_only_when_requested()
    {
        var items = new GpuCounterItem[]
        {
            new("pid_3_luid_0x00000000_0x00000001_phys_0_eng_1_engtype_", 0, true),
            new("pid_1_luid_0x00000014_0x0000000a_phys_0_eng_1_engtype_3D", 42, true),
            new("pid_2_luid_0x00000014_0x0000000b_phys_0_eng_1_engtype_3D", 20, true),
        };
        using var production = new GpuLoadSource(new FakeAdapters([Candidate]), new FakeCounters { Items = items });
        _ = production.Read();
        Assert.Null(production.LastDiagnostic);

        using var diagnostic = new GpuLoadSource(new FakeAdapters([Candidate]), new FakeCounters { Items = items }, captureDiagnostics: true);
        Assert.IsType<MeasuredGpuSample>(diagnostic.Read());
        Assert.Equal(3, Assert.IsType<GpuReadDiagnostic>(diagnostic.LastDiagnostic).ObservedInstances.Count);
    }

    [Fact]
    public void Failed_reenumeration_retries_even_when_the_replacement_list_is_not_stale()
    {
        var adapters = new FakeAdapters([Candidate]);
        using var source = new GpuLoadSource(adapters, new FakeCounters());
        Assert.IsType<MeasuredGpuSample>(source.Read());

        adapters.Stale = true;
        adapters.ThrowOnEnumerate = true;
        Assert.Equal(UnavailabilityReason.ReadFailed, Assert.IsType<GpuUnavailableSample>(source.Read()).Reason);

        adapters.Stale = false;
        adapters.ThrowOnEnumerate = false;
        Assert.IsType<MeasuredGpuSample>(source.Read());
        Assert.Equal(3, adapters.Enumerations);
    }

    [Fact]
    public void Source_contains_dxcore_failure_and_ambiguous_selection()
    {
        using var failed = new GpuLoadSource(new FakeAdapters([]) { ThrowOnEnumerate = true }, new FakeCounters());
        Assert.Equal(UnavailabilityReason.ReadFailed, Assert.IsType<SampleUnavailable>(failed.Read()).Reason);

        var counters = new FakeCounters();
        using var ambiguous = new GpuLoadSource(new FakeAdapters([Candidate, Candidate]), counters);
        Assert.Equal(UnavailabilityReason.AmbiguousAdapter, Assert.IsType<GpuUnavailableSample>(ambiguous.Read()).Reason);
        Assert.Equal(0, counters.Collections);
    }

    [Fact]
    public async Task Dispose_during_read_leaves_counter_for_process_exit()
    {
        var counters = new FakeCounters { Block = new ManualResetEventSlim(false) };
        var source = new GpuLoadSource(new FakeAdapters([Candidate]), counters);
        var read = Task.Run(source.Read);
        Assert.True(counters.Entered.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        source.Dispose();
        Assert.Equal(0, counters.Disposals);
        counters.Block.Set();
        _ = await read.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        Assert.Equal(0, counters.Disposals);
        source.Dispose();
        Assert.Equal(0, counters.Disposals);
    }

    private sealed class FakeAdapters(IReadOnlyList<GpuAdapterCandidate> candidates) : IGpuAdapterApi
    {
        public bool Stale { get; set; }
        public bool ThrowOnEnumerate { get; set; }
        public int Enumerations { get; private set; }
        public IReadOnlyList<GpuAdapterCandidate> Enumerate()
        {
            Enumerations++;
            return ThrowOnEnumerate ? throw new InvalidOperationException("DXCore failed.") : candidates;
        }
        public bool IsStale() => Stale;
        public void Dispose() { }
    }

    private sealed class FakeCounters : IGpuCounterApi
    {
        public bool Success { get; set; } = true;
        public IReadOnlyList<GpuCounterItem> Items { get; set; } = [new("pid_1_luid_0x00000014_0x0000000a_phys_0_eng_1_engtype_3D", 42, true)];
        public ManualResetEventSlim? Block { get; init; }
        public ManualResetEventSlim Entered { get; } = new(false);
        public int Collections { get; private set; }
        public int Disposals { get; private set; }
        public bool TryCollect(out IReadOnlyList<GpuCounterItem> items, out int status)
        {
            Collections++;
            Entered.Set();
            Block?.Wait();
            items = Items;
            status = Success ? 0 : 1;
            return Success;
        }
        public void Dispose() => Disposals++;
    }
}
