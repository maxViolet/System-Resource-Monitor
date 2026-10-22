using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Core.Tests.Metrics;

namespace SystemResourceMonitor.Core.Tests.Claude;

internal static class ClaudeSamples
{
    public static readonly DateTimeOffset ShortReset = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset WeeklyReset = new(2026, 10, 24, 22, 0, 0, TimeSpan.Zero);

    public static readonly ClaudeLimitSnapshot Snapshot = new(
        ClaudeLimitWindow.Create("Current session", 25, ShortReset)!,
        [ClaudeLimitWindow.Create("Current week (all models)", 5, WeeklyReset)!]);

    public static readonly ClaudeReadResult Success = new ClaudeReadSucceeded(Snapshot);

    public static ClaudeReadResult Failure(ClaudeUnavailableReason reason) => new ClaudeReadFailed(reason);

    public static bool IsUnavailable(ClaudeLimitState state, ClaudeUnavailableReason reason) =>
        state is ClaudeLimitsUnavailable unavailable && unavailable.Reason == reason;
}

/// <summary>
/// A controllable Claude source: scripted results, a gate that holds reads until released, counters for started and
/// returned reads and the most reads in flight.
/// </summary>
internal sealed class FakeClaudeSource : IClaudeLimitSource
{
    private readonly object _gate = new();
    private ClaudeReadResult _next = ClaudeSamples.Success;
    private TaskCompletionSource? _block;
    private int _reads;
    private int _returned;
    private int _inFlight;
    private int _maxInFlight;

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

    public int Returned
    {
        get
        {
            lock (_gate)
            {
                return _returned;
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

    public void Returns(ClaudeReadResult next)
    {
        lock (_gate)
        {
            _next = next;
        }
    }

    /// <summary>Makes later reads wait until <see cref="Release"/>, ignoring cancellation like a client still exiting.</summary>
    public void Block()
    {
        lock (_gate)
        {
            _block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _block?.TrySetResult();
            _block = null;
        }
    }

    public Task<ClaudeReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource? block;
        ClaudeReadResult next;
        lock (_gate)
        {
            _reads++;
            _maxInFlight = Math.Max(_maxInFlight, ++_inFlight);
            Monitor.PulseAll(_gate);
            block = _block;
            next = _next;
        }

        return ReadCoreAsync(block, next);
    }

    private async Task<ClaudeReadResult> ReadCoreAsync(TaskCompletionSource? block, ClaudeReadResult next)
    {
        try
        {
            if (block is not null)
            {
                await block.Task.ConfigureAwait(false);
            }

            return next;
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
            Waits.Until(_gate, () => _reads >= count, $"Claude read {count} to start");
        }
    }
}

/// <summary>Records each <see cref="ClaudeLimitMonitor.Changed"/> notification in order.</summary>
internal sealed class ClaudeStateRecorder
{
    private readonly object _gate = new();
    private readonly List<ClaudeLimitState> _states = [];

    public ClaudeStateRecorder(ClaudeLimitMonitor monitor) => monitor.Changed += (_, state) =>
    {
        lock (_gate)
        {
            _states.Add(state);
            Monitor.PulseAll(_gate);
        }
    };

    public IReadOnlyList<ClaudeLimitState> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _states];
            }
        }
    }

    public void WaitFor(int count)
    {
        lock (_gate)
        {
            Waits.Until(_gate, () => _states.Count >= count, $"{count} Claude publications");
        }
    }
}
