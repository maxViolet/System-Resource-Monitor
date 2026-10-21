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

        Assert.Equal("Claude: [5h] 0% | [W] 0% | month: TODO", section.Text);
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

        Assert.Equal("Claude: [5h] 24% in 01:02 | [W] 40% | month: TODO", section.Text);
        Assert.Contains("Current week (all models)", section.ToolTip);
        Assert.Contains("Current week (Fable)", section.ToolTip);
    }

    [Fact]
    public void Qualified_monthly_value_and_stale_and_unavailable_states_are_distinct()
    {
        var snapshot = new ClaudeLimitSnapshot(
            ClaudeLimitWindow.Create("Current session", 50, Now.AddHours(1))!,
            [ClaudeLimitWindow.Create("Current week (all models)", 20, Now.AddDays(1))!],
            ClaudeLimitWindow.Create("Current month", 10, Now.AddDays(7))!);
        var stale = ClaudeSectionFormat.Format(new ClaudeLimitsStale(snapshot, Now, 0), Now, Zone);
        var unavailable = ClaudeSectionFormat.Format(
            new ClaudeLimitsUnavailable(ClaudeUnavailableReason.UnknownFormat), Now, Zone);

        Assert.Contains("month: 10%", stale.Text);
        Assert.Contains("Current month", stale.ToolTip);
        Assert.DoesNotContain("no qualified Claude monthly window", stale.ToolTip);
        Assert.StartsWith("Claude (stale):", stale.Text);
        Assert.Equal("Claude: [5h] -- | [W] -- | month: TODO", unavailable.Text);
        Assert.Contains("unsupported format", unavailable.ToolTip);
    }

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
