namespace SystemResourceMonitor.AIResources.Core.Codex;

/// <summary>
/// Polls the Codex source once per <see cref="PollInterval"/> and owns the current <see cref="CodexLimitState"/>.
/// </summary>
/// <remarks>
/// It runs independently of the system-metric monitor: its polls, failures, and expiry share no thread, lock, or timer
/// with CPU, RAM, or GPU. Polls never overlap, because the source completes only after its client exits. Sign-in,
/// version, format, trust, timeout, and start failures are unavailable on the first failed poll. A network failure
/// keeps the last successful snapshot as stale until <see cref="StaleLimit"/> after that success; a separate timer
/// ends every snapshot at that age without waiting for another poll. One lock serializes every state transition, and
/// <see cref="Changed"/> is raised inside it.
/// </remarks>
public sealed class CodexLimitMonitor
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan StaleLimit = TimeSpan.FromMinutes(3);

    private readonly Lock _gate = new();
    private readonly ICodexLimitSource _source;
    private readonly TimeProvider _time;
    private readonly ITimer _expiryTimer;
    private readonly TaskCompletionSource _expiryFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CodexLimitState _current = CodexLimitsUnavailable.Initial;
    private CancellationTokenSource? _lifetimeSource;
    private CancellationToken _lifetime;
    private bool _started;
    private bool _stopped;

    public CodexLimitMonitor(ICodexLimitSource source, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);
        _source = source;
        _time = time;
        _expiryTimer = time.CreateTimer(_ => Expire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Raised once for each accepted poll result and each availability transition, including expiry, with the new
    /// state. Handlers run inside the monitor's lock on a worker or timer thread: they must return quickly, must not
    /// block, and must not throw. A handler exception stops polling and faults <see cref="RunAsync"/>.
    /// </summary>
    public event EventHandler<CodexLimitState>? Changed;

    public CodexLimitState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Polls until <paramref name="cancellationToken"/> is cancelled, then completes without throwing once the pending
    /// poll has ended. After cancellation no poll starts, a late result is discarded, and no expiry is published. Any
    /// other failure faults the returned task. The monitor runs once; a second call throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_started)
            {
                throw new InvalidOperationException("The Codex limit monitor runs only once.");
            }

            _started = true;
        }

        return RunCoreAsync(cancellationToken);
    }

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            _lifetimeSource = lifetime;
            _lifetime = lifetime.Token;
        }

        try
        {
            using var stop = lifetime.Token.Register(Stop);
            var loop = PollAsync(lifetime.Token);
            var first = await Task.WhenAny(loop, _expiryFailure.Task).ConfigureAwait(false);
            if (first.IsFaulted)
            {
                await lifetime.CancelAsync().ConfigureAwait(false);
            }

            // Awaits the pending poll, so the run ends only after its client has exited or reached its deadline.
            await loop.ConfigureAwait(false);
            if (_expiryFailure.Task.IsFaulted)
            {
                await _expiryFailure.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            Stop();
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var origin = _time.GetTimestamp();
        var due = TimeSpan.Zero;
        while (!cancellationToken.IsCancellationRequested)
        {
            CodexReadResult result;
            try
            {
                // The thread pool keeps the source's synchronous resolution work off the caller's thread.
                result = await Task.Run(() => _source.ReadAsync(cancellationToken), CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                result = new CodexReadFailed(CodexUnavailableReason.ReadFailed);
            }

            Apply(result, _time.GetUtcNow(), _time.GetTimestamp());

            due += PollInterval;
            var elapsed = _time.GetElapsedTime(origin);
            if (elapsed >= due)
            {
                // The poll overran its slot: start the next one now and skip the missed ticks instead of bursting.
                due = elapsed;
                continue;
            }

            try
            {
                await Task.Delay(due - elapsed, _time, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>Applies one completed poll stamped at <paramref name="measuredAt"/> and <paramref name="timestamp"/>.</summary>
    internal void Apply(CodexReadResult result, DateTimeOffset measuredAt, long timestamp)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            if (IsStopped)
            {
                return;
            }

            switch (result)
            {
                case CodexReadSucceeded succeeded:
                    // Arm the deadline before subscribers observe the snapshot.
                    _expiryTimer.Change(StaleLimit, Timeout.InfiniteTimeSpan);
                    Publish(new CodexLimitsCurrent(succeeded.Snapshot, measuredAt, timestamp));
                    break;
                case CodexReadFailed { Reason: CodexUnavailableReason.NetworkFailed }:
                    switch (_current)
                    {
                        case CodexLimitsCurrent current when RemainingAge(current.Timestamp) > TimeSpan.Zero:
                            // The expiry timer keeps the deadline of the last success.
                            Publish(new CodexLimitsStale(current.Snapshot, current.MeasuredAt, current.Timestamp));
                            break;
                        case CodexLimitsStale stale when RemainingAge(stale.Timestamp) > TimeSpan.Zero:
                            break;
                        default:
                            PublishUnavailable(CodexUnavailableReason.NetworkFailed);
                            break;
                    }

                    break;
                case CodexReadFailed failed:
                    PublishUnavailable(failed.Reason);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown Codex read result.");
            }
        }
    }

    private void Expire()
    {
        lock (_gate)
        {
            if (IsStopped)
            {
                return;
            }

            // A current snapshot expires; a stale one ends as the network failure that made it stale.
            long timestamp;
            CodexUnavailableReason reason;
            switch (_current)
            {
                case CodexLimitsCurrent current:
                    (timestamp, reason) = (current.Timestamp, CodexUnavailableReason.Expired);
                    break;
                case CodexLimitsStale stale:
                    (timestamp, reason) = (stale.Timestamp, CodexUnavailableReason.NetworkFailed);
                    break;
                default:
                    return;
            }

            var remaining = RemainingAge(timestamp);
            if (remaining > TimeSpan.Zero)
            {
                // The timer fired before this snapshot's deadline.
                _expiryTimer.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            try
            {
                PublishUnavailable(reason);
            }
            catch (Exception exception)
            {
                // A timer callback must not throw: under TimeProvider.System that would end the process.
                _expiryFailure.TrySetException(exception);
                _lifetimeSource?.Cancel();
            }
        }
    }

    private void PublishUnavailable(CodexUnavailableReason reason)
    {
        _expiryTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        if (_current is not CodexLimitsUnavailable current || current.Reason != reason)
        {
            Publish(new CodexLimitsUnavailable(reason));
        }
    }

    private void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            _expiryTimer.Dispose();
        }
    }

    private bool IsStopped => _stopped || _lifetime.IsCancellationRequested;

    private TimeSpan RemainingAge(long timestamp)
    {
        var remaining = StaleLimit - _time.GetElapsedTime(timestamp);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void Publish(CodexLimitState state)
    {
        _current = state;
        Changed?.Invoke(this, state);
    }
}
