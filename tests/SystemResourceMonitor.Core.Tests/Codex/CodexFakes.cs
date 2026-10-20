using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Core.Tests.Metrics;

namespace SystemResourceMonitor.Core.Tests.Codex;

internal static class CodexSamples
{
    public static readonly DateTimeOffset ShortReset = DateTimeOffset.FromUnixTimeSeconds(1792500924);
    public static readonly DateTimeOffset WeeklyReset = DateTimeOffset.FromUnixTimeSeconds(1793087724);

    public static readonly CodexLimitSnapshot Snapshot = new(
        CodexLimitWindow.Create(27, ShortReset)!, CodexLimitWindow.Create(4, WeeklyReset)!);

    public static readonly CodexLimitSnapshot OtherSnapshot = new(
        CodexLimitWindow.Create(30, ShortReset)!, CodexLimitWindow.Create(5, WeeklyReset)!);

    public static readonly CodexReadResult Success = new CodexReadSucceeded(Snapshot);

    public static CodexReadResult Failure(CodexUnavailableReason reason) => new CodexReadFailed(reason);

    public static bool IsUnavailable(CodexLimitState state, CodexUnavailableReason reason) =>
        state is CodexLimitsUnavailable unavailable && unavailable.Reason == reason;
}

/// <summary>
/// A controllable Codex source: scripted results, a gate that holds reads until released, counters for started and
/// returned reads and the most reads in flight, and the cancellation token of each read.
/// </summary>
internal sealed class FakeCodexSource : ICodexLimitSource
{
    private readonly object _gate = new();
    private readonly List<CancellationToken> _tokens = [];
    private Func<CodexReadResult> _next = () => CodexSamples.Success;
    private Exception? _synchronousFailure;
    private TaskCompletionSource? _block;
    private bool _blockHonorsCancellation;
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

    public IReadOnlyList<CancellationToken> Tokens
    {
        get
        {
            lock (_gate)
            {
                return [.. _tokens];
            }
        }
    }

    public void Returns(CodexReadResult next)
    {
        lock (_gate)
        {
            _next = () => next;
            _synchronousFailure = null;
        }
    }

    /// <summary>Makes later reads fault their returned task with <paramref name="failure"/>.</summary>
    public void Throws(Exception failure)
    {
        lock (_gate)
        {
            _next = () => throw failure;
            _synchronousFailure = null;
        }
    }

    /// <summary>Makes later reads throw <paramref name="failure"/> before returning a task.</summary>
    public void ThrowsSynchronously(Exception failure)
    {
        lock (_gate)
        {
            _synchronousFailure = failure;
        }
    }

    /// <summary>
    /// Makes later reads wait until <see cref="Release"/>. With <paramref name="honorCancellation"/> a waiting read
    /// completes with <see cref="OperationCanceledException"/> once its token is cancelled; otherwise it ignores the
    /// token, like a source still waiting for its client to exit.
    /// </summary>
    public void Block(bool honorCancellation = false)
    {
        lock (_gate)
        {
            _block = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _blockHonorsCancellation = honorCancellation;
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

    public Task<CodexReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource? block;
        bool honorCancellation;
        Func<CodexReadResult> next;
        lock (_gate)
        {
            _reads++;
            _tokens.Add(cancellationToken);
            Monitor.PulseAll(_gate);
            if (_synchronousFailure is { } failure)
            {
                _returned++;
                throw failure;
            }

            _maxInFlight = Math.Max(_maxInFlight, ++_inFlight);
            block = _block;
            honorCancellation = _blockHonorsCancellation;
            next = _next;
        }

        return ReadCoreAsync(block, honorCancellation, next, cancellationToken);
    }

    private async Task<CodexReadResult> ReadCoreAsync(
        TaskCompletionSource? block, bool honorCancellation, Func<CodexReadResult> next, CancellationToken cancellationToken)
    {
        try
        {
            if (block is not null)
            {
                await (honorCancellation ? block.Task.WaitAsync(cancellationToken) : block.Task).ConfigureAwait(false);
            }

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
            Waits.Until(_gate, () => _reads >= count, $"Codex read {count} to start");
        }
    }
}

/// <summary>Records each <see cref="CodexLimitMonitor.Changed"/> notification in order.</summary>
internal sealed class CodexStateRecorder
{
    private readonly object _gate = new();
    private readonly List<CodexLimitState> _states = [];

    public CodexStateRecorder(CodexLimitMonitor monitor) => monitor.Changed += (_, state) =>
    {
        lock (_gate)
        {
            _states.Add(state);
            Monitor.PulseAll(_gate);
        }
    };

    public IReadOnlyList<CodexLimitState> All
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
            Waits.Until(_gate, () => _states.Count >= count, $"{count} Codex publications");
        }
    }
}
