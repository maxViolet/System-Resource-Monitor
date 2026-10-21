using System.Runtime.ExceptionServices;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class ClaudeSectionWidthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Every_formatted_text_fits_the_section_width(double pixelsPerDip)
    {
        var texts = FormattedTexts();

        var clipped = RunOnSta(() =>
        {
            var width = BarWindow.WidestCandidate(ClaudeSectionFormat.WidthCandidates(), pixelsPerDip);
            return texts.Where(text => BarWindow.MeasureWidth(text, pixelsPerDip) > width).ToList();
        });

        Assert.Empty(clipped);
    }

    /// <summary>
    /// Current and stale states for every short share with the widest and narrowest countdowns, every weekly share
    /// with and without a qualified monthly window, and every unavailable reason.
    /// </summary>
    private static HashSet<string> FormattedTexts()
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var countdown in (TimeSpan[])[TimeSpan.FromMinutes(299), TimeSpan.FromSeconds(30)])
        {
            for (var shortUsed = 0; shortUsed <= 100; shortUsed++)
            {
                for (var otherUsed = 0; otherUsed <= 100; otherUsed++)
                {
                    Add(texts, Snapshot(shortUsed, countdown, otherUsed, monthlyUsed: null));
                    Add(texts, Snapshot(shortUsed, countdown, 100, otherUsed));
                }
            }
        }

        foreach (var reason in Enum.GetValues<ClaudeUnavailableReason>())
        {
            texts.Add(ClaudeSectionFormat.Format(new ClaudeLimitsUnavailable(reason), Now, TimeZoneInfo.Utc).Text);
        }

        Assert.Contains("Claude: 5h 100% in 04:59 | W 100%", texts);
        Assert.Contains("Claude (stale): 5h 0% | W 0%", texts);
        return texts;
    }

    private static void Add(HashSet<string> texts, ClaudeLimitSnapshot snapshot)
    {
        texts.Add(ClaudeSectionFormat.Format(new ClaudeLimitsCurrent(snapshot, Now, 0), Now, TimeZoneInfo.Utc).Text);
        texts.Add(ClaudeSectionFormat.Format(new ClaudeLimitsStale(snapshot, Now, 0), Now, TimeZoneInfo.Utc).Text);
    }

    private static ClaudeLimitSnapshot Snapshot(int shortUsed, TimeSpan countdown, int weeklyUsed, int? monthlyUsed) => new(
        ClaudeLimitWindow.Create("Current session", shortUsed, Now + countdown)!,
        [ClaudeLimitWindow.Create("Current week (all models)", weeklyUsed, Now + TimeSpan.FromDays(6))!],
        monthlyUsed is { } used ? ClaudeLimitWindow.Create("Current month", used, Now + TimeSpan.FromDays(20)) : null);

    /// <summary>WPF text measurement runs on a dedicated STA thread.</summary>
    private static T RunOnSta<T>(Func<T> action)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
        return result;
    }
}
