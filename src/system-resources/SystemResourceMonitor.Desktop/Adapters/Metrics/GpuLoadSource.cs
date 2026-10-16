using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Desktop.Adapters.Metrics;

internal interface IGpuAdapterApi : IDisposable
{
    IReadOnlyList<GpuAdapterCandidate> Enumerate();
    bool IsStale();
}

internal interface IGpuCounterApi : IDisposable
{
    bool TryCollect(out IReadOnlyList<GpuCounterItem> items, out int status);
}

internal sealed record GpuReadDiagnostic(GpuAdapter? Adapter, MetricSample Sample, IReadOnlyList<GpuEngineInstance> CountedInstances, int UnparseableCount);

internal sealed class GpuLoadSource(IGpuAdapterApi adapters, IGpuCounterApi counters) : IMetricSource, IDisposable
{
    private readonly object _gate = new();
    private bool _hasAdapterList;
    private GpuAdapterSelection? _selection;
    internal GpuReadDiagnostic? LastDiagnostic { get; private set; }
    // 0 = active, 1 = read in progress, 2 = disposed, 3 = disposed while a read was in progress.
    private int _state;

    public MetricKind Kind => MetricKind.Gpu;

    public MetricSample Read()
    {
        LastDiagnostic = null;
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            var disposed = ReadFailed(_selection?.Adapter);
            LastDiagnostic = new GpuReadDiagnostic(_selection?.Adapter, disposed, [], 0);
            return disposed;
        }

        try
        {
            GpuAdapterSelection selection;
            try
            {
                lock (_gate)
                {
                    if (!_hasAdapterList || adapters.IsStale())
                    {
                        _selection = GpuAdapterSelector.Select(adapters.Enumerate());
                        _hasAdapterList = true;
                    }

                    selection = _selection!;
                }
            }
            catch (Exception)
            {
                var failed = ReadFailed(_selection?.Adapter);
                LastDiagnostic = new GpuReadDiagnostic(_selection?.Adapter, failed, [], 0);
                return failed;
            }

            if (selection.Adapter is null)
            {
                var unavailable = new GpuUnavailableSample(null, selection.Reason!.Value);
                LastDiagnostic = new GpuReadDiagnostic(null, unavailable, [], 0);
                return unavailable;
            }

            IReadOnlyList<GpuCounterItem> items;
            try
            {
                if (!counters.TryCollect(out items, out _))
                {
                    var failed = ReadFailed(selection.Adapter);
                    LastDiagnostic = new GpuReadDiagnostic(selection.Adapter, failed, [], 0);
                    return failed;
                }
            }
            catch (Exception)
            {
                var failed = ReadFailed(selection.Adapter);
                LastDiagnostic = new GpuReadDiagnostic(selection.Adapter, failed, [], 0);
                return failed;
            }

            try
            {
                var result = GpuLoadCalculator.Calculate(selection.Adapter, items);
                if (result is SampleUnavailable { Reason: UnavailabilityReason.NotYetSampled } && _hasCollectedBeforeLastResult)
                {
                    result = new GpuUnavailableSample(selection.Adapter, UnavailabilityReason.Invalid);
                }

                _hasCollectedBeforeLastResult = true;
                var final = result is SampleUnavailable { Reason: UnavailabilityReason.NotYetSampled }
                    ? new GpuUnavailableSample(selection.Adapter, UnavailabilityReason.NotYetSampled)
                    : result;
                var parsed = items.Select(item => (Item: item, Instance: GpuEngineInstance.TryParse(item.InstanceName, out var instance)
                    ? instance : (GpuEngineInstance?)null)).ToArray();
                LastDiagnostic = new GpuReadDiagnostic(selection.Adapter, final,
                    parsed.Where(entry => entry.Item.IsValid && entry.Instance is { Engine: not GpuEngineType.Other, PhysicalIndex: 0 } instance
                        && instance.Luid == selection.Adapter.Luid).Select(entry => entry.Instance!.Value).ToArray(),
                    parsed.Count(entry => entry.Instance is null));
                return final;
            }
            catch (Exception)
            {
                var failed = ReadFailed(selection.Adapter);
                LastDiagnostic = new GpuReadDiagnostic(selection.Adapter, failed, [], 0);
                return failed;
            }
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _state, 0, 1) == 3)
            {
                counters.Dispose();
                adapters.Dispose();
                Interlocked.Exchange(ref _state, 2);
            }
        }
    }

    private bool _hasCollectedBeforeLastResult;

    private static MetricSample ReadFailed(GpuAdapter? adapter) => adapter is null
        ? new SampleUnavailable(UnavailabilityReason.ReadFailed)
        : new GpuUnavailableSample(adapter, UnavailabilityReason.ReadFailed);

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
        {
            counters.Dispose();
            adapters.Dispose();
        }
        else
        {
            _ = Interlocked.CompareExchange(ref _state, 3, 1);
        }
    }
}
