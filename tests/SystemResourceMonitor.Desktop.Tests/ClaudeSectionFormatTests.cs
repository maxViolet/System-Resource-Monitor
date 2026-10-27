using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class ClaudeSectionFormatTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone(
        "Test +05", TimeSpan.FromHours(5), "Test +05", "Test +05");

    [Fact]
    public void Unused_short_window_has_no_countdown_and_missing_reset_is_explained()
    {
        var snapshot = new ClaudeLimitSnapshot(ClaudeLimitWindow.Create("Current session", 0, null)!,
            [ClaudeLimitWindow.Create("Current week (all models)", 0, null)!]);
        var state = new ClaudeLimitsCurrent(snapshot, Now, 0);
        var section = ClaudeSectionFormat.Format(state, Now, Zone);

        Assert.Equal("Claude: 5h 0% | W 0%", section.Text);
        Assert.Equal("Current session: 0% used; Claude Code shows no reset time for an unused window.\n"
            + "Current week (all models): 0% used; Claude Code shows no reset time for an unused window.", section.ToolTip);
        Assert.Null(ClaudeSectionFormat.UntilCountdownChanges(state, Now));
    }

    [Fact]
    public void Unused_short_window_with_a_reported_reset_still_has_no_countdown()
    {
        var state = Current(0, Now.AddHours(5));

        Assert.Equal("Claude: 5h 0% | W 5% in 1d 0h", ClaudeSectionFormat.Format(state, Now, Zone).Text);
        Assert.Equal(TimeSpan.FromHours(1), ClaudeSectionFormat.UntilCountdownChanges(state, Now));
    }

    [Fact]
    public void Tooltip_lists_every_window_at_minute_precision()
    {
        var snapshot = new ClaudeLimitSnapshot(
            ClaudeLimitWindow.Create("Current session", 24, Now.AddMinutes(61).AddSeconds(1))!,
            [ClaudeLimitWindow.Create("Current week (all models)", 5, Now.AddDays(1))!,
             ClaudeLimitWindow.Create("Current week (Fable)", 40, Now.AddDays(2))!],
            ClaudeLimitWindow.Create("Current month", 18, Now.AddDays(7))!);

        Assert.Equal("Current session: 24% used, resets 21 Oct 11:01 (minute precision).\n"
            + "Current week (all models): 5% used, resets 22 Oct 10:00 (minute precision).\n"
            + "Current week (Fable): 40% used, resets 23 Oct 10:00 (minute precision).\n"
            + "Current month: 18% used, resets 28 Oct 10:00 (minute precision).",
            ClaudeSectionFormat.Format(new ClaudeLimitsCurrent(snapshot, Now, 0), Now, Zone).ToolTip);
    }

    [Theory]
    [InlineData(ClaudeUnavailableReason.NotYetSampled, "Waiting for the first Claude reading.")]
    [InlineData(ClaudeUnavailableReason.SignedOut, "Claude is signed out. Sign in with Claude Code.")]
    [InlineData(ClaudeUnavailableReason.NetworkFailed, "Claude could not be reached.")]
    [InlineData(ClaudeUnavailableReason.UnknownFormat, "Claude answered in an unsupported format.")]
    [InlineData(ClaudeUnavailableReason.UnsupportedVersion,
        "The installed Claude Code version is not supported. Requires 2.1.304 or later.")]
    [InlineData(ClaudeUnavailableReason.ClientNotFound, "The native Claude Code client is not installed.")]
    [InlineData(ClaudeUnavailableReason.ClientUntrusted, "The Claude Code executable failed verification.")]
    [InlineData(ClaudeUnavailableReason.TimedOut, "Claude Code did not answer in time.")]
    [InlineData(ClaudeUnavailableReason.ReadFailed, "Claude Code could not be read.")]
    [InlineData(ClaudeUnavailableReason.Expired, "No Claude update for three minutes.")]
    public void Every_unavailable_reason_has_a_safe_explanation(ClaudeUnavailableReason reason, string explanation)
    {
        var section = ClaudeSectionFormat.Format(new ClaudeLimitsUnavailable(reason), Now, Zone);

        Assert.Equal("Claude: 5h -- | W --", section.Text);
        Assert.Equal(explanation, section.ToolTip);
    }

    [Fact]
    public void Every_reason_is_explained_distinctly()
    {
        var explanations = Enum.GetValues<ClaudeUnavailableReason>().Select(ClaudeSectionFormat.Explain).ToList();

        Assert.Equal(explanations.Count, explanations.Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => ClaudeSectionFormat.Explain((ClaudeUnavailableReason)99));
    }

    [Fact]
    public void Active_countdown_rounds_up_and_binding_weekly_window_is_displayed()
    {
        var snapshot = new ClaudeLimitSnapshot(
            ClaudeLimitWindow.Create("Current session", 24, Now.AddMinutes(61).AddSeconds(1))!,
            [ClaudeLimitWindow.Create("Current week (all models)", 5, Now.AddDays(1))!,
             ClaudeLimitWindow.Create("Current week (Fable)", 40, Now.AddDays(2))!]);
        var section = ClaudeSectionFormat.Format(new ClaudeLimitsCurrent(snapshot, Now, 0), Now, Zone);

        Assert.Equal("Claude: 5h 24% in 01:02 | W 40% in 2d 0h", section.Text);
        Assert.Contains("Current week (all models)", section.ToolTip);
        Assert.Contains("Current week (Fable)", section.ToolTip);
    }

    [Fact]
    public void Qualified_monthly_window_stays_in_the_tooltip_only_and_stale_and_unavailable_states_are_distinct()
    {
        var snapshot = new ClaudeLimitSnapshot(
            ClaudeLimitWindow.Create("Current session", 50, Now.AddHours(1))!,
            [ClaudeLimitWindow.Create("Current week (all models)", 20, Now.AddDays(1))!],
            ClaudeLimitWindow.Create("Current month", 10, Now.AddDays(7))!);
        var stale = ClaudeSectionFormat.Format(new ClaudeLimitsStale(snapshot, Now, 0), Now, Zone);
        var unavailable = ClaudeSectionFormat.Format(
            new ClaudeLimitsUnavailable(ClaudeUnavailableReason.UnknownFormat), Now, Zone);

        Assert.Equal("Claude (stale): 5h 50% in 01:00 | W 20% in 1d 0h", stale.Text);
        Assert.Contains("Current month: 10% used", stale.ToolTip);
        Assert.Equal("Claude: 5h -- | W --", unavailable.Text);
        Assert.Contains("unsupported format", unavailable.ToolTip);
    }

    [Theory]
    [InlineData(1L, "00:01")]
    [InlineData(TimeSpan.TicksPerMinute, "00:01")]
    [InlineData(TimeSpan.TicksPerSecond, "00:01")]
    [InlineData(TimeSpan.TicksPerMinute + 1, "00:02")]
    [InlineData(60 * TimeSpan.TicksPerMinute, "01:00")]
    [InlineData(61 * TimeSpan.TicksPerMinute + TimeSpan.TicksPerSecond, "01:02")]
    [InlineData(299 * TimeSpan.TicksPerMinute, "04:59")]
    [InlineData(300 * TimeSpan.TicksPerMinute, "05:00")]
    [InlineData(200 * TimeSpan.TicksPerHour, "99:59")]
    [InlineData(0L, "00:00")]
    [InlineData(-TimeSpan.TicksPerSecond, "00:00")]
    public void Countdown_rounds_up_to_whole_minutes_caps_and_stops_at_zero(long remainderTicks, string countdown)
    {
        var state = Current(24, Now + TimeSpan.FromTicks(remainderTicks));

        Assert.Equal($"Claude: 5h 24% in {countdown} | W 5% in 1d 0h", ClaudeSectionFormat.Format(state, Now, Zone).Text);
    }

    [Theory]
    [InlineData(90 * TimeSpan.TicksPerSecond, 30 * TimeSpan.TicksPerSecond)]
    [InlineData(TimeSpan.TicksPerMinute + 1, 1L)]
    [InlineData(300 * TimeSpan.TicksPerMinute, TimeSpan.TicksPerMinute)]
    public void Countdown_changes_when_the_remainder_crosses_the_next_lower_whole_minute(long remainderTicks, long untilTicks)
    {
        var state = Current(24, Now + TimeSpan.FromTicks(remainderTicks));

        var change = Assert.NotNull(ClaudeSectionFormat.UntilCountdownChanges(state, Now));
        Assert.Equal(TimeSpan.FromTicks(untilTicks), change);
        var at = ClaudeSectionFormat.Format(state, Now, Zone).Text;
        Assert.Equal(at, ClaudeSectionFormat.Format(state, Now + change - TimeSpan.FromTicks(1), Zone).Text);
        Assert.NotEqual(at, ClaudeSectionFormat.Format(state, Now + change, Zone).Text);
    }

    [Fact]
    public void Passed_short_reset_keeps_the_weekly_countdown_change()
    {
        Assert.Equal(TimeSpan.FromHours(1), ClaudeSectionFormat.UntilCountdownChanges(Current(24, Now), Now));
    }

    [Fact]
    public void Weekly_reset_updates_before_the_next_short_window_minute()
    {
        var snapshot = new ClaudeLimitSnapshot(
            ClaudeLimitWindow.Create("Current session", 24, Now.AddHours(2))!,
            [ClaudeLimitWindow.Create("Current week (all models)", 20, Now.AddSeconds(20))!]);
        var state = new ClaudeLimitsCurrent(snapshot, Now, 0);

        Assert.Equal("Claude: 5h 24% in 02:00 | W 20% in 0d 1h",
            ClaudeSectionFormat.Format(state, Now, Zone).Text);
        Assert.Equal(TimeSpan.FromSeconds(20), ClaudeSectionFormat.UntilCountdownChanges(state, Now));
        Assert.Equal("Claude: 5h 24% in 02:00 | W 20%",
            ClaudeSectionFormat.Format(state, Now.AddSeconds(20), Zone).Text);
    }

    private static ClaudeLimitsCurrent Current(int shortUsed, DateTimeOffset shortResetsAt) => new(
        new ClaudeLimitSnapshot(ClaudeLimitWindow.Create("Current session", shortUsed, shortResetsAt)!,
            [ClaudeLimitWindow.Create("Current week (all models)", 5, Now.AddDays(1))!]),
        Now, 0);

    [Fact]
    public void Every_displayed_state_fits_the_declared_width_candidates()
    {
        var candidates = ClaudeSectionFormat.WidthCandidates().ToArray();
        var longest = candidates.Max(value => BarWindow.MeasureWidth(value, 1));
        var snapshot = new ClaudeLimitSnapshot(
            ClaudeLimitWindow.Create("Current session", 1, Now.AddHours(5))!,
            [ClaudeLimitWindow.Create("Current week (all models)", 0, null)!]);
        var actual = ClaudeSectionFormat.Format(new ClaudeLimitsCurrent(snapshot, Now, 0), Now, Zone).Text;

        Assert.True(BarWindow.MeasureWidth(actual, 1) <= longest);
    }
}
