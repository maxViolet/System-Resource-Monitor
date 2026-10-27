using System.Globalization;
using SystemResourceMonitor.AIResources.Core;
using SystemResourceMonitor.AIResources.Core.Claude;

namespace SystemResourceMonitor.Desktop.Bar;

/// <summary>
/// Formats the independent Claude section of ADR 0021 and every observed weekly window; each share is the used share.
/// </summary>
public static class ClaudeSectionFormat
{
    private const string TimeFormat = "d MMM HH:mm";

    public static MetricSection Format(ClaudeLimitState state, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(zone);
        return state switch
        {
            ClaudeLimitsCurrent current => new MetricSection(Text("Claude:", current.Snapshot, now),
                Windows(current.Snapshot, zone)),
            ClaudeLimitsStale stale => new MetricSection(Text("Claude (stale):", stale.Snapshot, now),
                $"Stale: Claude could not be reached. Last update {Local(stale.MeasuredAt, zone)}.\n"
                + Windows(stale.Snapshot, zone)),
            ClaudeLimitsUnavailable unavailable => new MetricSection("Claude: 5h -- | W --",
                Explain(unavailable.Reason)),
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
        if (snapshot is null) return null;
        var weeklyWindow = snapshot.BindingWeeklyWindow;
        var weeklyChange = WeeklyLimitFormat.UntilCountdownChanges(weeklyWindow.UsedPercent, weeklyWindow.ResetsAt, now);
        if (snapshot.ShortWindow.ResetsAt is not { } reset || snapshot.ShortWindow.UsedPercent == 0)
            return weeklyChange;
        var remaining = reset - now;
        if (remaining <= TimeSpan.Zero) return weeklyChange;
        var shortChange = remaining - TimeSpan.FromMinutes(CountdownMinutes(remaining) - 1);
        return weeklyChange is { } week ? TimeSpan.FromTicks(Math.Min(shortChange.Ticks, week.Ticks)) : shortChange;
    }

    public static IEnumerable<string> WidthCandidates()
    {
        foreach (var label in new[] { "Claude:", "Claude (stale):" })
        {
            foreach (var digit in "0123456789")
            {
                var one = digit.ToString();
                var two = new string(digit, 2);
                foreach (var shortValue in new[] { "0%", $"{one}% in {two}:{two}", $"{two}% in {two}:{two}",
                             $"100% in {two}:{two}" })
                foreach (var week in new[] { $"W {one}% in {one}d {two}h", $"W {two}% in {one}d {two}h",
                             $"W 100% in {one}d {two}h", $"W {one}%", $"W {two}%", "W 100%", "W --" })
                    yield return $"{label} 5h {shortValue} | {week}";
            }
        }
        yield return "Claude: 5h -- | W --";
    }

    private static string Text(string label, ClaudeLimitSnapshot snapshot, DateTimeOffset now)
    {
        var shortWindow = snapshot.ShortWindow;
        var shortText = shortWindow.UsedPercent == 0 ? "0%" : ShortText(shortWindow, now);
        var week = snapshot.BindingWeeklyWindow;
        return $"{label} 5h {shortText} | {WeeklyLimitFormat.Format(week.UsedPercent, week.ResetsAt, now)}";
    }

    private static string ShortText(ClaudeLimitWindow window, DateTimeOffset now)
    {
        if (window.ResetsAt is not { } reset) return $"{window.UsedPercent}%";
        var minutes = Math.Min(CountdownMinutes(reset - now), 99 * 60 + 59);
        return string.Create(CultureInfo.InvariantCulture,
            $"{window.UsedPercent}% in {minutes / 60:00}:{minutes % 60:00}");
    }

    private static string Windows(ClaudeLimitSnapshot snapshot, TimeZoneInfo zone)
    {
        var lines = new List<string> { Window(snapshot.ShortWindow, zone) };
        lines.AddRange(snapshot.WeeklyWindows.Select(window => Window(window, zone)));
        if (snapshot.MonthlyWindow is { } month) lines.Add(Window(month, zone));
        return string.Join("\n", lines);
    }

    private static string Window(ClaudeLimitWindow window, TimeZoneInfo zone) =>
        window.ResetsAt is { } reset
            ? $"{window.Label}: {window.UsedPercent}% used, resets {Local(reset, zone)} (minute precision)."
            : $"{window.Label}: {window.UsedPercent}% used; Claude Code shows no reset time for an unused window.";

    /// <summary>The safe tooltip for <paramref name="reason"/>; an undefined reason throws.</summary>
    internal static string Explain(ClaudeUnavailableReason reason) => reason switch
    {
        ClaudeUnavailableReason.NotYetSampled => "Waiting for the first Claude reading.",
        ClaudeUnavailableReason.SignedOut => "Claude is signed out. Sign in with Claude Code.",
        ClaudeUnavailableReason.NetworkFailed => "Claude could not be reached.",
        ClaudeUnavailableReason.UnknownFormat => "Claude answered in an unsupported format.",
        ClaudeUnavailableReason.UnsupportedVersion =>
            $"The installed Claude Code version is not supported. Requires {ClaudeContract.MinimumVersion.ToString(3)} or later.",
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
