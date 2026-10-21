using System.Globalization;
using SystemResourceMonitor.AIResources.Core.Claude;

namespace SystemResourceMonitor.Desktop.Bar;

/// <summary>Formats the independent Claude section and every observed weekly window.</summary>
public static class ClaudeSectionFormat
{
    private const string TimeFormat = "d MMM HH:mm";
    private const string MonthExplanation = "Monthly limit: no qualified Claude monthly window.";

    public static MetricSection Format(ClaudeLimitState state, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(zone);
        return state switch
        {
            ClaudeLimitsCurrent current => new MetricSection(Text("Claude:", current.Snapshot, now),
                WithMonthExplanation(current.Snapshot, Windows(current.Snapshot, zone))),
            ClaudeLimitsStale stale => new MetricSection(Text("Claude (stale):", stale.Snapshot, now),
                $"Stale: Claude could not be reached. Last update {Local(stale.MeasuredAt, zone)}.\n"
                + WithMonthExplanation(stale.Snapshot, Windows(stale.Snapshot, zone))),
            ClaudeLimitsUnavailable unavailable => new MetricSection("Claude: 5H - -- | W -- | month: --",
                Explain(unavailable.Reason) + "\n" + MonthExplanation),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    public static TimeSpan? UntilCountdownChanges(ClaudeLimitState state, DateTimeOffset now)
    {
        var snapshot = state switch
        {
            ClaudeLimitsCurrent current => current.Snapshot,
            ClaudeLimitsStale stale => stale.Snapshot,
            _ => null,
        };
        if (snapshot?.ShortWindow.ResetsAt is not { } reset || snapshot.ShortWindow.RemainingPercent == 100)
            return null;
        var remaining = reset - now;
        if (remaining <= TimeSpan.Zero) return null;
        return remaining - TimeSpan.FromMinutes(CountdownMinutes(remaining) - 1);
    }

    public static IEnumerable<string> WidthCandidates()
    {
        foreach (var label in new[] { "Claude:", "Claude (stale):" })
        {
            foreach (var shortValue in new[] { "100%", "0% in 00 00", "99% in 99 59" })
            foreach (var week in new[] { "0%", "100%" })
            foreach (var month in new[] { "--", "100%" })
                yield return $"{label} 5H - {shortValue} | W {week} | month: {month}";
        }
        yield return "Claude: 5H - -- | W -- | month: --";
    }

    private static string Text(string label, ClaudeLimitSnapshot snapshot, DateTimeOffset now)
    {
        var shortWindow = snapshot.ShortWindow;
        var shortText = shortWindow.RemainingPercent == 100 ? "100%" : ShortText(shortWindow, now);
        var month = snapshot.MonthlyWindow is { } monthly ? $"{monthly.RemainingPercent}%" : "--";
        return $"{label} 5H - {shortText} | W {snapshot.BindingWeeklyWindow.RemainingPercent}% | month: {month}";
    }

    private static string ShortText(ClaudeLimitWindow window, DateTimeOffset now)
    {
        if (window.ResetsAt is not { } reset) return $"{window.RemainingPercent}%";
        var minutes = Math.Min(CountdownMinutes(reset - now), 99 * 60 + 59);
        return string.Create(CultureInfo.InvariantCulture,
            $"{window.RemainingPercent}% in {minutes / 60:00} {minutes % 60:00}");
    }

    private static string Windows(ClaudeLimitSnapshot snapshot, TimeZoneInfo zone)
    {
        var lines = new List<string> { Window(snapshot.ShortWindow, zone) };
        lines.AddRange(snapshot.WeeklyWindows.Select(window => Window(window, zone)));
        if (snapshot.MonthlyWindow is { } month) lines.Add(Window(month, zone));
        return string.Join("\n", lines);
    }

    private static string WithMonthExplanation(ClaudeLimitSnapshot snapshot, string windows) =>
        snapshot.MonthlyWindow is null ? windows + "\n" + MonthExplanation : windows;

    private static string Window(ClaudeLimitWindow window, TimeZoneInfo zone) =>
        window.ResetsAt is { } reset
            ? $"{window.Label}: {window.RemainingPercent}% left, resets {Local(reset, zone)} (minute precision)."
            : $"{window.Label}: {window.RemainingPercent}% left; reset time is not shown by Claude Code.";

    private static string Explain(ClaudeUnavailableReason reason) => reason switch
    {
        ClaudeUnavailableReason.NotYetSampled => "Waiting for the first Claude reading.",
        ClaudeUnavailableReason.SignedOut => "Claude is signed out. Sign in with Claude Code.",
        ClaudeUnavailableReason.NetworkFailed => "Claude could not be reached.",
        ClaudeUnavailableReason.UnknownFormat => "Claude answered in an unsupported format.",
        ClaudeUnavailableReason.UnsupportedVersion =>
            $"The installed Claude Code version is not qualified. Supported: {string.Join(", ", ClaudeContract.QualifiedVersions)}.",
        ClaudeUnavailableReason.ClientNotFound => "The native Claude Code client is not installed.",
        ClaudeUnavailableReason.ClientUntrusted => "The Claude Code executable failed verification.",
        ClaudeUnavailableReason.TimedOut => "Claude Code did not answer in time.",
        ClaudeUnavailableReason.ReadFailed => "Claude Code could not be read.",
        ClaudeUnavailableReason.Expired => "No Claude update for three minutes.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };

    private static long CountdownMinutes(TimeSpan remaining) =>
        remaining <= TimeSpan.Zero ? 0 : (remaining.Ticks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute;

    private static string Local(DateTimeOffset value, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(value, zone).ToString(TimeFormat, CultureInfo.InvariantCulture);
}
