using System.Runtime.ExceptionServices;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class CodexSectionWidthTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Every_formatted_text_fits_the_section_width(double pixelsPerDip)
    {
        var texts = FormattedTexts();

        var clipped = RunOnSta(() =>
        {
            var width = BarWindow.WidestCandidate(CodexSectionFormat.WidthCandidates(), pixelsPerDip);
            return texts.Where(text => BarWindow.MeasureWidth(text, pixelsPerDip) > width).ToList();
        });

        Assert.Empty(clipped);
    }

    /// <summary>
    /// Current and stale states for every short and weekly share with the widest and narrowest countdowns, the unused
    /// short window, and every unavailable reason.
    /// </summary>
    private static HashSet<string> FormattedTexts()
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var countdown in (TimeSpan[])[TimeSpan.FromMinutes(299), TimeSpan.FromSeconds(30)])
        {
            for (var shortUsed = 0; shortUsed <= 100; shortUsed++)
            {
                for (var weeklyUsed = 0; weeklyUsed <= 100; weeklyUsed++)
                {
                    var snapshot = new CodexLimitSnapshot(
                        CodexLimitWindow.Create(shortUsed, Now + countdown)!,
                        CodexLimitWindow.Create(weeklyUsed, Now + TimeSpan.FromDays(6))!);
                    texts.Add(CodexSectionFormat.Format(new CodexLimitsCurrent(snapshot, Now, 0), Now, TimeZoneInfo.Utc).Text);
                    texts.Add(CodexSectionFormat.Format(
                        new CodexLimitsStale(snapshot, Now, 0), Now, TimeZoneInfo.Utc).Text);
                }
            }
        }

        foreach (var reason in Enum.GetValues<CodexUnavailableReason>())
        {
            texts.Add(CodexSectionFormat.Format(new CodexLimitsUnavailable(reason), Now, TimeZoneInfo.Utc).Text);
        }

        Assert.Contains("Codex: 5h 99% in 04:59 | W 0%", texts);
        Assert.Contains("Codex (stale): 5h 0% | W 100%", texts);
        return texts;
    }

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
