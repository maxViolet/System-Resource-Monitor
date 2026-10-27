using System.Globalization;

namespace SystemResourceMonitor.AIResources.Core;

/// <summary>Formats the binding weekly window for either provider's bar section.</summary>
public static class WeeklyLimitFormat
{
    private static readonly TimeSpan DefaultMaximumAhead = TimeSpan.FromDays(8);

    public static string Format(int? usedPercent, DateTimeOffset? resetsAt, DateTimeOffset now,
        TimeSpan? maximumAhead = null)
    {
        if (usedPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(usedPercent));
        }

        var maximum = maximumAhead ?? DefaultMaximumAhead;
        if (maximum <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAhead));
        }

        if (usedPercent == 0)
        {
            return "W 0%";
        }

        if (usedPercent is null || resetsAt is null)
        {
            return "W --";
        }

        var remaining = resetsAt.Value - now;
        if (remaining > maximum)
        {
            return "W --";
        }

        var prefix = string.Create(CultureInfo.InvariantCulture, $"W {usedPercent}%");
        if (remaining <= TimeSpan.Zero)
        {
            return prefix;
        }

        var wholeHours = (remaining.Ticks + TimeSpan.TicksPerHour - 1) / TimeSpan.TicksPerHour;
        var days = wholeHours / 24;
        var hours = wholeHours % 24;
        return string.Create(CultureInfo.InvariantCulture, $"{prefix} in {days}d {hours}h");
    }

    /// <summary>Time until the displayed whole hour changes; null when there is no running countdown.</summary>
    public static TimeSpan? UntilCountdownChanges(int? usedPercent, DateTimeOffset? resetsAt, DateTimeOffset now,
        TimeSpan? maximumAhead = null)
    {
        if (usedPercent is null or 0 || resetsAt is null)
        {
            return null;
        }

        var remaining = resetsAt.Value - now;
        if (remaining <= TimeSpan.Zero || remaining > (maximumAhead ?? DefaultMaximumAhead))
        {
            return null;
        }

        var wholeHours = (remaining.Ticks + TimeSpan.TicksPerHour - 1) / TimeSpan.TicksPerHour;
        return remaining - TimeSpan.FromHours(wholeHours - 1);
    }
}
