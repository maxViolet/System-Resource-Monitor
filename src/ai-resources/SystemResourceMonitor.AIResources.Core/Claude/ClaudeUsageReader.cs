using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SystemResourceMonitor.AIResources.Core.Claude;

/// <summary>Reads only the displayed subscription windows from the pinned client's /usage JSON envelope.</summary>
public static partial class ClaudeUsageReader
{
    public const int MaxOutputBytes = 64 * 1024;
    private static readonly JsonDocumentOptions JsonOptions = new() { MaxDepth = 8 };

    /// <summary>The latest accepted session reset: the 300-minute window plus the observed one-minute display variation.</summary>
    private static readonly TimeSpan MaxSessionResetAhead = TimeSpan.FromMinutes(ClaudeContract.ShortWindowMinutes + 1);

    [GeneratedRegex(@"^Current (?<kind>session|week \([^\r\n)]{1,60}\)): (?<used>\d{1,3})% used(?: · resets (?<reset>[^\r\n]{1,100}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex WindowLine();

    [GeneratedRegex(@"^(?<date>[A-Z][a-z]{2} \d{1,2}),? (?<time>\d{1,2}(?::\d{2})?[ap]m) \((?<zone>[^()]{1,64})\)$", RegexOptions.CultureInvariant)]
    private static partial Regex ResetLine();

    // Any other labelled share or used/remaining share is an unqualified row, with or without a colon.
    [GeneratedRegex(@"^[A-Za-z][^\r\n:]{1,80}:\s*\d{1,3}%(?:\s|$)|\b\d{1,3}%\s*(?:used|remaining|left)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OtherUsageLine();

    public static ClaudeLimitSnapshot? Read(ReadOnlySpan<byte> utf8, DateTimeOffset observedAt)
    {
        if (utf8.IsEmpty || utf8.Length > MaxOutputBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray(), JsonOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !String(root, "local_command", "usage")
                || !Number(root, "num_turns", 0) || !Number(root, "duration_api_ms", 0)
                || !ZeroCost(root)
                || !Bool(root, "is_error", false) || !root.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var text = result.GetString();
            if (text is null || text.Length > MaxOutputBytes)
            {
                return null;
            }

            ClaudeLimitWindow? shortWindow = null;
            var weekly = new List<ClaudeLimitWindow>();
            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("Current ", StringComparison.Ordinal))
                {
                    if (OtherUsageLine().IsMatch(line)) return null;
                    continue;
                }

                var match = WindowLine().Match(line);
                if (!match.Success || !int.TryParse(match.Groups["used"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var used))
                {
                    return null;
                }

                var kind = match.Groups["kind"].Value;
                var resetText = match.Groups["reset"].Value;
                var reset = resetText.Length == 0 ? null : ParseReset(resetText, observedAt);
                if ((resetText.Length > 0 && reset is null) || (used > 0 && reset is null)
                    || (kind == "session" && reset > observedAt + MaxSessionResetAhead))
                {
                    return null;
                }

                var label = "Current " + kind;
                var window = ClaudeLimitWindow.Create(label, used, reset);
                if (window is null || !labels.Add(label))
                {
                    return null;
                }

                if (kind == "session") shortWindow = window;
                else if (kind.StartsWith("week", StringComparison.Ordinal)) weekly.Add(window);
                else return null;
            }

            return shortWindow is not null && labels.Contains("Current week (all models)") && weekly.Count > 0
                ? new ClaudeLimitSnapshot(shortWindow, weekly)
                : null;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static DateTimeOffset? ParseReset(string text, DateTimeOffset observedAt)
    {
        var match = ResetLine().Match(text);
        if (!match.Success) return null;
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(match.Groups["zone"].Value); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }

        var localText = match.Groups["date"].Value + " " + match.Groups["time"].Value;
        var localNow = TimeZoneInfo.ConvertTime(observedAt, zone);
        foreach (var year in new[] { localNow.Year, localNow.Year + 1 })
        {
            if (!DateTime.TryParseExact(localText + " " + year,
                    ["MMM d htt yyyy", "MMM d h:mmtt yyyy"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var local)) continue;
            local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) return null;
            var utc = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
            if (utc >= observedAt.AddMinutes(-2) && utc <= observedAt.AddDays(8)) return utc;
        }

        return null;
    }

    private static bool String(JsonElement root, string property, string expected) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() == expected;

    private static bool Number(JsonElement root, string property, int expected) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number) && number == expected;

    private static bool Bool(JsonElement root, string property, bool expected) =>
        root.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean() == expected;

    private static bool ZeroCost(JsonElement root) =>
        root.TryGetProperty("total_cost_usd", out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var cost) && cost == 0;
}
