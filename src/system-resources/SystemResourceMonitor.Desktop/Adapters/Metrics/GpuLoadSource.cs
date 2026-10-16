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

internal readonly record struct GpuObservedEngine(GpuEngineInstance Instance, double Value);
internal sealed record GpuReadDiagnostic(GpuAdapter? Adapter, IReadOnlyList<GpuObservedEngine> ObservedInstances, int UnparseableCount);

internal sealed class GpuLoadSource(IGpuAdapterApi adapters, IGpuCounterApi counters, bool captureDiagnostics = false) : IMetricSource, IDisposable
{
    private bool _hasAdapterList;
    private bool _hasCollectedBeforeLastResult;
    private GpuAdapterSelection? _selection;
    // 0 = active, 1 = reading, 2 = disposed, 3 = disposed while reading; leave handles for process exit.
    private int _state;

    internal GpuReadDiagnostic? LastDiagnostic { get; private set; }
    public MetricKind Kind => MetricKind.Gpu;

    public MetricSample Read()
    {
        LastDiagnostic = null;
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            return Record(ReadFailed(_selection?.Adapter));
        }

        try
        {
            if (!_hasAdapterList || adapters.IsStale())
            {
                _hasAdapterList = false;
                _hasCollectedBeforeLastResult = false;
                _selection = GpuAdapterSelector.Select(adapters.Enumerate());
                _hasAdapterList = true;
            }

            var selected = _selection!.Adapter;
            if (selected is null)
            {
                return Record(new GpuUnavailableSample(null, _selection.Reason!.Value));
            }

            if (!counters.TryCollect(out var items, out _))
            {
                return Record(ReadFailed(selected));
            }

            var sample = GpuLoadCalculator.Calculate(selected, items);
            if (sample is SampleUnavailable { Reason: UnavailabilityReason.NotYetSampled })
            {
                sample = new GpuUnavailableSample(selected,
                    _hasCollectedBeforeLastResult ? UnavailabilityReason.Invalid : UnavailabilityReason.NotYetSampled);
            }

            _hasCollectedBeforeLastResult = true;
            return Record(sample, items);
        }
        catch (Exception)
        {
            return Record(ReadFailed(_selection?.Adapter));
        }
        finally
        {
            _ = Interlocked.CompareExchange(ref _state, 0, 1);
        }
    }

    private MetricSample Record(MetricSample sample, IReadOnlyList<GpuCounterItem>? items = null)
    {
        if (!captureDiagnostics)
        {
            return sample;
        }

        var observed = new List<GpuObservedEngine>();
        var unparseable = 0;
        if (items is not null)
        {
            foreach (var item in items)
            {
                if (!GpuEngineInstance.TryParse(item.InstanceName, out var instance))
                {
                    unparseable++;
                }
                else if (item.IsValid && instance.Engine != GpuEngineType.Other)
                {
                    observed.Add(new GpuObservedEngine(instance, item.Value));
                }
            }
        }

        LastDiagnostic = new GpuReadDiagnostic(_selection?.Adapter, observed, unparseable);
        return sample;
    }

    private static MetricSample ReadFailed(GpuAdapter? adapter) => adapter is null
        ? new SampleUnavailable(UnavailabilityReason.ReadFailed)
        : new GpuUnavailableSample(adapter, UnavailabilityReason.ReadFailed);

    public void Dispose()
    {
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if (state is 2 or 3)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _state, state == 0 ? 2 : 3, state) != state)
            {
                continue;
            }

            if (state == 0)
            {
                counters.Dispose();
                adapters.Dispose();
            }

            return;
        }
    }
}
