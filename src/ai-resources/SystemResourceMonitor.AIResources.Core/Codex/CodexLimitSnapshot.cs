namespace SystemResourceMonitor.AIResources.Core.Codex;

/// <summary>
/// One qualified limit window: the client's integer used share and the UTC reset time. Construct it only through
/// <see cref="Create"/>, so the used share is always within 0 through 100.
/// </summary>
public sealed record CodexLimitWindow
{
    private CodexLimitWindow(int usedPercent, DateTimeOffset resetsAt)
    {
        UsedPercent = usedPercent;
        ResetsAt = resetsAt;
    }

    /// <summary>The used share as the app-server reports it, already rounded by the client.</summary>
    public int UsedPercent { get; }

    /// <summary>The remaining limit: <c>100 − used</c>, the official client's verified rule.</summary>
    public int RemainingPercent => 100 - UsedPercent;

    /// <summary>
    /// The reset time in UTC. While the short window has not started (0 % used) it slides forward with every poll.
    /// </summary>
    public DateTimeOffset ResetsAt { get; }

    /// <summary>Returns the window, or <see langword="null"/> when the used share is outside 0 through 100.</summary>
    public static CodexLimitWindow? Create(int usedPercent, DateTimeOffset resetsAt) =>
        usedPercent is >= 0 and <= 100 ? new CodexLimitWindow(usedPercent, resetsAt.ToUniversalTime()) : null;
}

/// <summary>
/// The limit snapshot of the qualified aggregate <c>codex</c> bucket: its 300-minute short window and its
/// 10080-minute weekly window. No monthly or per-model window is qualified.
/// </summary>
public sealed record CodexLimitSnapshot(CodexLimitWindow ShortWindow, CodexLimitWindow WeeklyWindow)
{
    public CodexLimitWindow ShortWindow { get; } = ShortWindow ?? throw new ArgumentNullException(nameof(ShortWindow));

    public CodexLimitWindow WeeklyWindow { get; } = WeeklyWindow ?? throw new ArgumentNullException(nameof(WeeklyWindow));
}

/// <summary>Why Codex limits have no trustworthy current value. Unavailable is never shown as zero.</summary>
public enum CodexUnavailableReason
{
    /// <summary>No poll has completed yet.</summary>
    NotYetSampled,

    /// <summary>The client reported that no Codex account is signed in.</summary>
    SignedOut,

    /// <summary>The client could not reach its usage service, and no value younger than the stale limit remains.</summary>
    NetworkFailed,

    /// <summary>The client answered outside the qualified response contract or exceeded an output limit.</summary>
    UnknownFormat,

    /// <summary>The installed client is not the qualified version.</summary>
    UnsupportedVersion,

    /// <summary>No installed standalone client was found.</summary>
    ClientNotFound,

    /// <summary>The client failed the path, reparse-point, permission, or publisher-signature check.</summary>
    ClientUntrusted,

    /// <summary>The client did not answer within the request timeout.</summary>
    TimedOut,

    /// <summary>The client could not be started or read.</summary>
    ReadFailed,

    /// <summary>The last value reached its age limit without a newer poll.</summary>
    Expired,
}

/// <summary>The result of one Codex poll: exactly one of <see cref="CodexReadSucceeded"/> or <see cref="CodexReadFailed"/>.</summary>
public abstract record CodexReadResult
{
    private protected CodexReadResult()
    {
    }
}

public sealed record CodexReadSucceeded(CodexLimitSnapshot Snapshot) : CodexReadResult
{
    public CodexLimitSnapshot Snapshot { get; } = Snapshot ?? throw new ArgumentNullException(nameof(Snapshot));
}

/// <summary>A failed poll. A source never reports <see cref="CodexUnavailableReason.NotYetSampled"/> or <see cref="CodexUnavailableReason.Expired"/>.</summary>
public sealed record CodexReadFailed : CodexReadResult
{
    public CodexReadFailed(CodexUnavailableReason reason)
    {
        if (reason is CodexUnavailableReason.NotYetSampled or CodexUnavailableReason.Expired || !Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A source cannot report this reason.");
        }

        Reason = reason;
    }

    public CodexUnavailableReason Reason { get; }
}
