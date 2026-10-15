namespace SystemResourceMonitor.Desktop.Lifecycle;

/// <summary>
/// The bar operations shutdown needs while its window still exists.
/// </summary>
public interface IShutdownBar
{
    /// <summary>
    /// Releases the AppBar reservation; does nothing when the bar is not registered.
    /// Throws <see cref="InvalidOperationException"/> when Windows rejects the removal.
    /// </summary>
    void RemoveAppBar();

    /// <summary>Removes the tray icon idempotently after AppBar removal.</summary>
    void RemoveTrayIcon();

    /// <summary>Closes the window without requesting shutdown again.</summary>
    void CloseFinal();
}

/// <summary>
/// The single, idempotent exit path for normal exit, session end, and handled failures.
/// Call it on the UI thread only.
/// </summary>
/// <remarks>
/// Order: stop publication, cancel future work, remove the AppBar in a <c>finally</c> path, close the bar,
/// wait at most <see cref="WaitDeadline"/> for tracked tasks, then exit. Work still pending at the deadline
/// is recorded and left running; its late result cannot publish.
/// </remarks>
public sealed class ShutdownCoordinator(
    CancellationTokenSource lifetime,
    TimeProvider time,
    Action<int> exit,
    Action<string>? record = null)
{
    public static readonly TimeSpan WaitDeadline = TimeSpan.FromSeconds(2);

    private readonly List<(string Operation, Task Observed)> _tracked = [];
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IShutdownBar? _bar;
    private bool _stopping;

    /// <summary>Cancelled when shutdown starts; owners run their work until then.</summary>
    public CancellationToken Lifetime => lifetime.Token;

    public void Attach(IShutdownBar bar) => _bar = bar;

    /// <summary>
    /// Tracks work an owner depends on for the app's lifetime. If it faults, the fault is recorded and failure
    /// shutdown starts on the caller's synchronization context; completion or cancellation is not a failure.
    /// Call it on the UI thread.
    /// </summary>
    public void TrackOwner(string operation, Task task)
    {
        Track(operation, task);
        _ = ObserveOwnerAsync(operation, task);
    }

    /// <summary>
    /// Adds work to the bounded shutdown wait. Its completion or fault is observed whenever it happens.
    /// </summary>
    public void Track(string operation, Task task)
    {
        var observed = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _tracked.Add((operation, observed));
    }

    /// <summary>
    /// Runs <paramref name="publish"/> only while shutdown has not started.
    /// </summary>
    public bool TryPublish(Action publish)
    {
        if (_stopping)
        {
            return false;
        }

        publish();
        return true;
    }

    /// <summary>
    /// Starts shutdown once; later calls return the same completion. The AppBar is removed before this returns.
    /// </summary>
    public Task ShutdownAsync(int exitCode)
    {
        if (!_stopping)
        {
            _stopping = true;
            _ = RunAsync(exitCode);
        }

        return _completed.Task;
    }

    private async Task RunAsync(int exitCode)
    {
        try
        {
            try
            {
                lifetime.Cancel();
            }
            catch (AggregateException exception)
            {
                exitCode = Failed(exception);
            }
            finally
            {
                if (_bar is not null)
                {
                    try
                    {
                        _bar.RemoveAppBar();
                        record?.Invoke("remove");
                    }
                    catch (InvalidOperationException exception)
                    {
                        exitCode = Failed(exception);
                    }
                    finally
                    {
                        try
                        {
                            _bar.RemoveTrayIcon();
                            record?.Invoke("tray-remove");
                        }
                        catch (InvalidOperationException exception)
                        {
                            exitCode = Failed(exception);
                        }
                    }
                }
            }

            try
            {
                _bar?.CloseFinal();
            }
            catch (InvalidOperationException exception)
            {
                exitCode = Failed(exception);
            }

            record?.Invoke("wait-start");
            await WaitForTrackedAsync();
        }
        finally
        {
            record?.Invoke("exit");
            exit(exitCode);
            _completed.SetResult();
        }
    }

    private async Task ObserveOwnerAsync(string operation, Task task)
    {
        try
        {
            // Resumes on the captured UI context, where shutdown must run.
            await task;
        }
        catch (OperationCanceledException) when (task.IsCanceled)
        {
        }
        catch (Exception exception)
        {
            record?.Invoke($"owner-failed:{operation}:{exception.GetType().Name}");
            _ = ShutdownAsync(ExitCodes.Failure);
        }
    }

    private int Failed(Exception exception)
    {
        record?.Invoke($"shutdown-failed:{exception.GetType().Name}");
        return ExitCodes.Failure;
    }

    private async Task WaitForTrackedAsync()
    {
        var pending = _tracked.Where(tracked => !tracked.Observed.IsCompleted).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        try
        {
            // Observed continuations never fault, so a timeout is the only exception.
            await Task.WhenAll(pending.Select(tracked => tracked.Observed)).WaitAsync(WaitDeadline, time);
        }
        catch (TimeoutException)
        {
            foreach (var (operation, observed) in pending)
            {
                if (!observed.IsCompleted)
                {
                    record?.Invoke($"pending:{operation}");
                }
            }
        }
    }
}
