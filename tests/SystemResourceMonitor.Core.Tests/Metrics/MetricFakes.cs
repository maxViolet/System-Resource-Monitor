using Microsoft.Extensions.Time.Testing;
using SystemResourceMonitor.Core.Metrics;

namespace SystemResourceMonitor.Core.Tests.Metrics;

internal static class Waits
{
    /// <summary>Real-time safety limit for asynchronous loop progress; fake time drives the behavior itself.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Waits on <paramref name="gate"/>, which the caller holds, until <paramref name="condition"/> is true.</summary>
    public static void Until(object gate, Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero || !Monitor.Wait(gate, remaining) && !condition())
            {
                Assert.Fail($"Timed out waiting for {description}.");
            }
        }
    }
}

/// <summary>A controllable source: scripted results, a gate that blocks reads, and counters for started reads.</summary>
internal sealed class FakeMetricSource(MetricKind kind, MetricSample sample) : IMetricSource
{
    private readonly object _gate = new();
    private Func<MetricSample> _next = () => sample;
    private ManualResetEventSlim? _block;
    private int _inFlight;
    private int _reads;
    private int _returned;
    private int _maxInFlight;

    public MetricKind Kind => kind;

    public int Reads
    {
        get
        {
            lock (_gate)
            {
                return _reads;
            }
        }
    }

    public int MaxInFlight
    {
        get
        {
            lock (_gate)
            {
                return _maxInFlight;
            }
        }
    }

    public void Returns(MetricSample next)
    {
        lock (_gate)
        {
            _next = () => next;
        }
    }

    public void Throws()
    {
        lock (_gate)
        {
            _next = () => throw new InvalidOperationException("The fake read failed.");
        }
    }

    /// <summary>Makes every later read wait until <see cref="Release"/>.</summary>
    public void Block()
    {
        lock (_gate)
        {
            _block = new ManualResetEventSlim(false);
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _block?.Set();
        }
    }

    public MetricSample Read()
    {
        ManualResetEventSlim? block;
        Func<MetricSample> next;
        lock (_gate)
        {
            _reads++;
            _maxInFlight = Math.Max(_maxInFlight, ++_inFlight);
            block = _block;
            next = _next;
            Monitor.PulseAll(_gate);
        }

        try
        {
            block?.Wait();
            return next();
        }
        finally
        {
            lock (_gate)
            {
                _inFlight--;
                _returned++;
                Monitor.PulseAll(_gate);
            }
        }
    }

    public void WaitForReads(int count)
    {
        lock (_gate)
        {
            Waits.Until(_gate, () => _reads >= count, $"{kind} read {count} to start");
        }
    }

    public void WaitForReturns(int count)
    {
        lock (_gate)
        {
            Waits.Until(_gate, () => _returned >= count, $"{kind} read {count} to return");
        }
    }
}

/// <summary>Records each <see cref="MetricMonitor.Changed"/> notification as the one reading it changed.</summary>
internal sealed class PublicationRecorder
{
    private readonly object _gate = new();
    private readonly List<MetricReading> _publications = [];
    private DashboardState _last = DashboardState.Initial;

    public PublicationRecorder(MetricMonitor monitor) => monitor.Changed += (_, state) =>
    {
        lock (_gate)
        {
            var changed = new[] { state.Cpu, state.Ram, state.Gpu }.Where(reading => !ReferenceEquals(reading, _last.Get(reading.Kind))).ToList();
            Assert.Single(changed);
            _publications.Add(changed[0]);
            _last = state;
            Monitor.PulseAll(_gate);
        }
    };

    public IReadOnlyList<MetricReading> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _publications];
            }
        }
    }

    public IReadOnlyList<MetricReading> Of(MetricKind kind) => All.Where(reading => reading.Kind == kind).ToList();

    public void WaitFor(MetricKind kind, int count)
    {
        lock (_gate)
        {
            Waits.Until(_gate, () => _publications.Count(reading => reading.Kind == kind) >= count, $"{count} {kind} publications");
        }
    }

    public void WaitFor(MetricKind kind, Func<MetricReading, bool> match, string description)
    {
        lock (_gate)
        {
            Waits.Until(_gate, () => _publications.Any(reading => reading.Kind == kind && match(reading)), description);
        }
    }
}

/// <summary>
/// A <see cref="FakeTimeProvider"/> that reports when each timer is armed, so a test moves time only after a polling loop
/// has started waiting.
/// </summary>
internal sealed class ObservedTimeProvider : FakeTimeProvider
{
    private readonly object _gate = new();
    private readonly HashSet<ObservedTimer> _armed = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ObservedTimer(this);
        timer.Attach(base.CreateTimer(
            timerState =>
            {
                timer.Disarm();
                callback(timerState);
            },
            state,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan));
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Waits until at least <paramref name="count"/> timers are armed for <paramref name="at"/> after the start.</summary>
    public void WaitForArmed(TimeSpan at, int count)
    {
        lock (_gate)
        {
            Waits.Until(_gate, () => _armed.Count(timer => timer.DueAt == Start + at) >= count, $"{count} timers armed for {at}");
        }
    }

    private sealed class ObservedTimer(ObservedTimeProvider owner) : ITimer
    {
        private ITimer? _inner;

        public DateTimeOffset? DueAt { get; private set; }

        public void Attach(ITimer inner) => _inner = inner;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan)
            {
                throw new NotSupportedException("The monitor uses one-shot timers only.");
            }

            // Report the timer as armed only after the fake clock holds it, so a test cannot advance past it first.
            DateTimeOffset? dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
            var changed = _inner!.Change(dueTime, period);
            lock (owner._gate)
            {
                DueAt = dueAt;
                if (DueAt is null)
                {
                    owner._armed.Remove(this);
                }
                else
                {
                    owner._armed.Add(this);
                }

                Monitor.PulseAll(owner._gate);
            }

            return changed;
        }

        public void Disarm()
        {
            lock (owner._gate)
            {
                DueAt = null;
                owner._armed.Remove(this);
            }
        }

        public void Dispose()
        {
            Disarm();
            _inner!.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// A minimal manual clock whose UTC time can jump independently of its monotonic timestamp and one-shot timers.
/// <see cref="FakeTimeProvider"/> cannot separate them: its timers follow its UTC time.
/// </summary>
internal sealed class SplitClockTimeProvider : TimeProvider
{
    private static readonly DateTimeOffset Origin = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);

    private readonly object _gate = new();
    private readonly List<ManualTimer> _armed = [];
    private long _ticks;
    private TimeSpan _wallClockJump;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public TimeSpan WallClockJump
    {
        get
        {
            lock (_gate)
            {
                return _wallClockJump;
            }
        }
        set
        {
            lock (_gate)
            {
                _wallClockJump = value;
            }
        }
    }

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _ticks;
        }
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return Origin + TimeSpan.FromTicks(_ticks) + _wallClockJump;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves monotonic time forward, firing each due timer on this thread at its own due time.</summary>
    public void Advance(TimeSpan delta)
    {
        long target;
        lock (_gate)
        {
            target = _ticks + delta.Ticks;
        }

        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _armed.Where(timer => timer.DueTicks <= target).MinBy(timer => timer.DueTicks);
                if (next is null)
                {
                    _ticks = target;
                    return;
                }

                _ticks = next.DueTicks;
                _armed.Remove(next);
            }

            next.Fire();
        }
    }

    private sealed class ManualTimer(SplitClockTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public long DueTicks { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (period != Timeout.InfiniteTimeSpan)
            {
                throw new NotSupportedException("The monitor uses one-shot timers only.");
            }

            lock (owner._gate)
            {
                owner._armed.Remove(this);
                if (dueTime != Timeout.InfiniteTimeSpan)
                {
                    DueTicks = owner._ticks + dueTime.Ticks;
                    owner._armed.Add(this);
                }
            }

            return true;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._armed.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

internal static class Samples
{
    public static readonly MetricSample Cpu = new MeasuredSample(12);
    public static readonly MetricSample Ram = new MeasuredRamSample(4UL << 30, 16UL << 30);
    public static readonly MetricSample Gpu = new SampleUnavailable(UnavailabilityReason.NotYetSampled);

    public static MetricSample For(MetricKind kind) => kind switch { MetricKind.Cpu => Cpu, MetricKind.Ram => Ram, _ => Gpu };

    public static MetricKind Other(MetricKind kind) => kind == MetricKind.Cpu ? MetricKind.Ram : MetricKind.Cpu;
}
