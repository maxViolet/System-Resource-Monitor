using System.Globalization;
using SystemResourceMonitor.AIResources.Core;
using SystemResourceMonitor.AIResources.Core.Codex;

namespace SystemResourceMonitor.Desktop.Bar;

/// <summary>
/// Maps a <see cref="CodexLimitState"/> to the Codex bar section of ADR 0021 with invariant culture:
/// <c>Codex: 5h 24% in HH:mm | W 67% in DD HH</c>, where each share is the used share. Only the qualified aggregate 5-hour and
/// weekly windows are shown; the bar has no monthly field. Tooltips never carry exception text, error codes, or
/// account data.
/// </summary>
public static class CodexSectionFormat
{
    /// <summary>The value of an unavailable field.</summary>
    public const string Unavailable = "--";

    private const string Label = "Codex:";
    private const string StaleLabel = "Codex (stale):";
    private const string TimeFormat = "d MMM HH:mm";
    private static readonly TimeSpan MaximumWeeklyResetAhead =
        TimeSpan.FromMinutes(CodexContract.WeeklyWindowMinutes + 1);

    private static readonly string ExpiredExplanation = string.Create(
        CultureInfo.InvariantCulture, $"No Codex update for {CodexLimitMonitor.StaleLimit.TotalMinutes:0} minutes.");

    /// <summary>Formats <paramref name="state"/> at <paramref name="now"/>; tooltip times are shown in <paramref name="zone"/>.</summary>
    public static MetricSection Format(CodexLimitState state, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(zone);
        return state switch
        {
            CodexLimitsCurrent current => new MetricSection(
                Text(Label, current.Snapshot, now),
                Windows(current.Snapshot, zone, now)),
            CodexLimitsStale stale => new MetricSection(
                Text(StaleLabel, stale.Snapshot, now),
                $"Stale: the Codex usage service could not be reached. Last update {Local(stale.MeasuredAt, zone)}; "
                + $"these values are dropped at {Local(stale.ExpiresAt, zone)}.\n{Windows(stale.Snapshot, zone, now)}"),
            CodexLimitsUnavailable unavailable => new MetricSection(
                Compose(Label, Unavailable, "W --"),
                Explain(unavailable.Reason)),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown Codex limit state."),
        };
    }

    /// <summary>
    /// How long from <paramref name="now"/> until the countdown text of <paramref name="state"/> changes, or
    /// <see langword="null"/> when it shows no running countdown.
    /// </summary>
    public static TimeSpan? UntilCountdownChanges(CodexLimitState state, DateTimeOffset now)
    {
        var snapshot = state switch
        {
            CodexLimitsCurrent current => current.Snapshot,
            CodexLimitsStale stale => stale.Snapshot,
            _ => null,
        };
        if (snapshot is null)
        {
            return null;
        }

        var weeklyChange = WeeklyLimitFormat.UntilCountdownChanges(snapshot.WeeklyWindow.UsedPercent,
            snapshot.WeeklyWindow.ResetsAt, now, MaximumWeeklyResetAhead);
        if (snapshot.ShortWindow.UsedPercent == 0)
        {
            return weeklyChange;
        }

        var remaining = snapshot.ShortWindow.ResetsAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            return weeklyChange;
        }

        // The text shows whole minutes rounded up, so it changes when the remainder crosses the next lower minute.
        var shortChange = remaining - TimeSpan.FromMinutes(CountdownMinutes(remaining) - 1);
        return weeklyChange is { } week ? TimeSpan.FromTicks(Math.Min(shortChange.Ticks, week.Ticks)) : shortChange;
    }

    /// <summary>
    /// Every text width class the section can show: both labels, one-, two-, and three-digit shares with a countdown,
    /// 0 % without one, every weekly width, each digit, and the unavailable text.
    /// </summary>
    public static IEnumerable<string> WidthCandidates()
    {
        foreach (var label in (string[])[Label, StaleLabel])
        {
            for (var digit = '0'; digit <= '9'; digit++)
            {
                var one = digit.ToString();
                var two = new string(digit, 2);
                foreach (var shortWindow in (string[])[$"{one}% in {two}:{two}", $"{two}% in {two}:{two}", $"100% in {two}:{two}", "0%"])
                {
                    foreach (var weekly in (string[])[$"W {one}% in {two} {two}", $"W {two}% in {two} {two}",
                                 $"W 100% in {two} {two}", $"W {one}%", $"W {two}%", "W 100%"])
                    {
                        yield return Compose(label, shortWindow, weekly);
                    }
                }
            }
        }

        yield return Compose(Label, Unavailable, "W --");
    }

    /// <summary>The safe tooltip for <paramref name="reason"/>; an undefined reason throws.</summary>
    internal static string Explain(CodexUnavailableReason reason) => reason switch
    {
        CodexUnavailableReason.NotYetSampled => "Waiting for the first Codex reading.",
        CodexUnavailableReason.SignedOut => "Codex is signed out. Sign in with the Codex client.",
        CodexUnavailableReason.NetworkFailed => "The Codex usage service could not be reached.",
        CodexUnavailableReason.UnknownFormat => "Codex answered in a format this bar does not recognise.",
        CodexUnavailableReason.UnsupportedVersion =>
            $"The installed Codex client is not version {CodexContract.QualifiedVersion}, the only supported version.",
        CodexUnavailableReason.ClientNotFound => "The standalone Codex client is not installed.",
        CodexUnavailableReason.ClientUntrusted => "The Codex client failed the path, permission, or signature check.",
        CodexUnavailableReason.TimedOut => "Codex did not answer in time.",
        CodexUnavailableReason.ReadFailed => "The Codex client could not be started.",
        CodexUnavailableReason.Expired => ExpiredExplanation,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Undefined reason."),
    };

    private static string Text(string label, CodexLimitSnapshot snapshot, DateTimeOffset now)
    {
        var shortWindow = snapshot.ShortWindow;
        var weekly = WeeklyLimitFormat.Format(snapshot.WeeklyWindow.UsedPercent, snapshot.WeeklyWindow.ResetsAt,
            now, MaximumWeeklyResetAhead);
        if (shortWindow.UsedPercent == 0)
        {
            // The unused window's reset time slides with each poll, so it has no countdown.
            return Compose(label, "0%", weekly);
        }

        // A qualified 5-hour reset is at most 300 minutes away; the clamp only keeps an outlier inside two-digit hours.
        var minutes = Math.Min(CountdownMinutes(shortWindow.ResetsAt - now), (99 * 60) + 59);
        return Compose(label, string.Create(CultureInfo.InvariantCulture,
            $"{shortWindow.UsedPercent}% in {minutes / 60:00}:{minutes % 60:00}"), weekly);
    }

    /// <summary>The one section layout.</summary>
    private static string Compose(string label, string shortWindow, string weekly) =>
        $"{label} 5h {shortWindow} | {weekly}";

    private static string Windows(CodexLimitSnapshot snapshot, TimeZoneInfo zone, DateTimeOffset now)
    {
        var shortWindow = snapshot.ShortWindow;
        var shortLine = shortWindow.UsedPercent == 0
            ? "5-hour limit: 0% used; the window starts with the next use."
            : string.Create(CultureInfo.InvariantCulture,
                $"5-hour limit: {shortWindow.UsedPercent}% used, resets {Local(shortWindow.ResetsAt, zone)}.");
        var weeklyWindow = snapshot.WeeklyWindow;
        var weeklyLine = weeklyWindow.ResetsAt - now > MaximumWeeklyResetAhead
            ? "Weekly limit: reset time is outside the qualified seven-day window."
            : string.Create(CultureInfo.InvariantCulture,
                $"Weekly limit: {weeklyWindow.UsedPercent}% used, resets {Local(weeklyWindow.ResetsAt, zone)}.");
        return $"{shortLine}\n{weeklyLine}";
    }

    /// <summary>Whole minutes rounded up, so a positive remainder never shows <c>00:00</c>; a passed reset shows zero.</summary>
    private static long CountdownMinutes(TimeSpan remaining) =>
        remaining <= TimeSpan.Zero ? 0 : (remaining.Ticks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute;

    private static string Local(DateTimeOffset time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(time, zone).ToString(TimeFormat, CultureInfo.InvariantCulture);
}
