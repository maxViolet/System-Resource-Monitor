namespace SystemResourceMonitor.Desktop.Lifecycle;

/// <summary>
/// Carries a monitor's <c>Changed</c> states (<see cref="Core.Metrics.MetricMonitor"/> or
/// <see cref="AIResources.Core.Codex.CodexLimitMonitor"/>) to the UI thread. <see cref="OnChanged"/> runs inside the
/// monitor's lock, so it only stores the latest state and posts one action while none is pending; it never blocks or
/// touches UI objects. A blocked UI thread therefore builds no queue, and the posted action applies only the newest state.
/// </summary>
internal sealed class MetricRelay<TState>(Action<Action> post, Action<TState> apply)
    where TState : class
{
    private TState? _latest;
    private int _pending;

    public void OnChanged(TState state)
    {
        Interlocked.Exchange(ref _latest, state);
        if (Interlocked.CompareExchange(ref _pending, 1, 0) == 0)
        {
            post(ApplyLatest);
        }
    }

    private void ApplyLatest()
    {
        // Clear the flag first: a change after this point posts again instead of being lost.
        Volatile.Write(ref _pending, 0);
        if (Interlocked.Exchange(ref _latest, null) is { } state)
        {
            apply(state);
        }
    }
}
