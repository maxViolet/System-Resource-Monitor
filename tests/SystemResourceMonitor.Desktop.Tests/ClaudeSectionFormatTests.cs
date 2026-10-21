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
        Assert.Contains("reset time is not shown", section.ToolTip);
        Assert.Null(ClaudeSectionFormat.UntilCountdownChanges(state, Now));
    }

    [Fact]
    public void Active_countdown_rounds_up_and_binding_weekly_window_is_displayed()
    {
        var snapshot = new ClaudeLimitSnapshot(
            ClaudeLimitWindow.Create("Current session", 24, Now.AddMinutes(61).AddSeconds(1))!,
            [ClaudeLimitWindow.Create("Current week (all models)", 5, Now.AddDays(1))!,
             ClaudeLimitWindow.Create("Current week (Fable)", 40, Now.AddDays(2))!]);
        var section = ClaudeSectionFormat.Format(new ClaudeLimitsCurrent(snapshot, Now, 0), Now, Zone);

        Assert.Equal("Claude: 5h 24% in 01:02 | W 40%", section.Text);
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

        Assert.Equal("Claude (stale): 5h 50% in 01:00 | W 20%", stale.Text);
        Assert.Contains("Current month: 10% used", stale.ToolTip);
        Assert.Equal("Claude: 5h -- | W --", unavailable.Text);
        Assert.Contains("unsupported format", unavailable.ToolTip);
    }

    [Theory]
    [InlineData(1L, "00:01")]
    [InlineData(TimeSpan.TicksPerMinute, "00:01")]
    [InlineData(TimeSpan.TicksPerMinute + 1, "00:02")]
    [InlineData(300 * TimeSpan.TicksPerMinute, "05:00")]
    [InlineData(200 * TimeSpan.TicksPerHour, "99:59")]
    [InlineData(0L, "00:00")]
    [InlineData(-TimeSpan.TicksPerSecond, "00:00")]
    public void Countdown_rounds_up_to_whole_minutes_caps_and_stops_at_zero(long remainderTicks, string countdown)
    {
        var state = Current(24, Now + TimeSpan.FromTicks(remainderTicks));

        Assert.Equal($"Claude: 5h 24% in {countdown} | W 5%", ClaudeSectionFormat.Format(state, Now, Zone).Text);
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
    public void Passed_reset_has_no_countdown_change()
    {
        Assert.Null(ClaudeSectionFormat.UntilCountdownChanges(Current(24, Now), Now));
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
