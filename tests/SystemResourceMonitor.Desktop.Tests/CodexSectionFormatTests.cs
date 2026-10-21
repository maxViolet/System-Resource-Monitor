using System.Globalization;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class CodexSectionFormatTests
{
    private const string UnavailableText = "Codex: 5h -- | W --";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    // 2026-10-24 21:30 UTC is 25 Oct 02:30 at +05:00, so the tooltip proves the zone conversion crosses the date.
    private static readonly DateTimeOffset WeeklyReset = new(2026, 10, 24, 21, 30, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Plus5 =
        TimeZoneInfo.CreateCustomTimeZone("Test +05:00", TimeSpan.FromHours(5), "Test +05:00", "Test +05:00");

    private static readonly Dictionary<CodexUnavailableReason, string> Explanations = new()
    {
        [CodexUnavailableReason.NotYetSampled] = "Waiting for the first Codex reading.",
        [CodexUnavailableReason.SignedOut] = "Codex is signed out. Sign in with the Codex client.",
        [CodexUnavailableReason.NetworkFailed] = "The Codex usage service could not be reached.",
        [CodexUnavailableReason.UnknownFormat] = "Codex answered in a format this bar does not recognise.",
        [CodexUnavailableReason.UnsupportedVersion] = "The installed Codex client is not version 0.159.0, the only supported version.",
        [CodexUnavailableReason.ClientNotFound] = "The standalone Codex client is not installed.",
        [CodexUnavailableReason.ClientUntrusted] = "The Codex client failed the path, permission, or signature check.",
        [CodexUnavailableReason.TimedOut] = "Codex did not answer in time.",
        [CodexUnavailableReason.ReadFailed] = "The Codex client could not be started.",
        [CodexUnavailableReason.Expired] = "No Codex update for 3 minutes.",
    };

    [Fact]
    public void Current_limits_show_used_shares_and_the_rounded_up_countdown()
    {
        var section = CodexSectionFormat.Format(Current(27, Now + new TimeSpan(1, 41, 30), 4), Now, Plus5);

        Assert.Equal("Codex: 5h 27% in 01:42 | W 4%", section.Text);
        Assert.Equal(
            "5-hour limit: 27% used, resets 20 Oct 16:41.\nWeekly limit: 4% used, resets 25 Oct 02:30.",
            section.ToolTip);
    }

    [Fact]
    public void Exhausted_short_window_still_counts_down_and_unused_weekly_window_shows_zero()
    {
        var section = CodexSectionFormat.Format(Current(100, Now + TimeSpan.FromMinutes(3), 0), Now, Plus5);

        Assert.Equal("Codex: 5h 100% in 00:03 | W 0%", section.Text);
    }

    [Fact]
    public void Unused_short_window_has_no_countdown_although_it_has_a_reset_time()
    {
        var state = Current(0, Now + TimeSpan.FromMinutes(300), 4);

        var section = CodexSectionFormat.Format(state, Now, Plus5);

        Assert.Equal("Codex: 5h 0% | W 4%", section.Text);
        Assert.Equal(
            "5-hour limit: 0% used; the window starts with the next use.\nWeekly limit: 4% used, resets 25 Oct 02:30.",
            section.ToolTip);
        Assert.Null(CodexSectionFormat.UntilCountdownChanges(state, Now));
    }

    [Fact]
    public void Stale_limits_keep_their_values_under_the_stale_label_and_state_update_and_drop_times()
    {
        var state = new CodexLimitsStale(
            Snapshot(27, Now + new TimeSpan(1, 41, 30), 4),
            Now - TimeSpan.FromMinutes(1),
            Timestamp: 0);

        var section = CodexSectionFormat.Format(state, Now, Plus5);

        Assert.Equal("Codex (stale): 5h 27% in 01:42 | W 4%", section.Text);
        Assert.Equal(
            "Stale: the Codex usage service could not be reached. Last update 20 Oct 14:59; these values are dropped at 20 Oct 15:02.\n"
            + "5-hour limit: 27% used, resets 20 Oct 16:41.\nWeekly limit: 4% used, resets 25 Oct 02:30.",
            section.ToolTip);
    }

    [Fact]
    public void Stale_unused_short_window_has_no_countdown()
    {
        var state = new CodexLimitsStale(Snapshot(0, Now + TimeSpan.FromMinutes(10), 50), Now, 0);

        Assert.Equal("Codex (stale): 5h 0% | W 50%", CodexSectionFormat.Format(state, Now, Plus5).Text);
        Assert.Null(CodexSectionFormat.UntilCountdownChanges(state, Now));
    }

    [Fact]
    public void Every_unavailable_reason_shows_dashes_and_its_safe_explanation()
    {
        var reasons = Enum.GetValues<CodexUnavailableReason>();
        Assert.Equal(reasons.Length, Explanations.Count);

        foreach (var reason in reasons)
        {
            var state = new CodexLimitsUnavailable(reason);

            var section = CodexSectionFormat.Format(state, Now, Plus5);

            Assert.Equal(UnavailableText, section.Text);
            Assert.Equal(Explanations[reason], section.ToolTip);
            Assert.Null(CodexSectionFormat.UntilCountdownChanges(state, Now));
        }
    }

    [Fact]
    public void Undefined_reason_throws()
    {
        var undefined = (CodexUnavailableReason)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => CodexSectionFormat.Explain(undefined));
        Assert.Throws<ArgumentOutOfRangeException>(() => CodexSectionFormat.Format(new CodexLimitsUnavailable(undefined), Now, Plus5));
    }

    [Theory]
    [InlineData(1L, "00:01")]
    [InlineData(TimeSpan.TicksPerMinute, "00:01")]
    [InlineData(TimeSpan.TicksPerMinute + 1, "00:02")]
    [InlineData(59 * TimeSpan.TicksPerMinute + 1, "01:00")]
    [InlineData(300 * TimeSpan.TicksPerMinute, "05:00")]
    [InlineData(0L, "00:00")]
    [InlineData(-TimeSpan.TicksPerSecond, "00:00")]
    public void Countdown_rounds_the_remainder_up_to_whole_minutes_and_stops_at_zero(long remainderTicks, string countdown)
    {
        var section = CodexSectionFormat.Format(Current(27, Now + TimeSpan.FromTicks(remainderTicks), 4), Now, Plus5);

        Assert.Equal($"Codex: 5h 27% in {countdown} | W 4%", section.Text);
    }

    [Theory]
    [InlineData(90 * TimeSpan.TicksPerSecond, 30 * TimeSpan.TicksPerSecond)]
    [InlineData(120 * TimeSpan.TicksPerSecond, 60 * TimeSpan.TicksPerSecond)]
    [InlineData(TimeSpan.TicksPerMinute + 1, 1L)]
    [InlineData(1L, 1L)]
    [InlineData(300 * TimeSpan.TicksPerMinute, TimeSpan.TicksPerMinute)]
    public void Countdown_changes_when_the_remainder_crosses_the_next_lower_whole_minute(long remainderTicks, long untilTicks)
    {
        var current = Current(27, Now + TimeSpan.FromTicks(remainderTicks), 4);
        var stale = new CodexLimitsStale(current.Snapshot, Now, 0);

        var until = CodexSectionFormat.UntilCountdownChanges(current, Now);

        var change = Assert.NotNull(until);
        Assert.Equal(TimeSpan.FromTicks(untilTicks), change);
        Assert.Equal(until, CodexSectionFormat.UntilCountdownChanges(stale, Now));
        // The text differs at the change and not one tick before it.
        var before = CodexSectionFormat.Format(current, Now + change - TimeSpan.FromTicks(1), Plus5).Text;
        var at = CodexSectionFormat.Format(current, Now, Plus5).Text;
        var after = CodexSectionFormat.Format(current, Now + change, Plus5).Text;
        Assert.Equal(at, before);
        Assert.NotEqual(at, after);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-TimeSpan.TicksPerSecond)]
    public void Passed_reset_has_no_countdown_change(long remainderTicks)
    {
        Assert.Null(CodexSectionFormat.UntilCountdownChanges(Current(27, Now + TimeSpan.FromTicks(remainderTicks), 4), Now));
    }

    [Fact]
    public void Formatting_does_not_depend_on_the_current_culture()
    {
        var state = Current(27, Now + new TimeSpan(1, 41, 30), 4);
        var stale = new CodexLimitsStale(state.Snapshot, Now - TimeSpan.FromMinutes(1), 0);
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var section = CodexSectionFormat.Format(state, Now, Plus5);
            Assert.Equal("Codex: 5h 27% in 01:42 | W 4%", section.Text);
            Assert.Equal(
                "5-hour limit: 27% used, resets 20 Oct 16:41.\nWeekly limit: 4% used, resets 25 Oct 02:30.",
                section.ToolTip);
            Assert.StartsWith(
                "Stale: the Codex usage service could not be reached. Last update 20 Oct 14:59; these values are dropped at 20 Oct 15:02.\n",
                CodexSectionFormat.Format(stale, Now, Plus5).ToolTip);
            Assert.Equal(
                "No Codex update for 3 minutes.",
                CodexSectionFormat.Format(new CodexLimitsUnavailable(CodexUnavailableReason.Expired), Now, Plus5).ToolTip);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    private static CodexLimitsCurrent Current(int shortUsed, DateTimeOffset shortResetsAt, int weeklyUsed) =>
        new(Snapshot(shortUsed, shortResetsAt, weeklyUsed), Now, Timestamp: 0);

    private static CodexLimitSnapshot Snapshot(int shortUsed, DateTimeOffset shortResetsAt, int weeklyUsed) => new(
        CodexLimitWindow.Create(shortUsed, shortResetsAt)!,
        CodexLimitWindow.Create(weeklyUsed, WeeklyReset)!);
}
