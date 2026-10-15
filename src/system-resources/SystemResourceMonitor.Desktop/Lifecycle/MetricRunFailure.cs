namespace SystemResourceMonitor.Desktop.Lifecycle;

/// <summary>
/// Watches the metric monitor's run. A faulted run has stopped polling and expiry, so the bar would keep showing its
/// last values as fresh; the fault is therefore an owner failure that enters failure shutdown. Cancellation and normal
/// completion are not failures.
/// </summary>
internal static class MetricRunFailure
{
    /// <summary>Posts <paramref name="fail"/> with the fault's base exception when <paramref name="run"/> faults.</summary>
    public static void Observe(Task run, Action<Action> post, Action<Exception> fail)
    {
        ArgumentNullException.ThrowIfNull(run);
        _ = run.ContinueWith(
            faulted => post(() => fail(faulted.Exception!.GetBaseException())),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
