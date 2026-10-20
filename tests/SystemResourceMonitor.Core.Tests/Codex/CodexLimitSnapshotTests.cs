using SystemResourceMonitor.AIResources.Core.Codex;

namespace SystemResourceMonitor.Core.Tests.Codex;

public sealed class CodexLimitSnapshotTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Window_outside_zero_through_one_hundred_is_not_created(int used)
    {
        Assert.Null(CodexLimitWindow.Create(used, CodexSamples.ShortReset));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(27, 73)]
    [InlineData(100, 0)]
    public void Remaining_limit_is_one_hundred_minus_the_used_share(int used, int remaining)
    {
        var window = Assert.IsType<CodexLimitWindow>(CodexLimitWindow.Create(used, CodexSamples.ShortReset));

        Assert.Equal(used, window.UsedPercent);
        Assert.Equal(remaining, window.RemainingPercent);
    }

    [Fact]
    public void Reset_time_is_kept_as_the_same_instant_in_utc()
    {
        var local = new DateTimeOffset(2026, 10, 20, 14, 30, 0, TimeSpan.FromHours(2));

        var window = CodexLimitWindow.Create(5, local)!;

        Assert.Equal(TimeSpan.Zero, window.ResetsAt.Offset);
        Assert.Equal(local.UtcDateTime, window.ResetsAt.UtcDateTime);
    }

    [Theory]
    [InlineData(CodexUnavailableReason.NotYetSampled)]
    [InlineData(CodexUnavailableReason.Expired)]
    [InlineData((CodexUnavailableReason)999)]
    public void Source_cannot_report_a_monitor_only_or_undefined_reason(CodexUnavailableReason reason)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CodexReadFailed(reason));
    }
}
