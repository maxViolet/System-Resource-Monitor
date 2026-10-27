using SystemResourceMonitor.AIResources.Core;

namespace SystemResourceMonitor.Core.Tests;

public sealed class WeeklyLimitFormatTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, 12, 0, 0, "W 33% in 01 12")]
    [InlineData(1, 12, 20, 0, "W 33% in 01 13")]
    [InlineData(0, 0, 0, 1, "W 33% in 00 01")]
    [InlineData(0, 23, 1, 0, "W 33% in 01 00")]
    [InlineData(6, 23, 1, 0, "W 33% in 07 00")]
    public void Positive_time_rounds_up_before_splitting_days_and_hours(
        int days, int hours, int minutes, int seconds, string expected)
    {
        var reset = Now.AddDays(days).AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);

        Assert.Equal(expected, WeeklyLimitFormat.Format(33, reset, Now));
    }

    [Fact]
    public void Countdown_changes_between_provider_polls()
    {
        var reset = Now.AddDays(1).AddHours(12).AddMinutes(20);

        Assert.Equal("W 33% in 01 13", WeeklyLimitFormat.Format(33, reset, Now));
        Assert.Equal("W 33% in 01 12", WeeklyLimitFormat.Format(33, reset, Now.AddMinutes(20)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void At_or_after_reset_omits_the_countdown(int hoursAfterReset)
    {
        Assert.Equal("W 33%", WeeklyLimitFormat.Format(33, Now.AddHours(hoursAfterReset), Now));
    }

    [Fact]
    public void Unknown_weekly_value_or_reset_is_unavailable()
    {
        Assert.Equal("W --", WeeklyLimitFormat.Format(null, Now.AddDays(1), Now));
        Assert.Equal("W --", WeeklyLimitFormat.Format(33, null, Now));
        Assert.Equal("W 0%", WeeklyLimitFormat.Format(0, null, Now));
    }

    [Theory]
    [InlineData(0, "W 0%")]
    [InlineData(100, "W 100% in 00 01")]
    public void Boundary_percentages_still_show_the_weekly_reset(int percent, string expected)
    {
        Assert.Equal(expected, WeeklyLimitFormat.Format(percent, Now.AddSeconds(1), Now));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Percentage_outside_the_qualified_range_is_rejected(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WeeklyLimitFormat.Format(percent, Now.AddDays(1), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => WeeklyLimitFormat.Format(percent, null, Now));
    }

    [Theory]
    [InlineData(1L, 1L)]
    [InlineData(TimeSpan.TicksPerHour, TimeSpan.TicksPerHour)]
    [InlineData(TimeSpan.TicksPerHour + 1, 1L)]
    [InlineData(36 * TimeSpan.TicksPerHour, TimeSpan.TicksPerHour)]
    [InlineData(36 * TimeSpan.TicksPerHour + TimeSpan.TicksPerMinute, TimeSpan.TicksPerMinute)]
    public void Next_change_is_the_next_lower_whole_hour(long remainingTicks, long expectedTicks)
    {
        var reset = Now + TimeSpan.FromTicks(remainingTicks);
        var until = Assert.NotNull(WeeklyLimitFormat.UntilCountdownChanges(33, reset, Now));

        Assert.Equal(TimeSpan.FromTicks(expectedTicks), until);
        Assert.Equal(WeeklyLimitFormat.Format(33, reset, Now),
            WeeklyLimitFormat.Format(33, reset, Now + until - TimeSpan.FromTicks(1)));
        Assert.NotEqual(WeeklyLimitFormat.Format(33, reset, Now),
            WeeklyLimitFormat.Format(33, reset, Now + until));
    }

    [Fact]
    public void Missing_or_passed_reset_has_no_pending_change()
    {
        Assert.Null(WeeklyLimitFormat.UntilCountdownChanges(33, null, Now));
        Assert.Null(WeeklyLimitFormat.UntilCountdownChanges(33, Now, Now));
        Assert.Null(WeeklyLimitFormat.UntilCountdownChanges(33, Now.AddSeconds(-1), Now));
        Assert.Null(WeeklyLimitFormat.UntilCountdownChanges(0, Now.AddDays(1), Now));
    }

    [Fact]
    public void Reset_beyond_the_qualified_window_is_unavailable()
    {
        var max = TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1));
        var reset = Now + max + TimeSpan.FromTicks(1);

        Assert.Equal("W --", WeeklyLimitFormat.Format(33, reset, Now, max));
        Assert.Null(WeeklyLimitFormat.UntilCountdownChanges(33, reset, Now, max));
        Assert.Equal("W 33% in 07 01", WeeklyLimitFormat.Format(33, Now + max, Now, max));
    }
}
