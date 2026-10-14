namespace SystemResourceMonitor.Desktop.Lifecycle;

/// <summary>
/// Ownership of a per-session named mutex. Acquire and dispose it on the same thread.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// Takes ownership without waiting; returns <see langword="null"/> when another process owns the mutex
    /// or it cannot be opened. An abandoned mutex counts as acquired.
    /// </summary>
    public static SingleInstanceGuard? TryAcquire(string name)
    {
        Mutex mutex;
        try
        {
            mutex = new Mutex(initiallyOwned: false, name);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.IO.IOException or WaitHandleCannotBeOpenedException)
        {
            return null;
        }

        try
        {
            if (mutex.WaitOne(0))
            {
                return new SingleInstanceGuard(mutex);
            }
        }
        catch (AbandonedMutexException)
        {
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
