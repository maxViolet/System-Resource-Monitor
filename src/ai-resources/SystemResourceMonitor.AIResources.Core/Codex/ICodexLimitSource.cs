namespace SystemResourceMonitor.AIResources.Core.Codex;

/// <summary>
/// Performs one Codex poll through the qualified client. The returned task completes only after the poll's client
/// process tree has exited, so polls never overlap and nothing the poll started outlives it. On cancellation the
/// source terminates the client at once and completes with <see cref="OperationCanceledException"/> after confirming
/// exit, or after the host's termination deadline when exit cannot be confirmed. Every other failure is a
/// <see cref="CodexReadFailed"/> result, never an exception.
/// </summary>
public interface ICodexLimitSource
{
    Task<CodexReadResult> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>The current Codex limits: exactly one of current, stale, or unavailable.</summary>
public abstract record CodexLimitState
{
    private protected CodexLimitState()
    {
    }
}

/// <summary>A snapshot from the latest poll, which succeeded.</summary>
public sealed record CodexLimitsCurrent(CodexLimitSnapshot Snapshot, DateTimeOffset MeasuredAt, long Timestamp) : CodexLimitState;

/// <summary>
/// The last successful snapshot, retained after a network failure until <see cref="ExpiresAt"/>, three minutes after
/// that success.
/// </summary>
public sealed record CodexLimitsStale(CodexLimitSnapshot Snapshot, DateTimeOffset MeasuredAt, long Timestamp) : CodexLimitState
{
    public DateTimeOffset ExpiresAt => MeasuredAt + CodexLimitMonitor.StaleLimit;
}

public sealed record CodexLimitsUnavailable(CodexUnavailableReason Reason) : CodexLimitState
{
    public static CodexLimitsUnavailable Initial { get; } = new(CodexUnavailableReason.NotYetSampled);
}
