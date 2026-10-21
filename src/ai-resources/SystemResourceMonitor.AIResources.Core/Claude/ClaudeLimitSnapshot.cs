namespace SystemResourceMonitor.AIResources.Core.Claude;

/// <summary>A subscription window reported by the qualified Claude client.</summary>
public sealed record ClaudeLimitWindow
{
    private ClaudeLimitWindow(string label, int usedPercent, DateTimeOffset? resetsAt)
    {
        Label = label;
        UsedPercent = usedPercent;
        ResetsAt = resetsAt?.ToUniversalTime();
    }

    public string Label { get; }
    public int UsedPercent { get; }
    public int RemainingPercent => 100 - UsedPercent;

    /// <summary>The client omits the reset of an unused window.</summary>
    public DateTimeOffset? ResetsAt { get; }

    public static ClaudeLimitWindow? Create(string label, int usedPercent, DateTimeOffset? resetsAt)
    {
        if (string.IsNullOrWhiteSpace(label) || usedPercent is < 0 or > 100 || (usedPercent > 0 && resetsAt is null))
        {
            return null;
        }

        return new ClaudeLimitWindow(label, usedPercent, resetsAt);
    }
}

/// <summary>All windows returned in one Claude Code /usage observation.</summary>
public sealed record ClaudeLimitSnapshot
{
    public ClaudeLimitSnapshot(ClaudeLimitWindow shortWindow, IReadOnlyList<ClaudeLimitWindow> weeklyWindows,
        ClaudeLimitWindow? monthlyWindow = null)
    {
        ShortWindow = shortWindow ?? throw new ArgumentNullException(nameof(shortWindow));
        ArgumentNullException.ThrowIfNull(weeklyWindows);
        if (weeklyWindows.Count == 0)
        {
            throw new ArgumentException("At least one weekly window is required.", nameof(weeklyWindows));
        }

        WeeklyWindows = weeklyWindows.ToArray();
        MonthlyWindow = monthlyWindow;
    }

    public ClaudeLimitWindow ShortWindow { get; }
    public IReadOnlyList<ClaudeLimitWindow> WeeklyWindows { get; }
    public ClaudeLimitWindow BindingWeeklyWindow => WeeklyWindows.MinBy(window => window.RemainingPercent)!;
    public ClaudeLimitWindow? MonthlyWindow { get; }
}

public enum ClaudeUnavailableReason
{
    NotYetSampled,
    SignedOut,
    NetworkFailed,
    UnknownFormat,
    UnsupportedVersion,
    ClientNotFound,
    ClientUntrusted,
    TimedOut,
    ReadFailed,
    Expired,
}

public abstract record ClaudeReadResult
{
    private protected ClaudeReadResult() { }
}

public sealed record ClaudeReadSucceeded(ClaudeLimitSnapshot Snapshot) : ClaudeReadResult;

public sealed record ClaudeReadFailed(ClaudeUnavailableReason Reason) : ClaudeReadResult;
