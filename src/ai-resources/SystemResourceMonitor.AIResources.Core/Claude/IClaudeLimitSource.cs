namespace SystemResourceMonitor.AIResources.Core.Claude;

/// <summary>
/// Performs one Claude poll through the qualified client. The returned task completes only after the poll's client
/// process tree has exited, so polls never overlap and nothing the poll started outlives it. On cancellation the
/// source terminates the client at once and completes with <see cref="OperationCanceledException"/> after confirming
/// exit, or after the host's termination deadline when exit cannot be confirmed. Every other failure is a
/// <see cref="ClaudeReadFailed"/> result, never an exception.
/// </summary>
public interface IClaudeLimitSource
{
    Task<ClaudeReadResult> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>The current Claude limits: exactly one of current, stale, or unavailable.</summary>
public abstract record ClaudeLimitState
{
    private protected ClaudeLimitState()
    {
    }
}

/// <summary>A snapshot from the latest poll, which succeeded.</summary>
public sealed record ClaudeLimitsCurrent(ClaudeLimitSnapshot Snapshot, DateTimeOffset MeasuredAt, long Timestamp) : ClaudeLimitState;

/// <summary>
/// The last successful snapshot, retained after a network failure until <see cref="ExpiresAt"/>, three minutes after
/// that success.
/// </summary>
public sealed record ClaudeLimitsStale(ClaudeLimitSnapshot Snapshot, DateTimeOffset MeasuredAt, long Timestamp) : ClaudeLimitState
{
    public DateTimeOffset ExpiresAt => MeasuredAt + ClaudeLimitMonitor.StaleLimit;
}

public sealed record ClaudeLimitsUnavailable(ClaudeUnavailableReason Reason) : ClaudeLimitState
{
    public static ClaudeLimitsUnavailable Initial { get; } = new(ClaudeUnavailableReason.NotYetSampled);
}
