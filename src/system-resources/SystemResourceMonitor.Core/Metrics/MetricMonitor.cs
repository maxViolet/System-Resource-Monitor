namespace SystemResourceMonitor.Core.Metrics;

/// <summary>
/// Polls the CPU and RAM sources independently and owns the current <see cref="DashboardState"/>.
/// </summary>
/// <remarks>
/// Each source is read every <see cref="PollInterval"/> on a thread-pool worker, never with two reads of one source
/// pending. Each reading expires on its own timer at monotonic age <see cref="ExpiryAge"/>, even while its read is
/// blocked. A failed read never renews a reading. One lock serializes every state transition, and <see cref="Changed"/>
/// is raised inside it, so subscribers observe states in order.
/// </remarks>
public sealed class MetricMonitor
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ExpiryAge = TimeSpan.FromSeconds(6);

    private readonly Lock _gate = new();
    private readonly IMetricSource _cpu;
    private readonly IMetricSource _ram;
    private readonly TimeProvider _time;
    private readonly ITimer _cpuExpiry;
    private readonly ITimer _ramExpiry;
    private DashboardState _current = DashboardState.Initial;
    private readonly TaskCompletionSource _expiryFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _lifetimeSource;
    private CancellationToken _lifetime;
    private bool _started;
    private bool _stopped;

    public MetricMonitor(IMetricSource cpu, IMetricSource ram, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(cpu);
        ArgumentNullException.ThrowIfNull(ram);
        ArgumentNullException.ThrowIfNull(time);
        if (cpu.Kind != MetricKind.Cpu)
        {
            throw new ArgumentException("The CPU source must report MetricKind.Cpu.", nameof(cpu));
        }

        if (ram.Kind != MetricKind.Ram)
        {
            throw new ArgumentException("The RAM source must report MetricKind.Ram.", nameof(ram));
        }

        _cpu = cpu;
        _ram = ram;
        _time = time;
        _cpuExpiry = time.CreateTimer(Expire, MetricKind.Cpu, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _ramExpiry = time.CreateTimer(Expire, MetricKind.Ram, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Raised once for each accepted reading and each availability transition, including expiry, with the new state.
    /// Handlers run inside the monitor's lock on a worker or timer thread: they must return quickly, must not block,
    /// and must not throw. A handler exception on either thread stops both loops and faults <see cref="RunAsync"/>;
    /// it never escapes a timer callback. The Desktop handler only posts the state to the dispatcher.
    /// </summary>
    public event EventHandler<DashboardState>? Changed;

    public DashboardState Current
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
    /// Polls both sources until <paramref name="cancellationToken"/> is cancelled, then completes without throwing.
    /// After cancellation no read starts, a pending read's late result is discarded, and no expiry is published; a
    /// pending synchronous read is not interrupted. Any other failure stops both loops and faults the returned task.
    /// The monitor runs once; a second call throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_started)
            {
                throw new InvalidOperationException("The metric monitor runs only once.");
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
            Task[] loops = [PollAsync(_cpu, lifetime.Token), PollAsync(_ram, lifetime.Token)];
            var first = await Task.WhenAny([.. loops, _expiryFailure.Task]).ConfigureAwait(false);
            if (first.IsFaulted)
            {
                await lifetime.CancelAsync().ConfigureAwait(false);
            }

            await Task.WhenAll(loops).ConfigureAwait(false);
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

    private async Task PollAsync(IMetricSource source, CancellationToken cancellationToken)
    {
        var origin = _time.GetTimestamp();
        var due = TimeSpan.Zero;
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = Task.Run(() => Collect(source), CancellationToken.None);
            (MetricSample Sample, DateTimeOffset MeasuredAt, long Timestamp) completed;
            try
            {
                completed = await read.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            Apply(source.Kind, completed.Sample, completed.MeasuredAt, completed.Timestamp);

            due += PollInterval;
            var elapsed = _time.GetElapsedTime(origin);
            if (elapsed >= due)
            {
                // The read overran its slot: start the next one now and skip the missed ticks instead of bursting.
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

    private (MetricSample Sample, DateTimeOffset MeasuredAt, long Timestamp) Collect(IMetricSource source)
    {
        MetricSample sample;
        try
        {
            sample = source.Read();
        }
        catch (Exception)
        {
            sample = new SampleUnavailable(UnavailabilityReason.ReadFailed);
        }

        return (sample, _time.GetUtcNow(), _time.GetTimestamp());
    }

    /// <summary>
    /// Applies one completed read of <paramref name="kind"/> stamped at <paramref name="measuredAt"/> and
    /// <paramref name="timestamp"/>.
    /// </summary>
    internal void Apply(MetricKind kind, MetricSample? sample, DateTimeOffset measuredAt, long timestamp)
    {
        var reading = AvailableMetric.Create(kind, sample, measuredAt, timestamp);
        lock (_gate)
        {
            if (IsStopped)
            {
                return;
            }

            var previous = _current.Get(kind);
            switch (reading)
            {
                case AvailableMetric available:
                    // Arm the new deadline before subscribers observe the reading. A reading already at the age
                    // limit when applied expires at once instead of through a zero-delay timer.
                    var remaining = RemainingAge(available);
                    ExpiryTimer(kind).Change(remaining > TimeSpan.Zero ? remaining : Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    Publish(available);
                    if (remaining <= TimeSpan.Zero)
                    {
                        Publish(new UnavailableMetric(kind, UnavailabilityReason.Expired));
                    }

                    break;
                case UnavailableMetric { Reason: UnavailabilityReason.Invalid }:
                    ExpiryTimer(kind).Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                    if (previous is not UnavailableMetric { Reason: UnavailabilityReason.Invalid })
                    {
                        Publish(reading);
                    }

                    break;
                case UnavailableMetric unavailable:
                    // A failed or unsampled read keeps an available reading and its original deadline.
                    if (previous is UnavailableMetric current && current.Reason != unavailable.Reason)
                    {
                        Publish(reading);
                    }

                    break;
            }
        }
    }

    private void Expire(object? state)
    {
        var kind = (MetricKind)state!;
        lock (_gate)
        {
            if (IsStopped || _current.Get(kind) is not AvailableMetric available)
            {
                return;
            }

            var remaining = RemainingAge(available);
            if (remaining > TimeSpan.Zero)
            {
                // The timer belonged to an earlier reading or fired before this reading's deadline.
                ExpiryTimer(kind).Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            try
            {
                Publish(new UnavailableMetric(kind, UnavailabilityReason.Expired));
            }
            catch (Exception exception)
            {
                // A timer callback must not throw: under TimeProvider.System that would end the process. Fault the run
                // instead, as a handler failure on a polling loop does. Cancel runs Stop, so no later change publishes.
                _expiryFailure.TrySetException(exception);
                _lifetimeSource?.Cancel();
            }
        }
    }

    private void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            _cpuExpiry.Dispose();
            _ramExpiry.Dispose();
        }
    }

    private bool IsStopped => _stopped || _lifetime.IsCancellationRequested;

    private TimeSpan RemainingAge(AvailableMetric reading)
    {
        var remaining = ExpiryAge - _time.GetElapsedTime(reading.Timestamp);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private ITimer ExpiryTimer(MetricKind kind) => kind == MetricKind.Cpu ? _cpuExpiry : _ramExpiry;

    private void Publish(MetricReading reading)
    {
        _current = _current.With(reading);
        Changed?.Invoke(this, _current);
    }
}
