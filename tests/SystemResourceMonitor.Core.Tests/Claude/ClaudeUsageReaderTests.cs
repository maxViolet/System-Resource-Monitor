using System.Text;
using System.Text.Json;
using SystemResourceMonitor.AIResources.Core.Claude;

namespace SystemResourceMonitor.Core.Tests.Claude;

public sealed class ClaudeUsageReaderTests
{
    private static readonly DateTimeOffset Observed = new(2026, 10, 21, 10, 0, 0, TimeSpan.FromHours(5));

    [Fact]
    public void Unused_short_window_can_omit_reset_while_active_weekly_window_has_one()
    {
        var snapshot = ClaudeUsageReader.Read(Envelope("""
            Current session: 0% used
            Current week (all models): 5% used · resets Oct 22, 3am (Asia/Qyzylorda)
            """), Observed);

        Assert.NotNull(snapshot);
        Assert.Equal(100, snapshot.ShortWindow.RemainingPercent);
        Assert.Null(snapshot.ShortWindow.ResetsAt);
        Assert.Equal(95, snapshot.BindingWeeklyWindow.RemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 22, 0, 0, TimeSpan.Zero),
            snapshot.BindingWeeklyWindow.ResetsAt);
    }

    [Fact]
    public void All_weekly_rows_are_retained_and_lowest_remaining_is_binding()
    {
        var snapshot = ClaudeUsageReader.Read(Envelope("""
            Current session: 25% used · resets Oct 21, 12pm (Asia/Qyzylorda)
            Current week (all models): 5% used · resets Oct 22, 3am (Asia/Qyzylorda)
            Current week (Fable): 40% used · resets Oct 23, 3am (Asia/Qyzylorda)
            """), Observed);

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot.WeeklyWindows.Count);
        Assert.Equal("Current week (Fable)", snapshot.BindingWeeklyWindow.Label);
        Assert.Equal(60, snapshot.BindingWeeklyWindow.RemainingPercent);
    }

    [Theory]
    [InlineData("Current session: 1% used\nCurrent week (all models): 0% used")]
    [InlineData("Current session: 100% used\nCurrent week (all models): 0% used")]
    [InlineData("Current session: 1% used · resets unknown\nCurrent week (all models): 0% used")]
    [InlineData("Current session: 1% used · resets Oct 22, 3am (Not/AZone)\nCurrent week (all models): 0% used")]
    [InlineData("Current session: 0% used\nCurrent week (all models): 101% used")]
    [InlineData("Current session: 0% used\nCurrent week (all models): 0% used\nCurrent week (all models): 0% used")]
    [InlineData("Current session: 0% used\nCurrent week (all models): 0% used\nCurrent year: 0% used")]
    [InlineData("Current session: 0% used\nCurrent week (all models): 0% used\nCurrent month: 0% used")]
    [InlineData("Current session: 0% used\nCurrent week (all models): 0% used\nMonthly limit: 40% used")]
    [InlineData("Current session: 0% used\nCurrent week (all models): 0% used\nMonthly limit: 60% remaining")]
    [InlineData("Current session: 0% used\nCurrent week (Fable): 0% used")]
    public void Missing_active_reset_and_changed_rows_are_unavailable(string result)
    {
        Assert.Null(ClaudeUsageReader.Read(Envelope(result), Observed));
    }

    [Fact]
    public void Model_turn_is_never_accepted_as_a_usage_poll()
    {
        var json = JsonSerializer.Serialize(new
        {
            local_command = "usage", num_turns = 1, duration_api_ms = 1, total_cost_usd = 0, is_error = false,
            result = "Current session: 0% used\nCurrent week (all models): 0% used"
        });
        Assert.Null(ClaudeUsageReader.Read(Encoding.UTF8.GetBytes(json), Observed));
    }

    [Fact]
    public void Charged_result_is_not_accepted_even_if_it_reports_zero_turns()
    {
        var json = JsonSerializer.Serialize(new
        {
            local_command = "usage", num_turns = 0, duration_api_ms = 0,
            total_cost_usd = 0.01, is_error = false,
            result = "Current session: 0% used\nCurrent week (all models): 0% used",
        });
        Assert.Null(ClaudeUsageReader.Read(Encoding.UTF8.GetBytes(json), Observed));
    }

    [Fact]
    public void Error_envelope_is_not_a_usage_snapshot()
    {
        var json = JsonSerializer.Serialize(new
        {
            local_command = "usage", num_turns = 0, duration_api_ms = 0,
            total_cost_usd = 0, is_error = true,
            result = "Current session: 0% used\nCurrent week (all models): 0% used",
        });
        Assert.Null(ClaudeUsageReader.Read(Encoding.UTF8.GetBytes(json), Observed));
    }

    private static byte[] Envelope(string result) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        local_command = "usage", num_turns = 0, duration_api_ms = 0, total_cost_usd = 0, is_error = false, result,
    }));
}
