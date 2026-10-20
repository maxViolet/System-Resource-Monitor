using System.Text.Json;

namespace SystemResourceMonitor.AIResources.Core.Codex;

/// <summary>
/// Reads the stdout of one app-server session, one JSON-RPC message per line, until the rate-limit response decides
/// the poll. It validates the client version from the <c>initialize</c> result, skips notifications, and accepts only
/// the response with the rate-limit request ID. Every other shape is <see cref="CodexUnavailableReason.UnknownFormat"/>.
/// The reader keeps no account data: it extracts only used shares and reset times, and never echoes raw output.
/// </summary>
public sealed class CodexResponseReader
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = CodexContract.MaxJsonDepth };

    private int _messages;
    private bool _initialized;

    /// <summary>The decided result, or <see langword="null"/> while more lines are needed.</summary>
    public CodexReadResult? Result { get; private set; }

    /// <summary>
    /// Accepts one stdout line without its terminator and returns the decided result, or <see langword="null"/>
    /// when the line was the <c>initialize</c> result or a notification. A decided reader ignores further lines.
    /// </summary>
    public CodexReadResult? Accept(ReadOnlyMemory<byte> line)
    {
        if (Result is not null)
        {
            return Result;
        }

        if (line.Length > CodexContract.MaxLineBytes || ++_messages > CodexContract.MaxMessages)
        {
            return Result = Unknown;
        }

        try
        {
            using var document = JsonDocument.Parse(line, Options);
            return Result = Read(document.RootElement);
        }
        catch (JsonException)
        {
            return Result = Unknown;
        }
    }

    /// <summary>Ends the session: stdout closed before the rate-limit response decided the poll.</summary>
    public CodexReadResult Complete() => Result ??= Unknown;

    private static CodexReadFailed Unknown => new(CodexUnavailableReason.UnknownFormat);

    private CodexReadResult? Read(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            return Unknown;
        }

        if (!message.TryGetProperty("id", out var id))
        {
            // A notification, for example remoteControl/status/changed or account/updated.
            return message.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String ? null : Unknown;
        }

        if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var requestId))
        {
            return Unknown;
        }

        switch (requestId)
        {
            case CodexContract.InitializeRequestId when !_initialized:
                if (!message.TryGetProperty("result", out var initialize) || initialize.ValueKind != JsonValueKind.Object
                    || !initialize.TryGetProperty("userAgent", out var userAgent) || userAgent.ValueKind != JsonValueKind.String)
                {
                    return Unknown;
                }

                if (!IsQualifiedVersion(userAgent.GetString()!))
                {
                    return new CodexReadFailed(CodexUnavailableReason.UnsupportedVersion);
                }

                _initialized = true;
                return null;
            case CodexContract.RateLimitsRequestId when _initialized:
                if (message.TryGetProperty("error", out var error))
                {
                    return Classify(error);
                }

                return message.TryGetProperty("result", out var result) ? ReadRateLimits(result) : Unknown;
            default:
                return Unknown;
        }
    }

    /// <summary>
    /// The <c>userAgent</c> starts with <c>&lt;originator&gt;/&lt;version&gt; </c>; only the qualified version passes.
    /// </summary>
    private static bool IsQualifiedVersion(string userAgent)
    {
        var product = userAgent.AsSpan();
        var space = product.IndexOf(' ');
        if (space < 0)
        {
            return false;
        }

        product = product[..space];
        var slash = product.LastIndexOf('/');
        return slash > 0 && product[(slash + 1)..].SequenceEqual(CodexContract.QualifiedVersion);
    }

    private static CodexReadFailed Classify(JsonElement error)
    {
        if (error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out var value)
            || !error.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String)
        {
            return Unknown;
        }

        var text = message.GetString()!;
        return value switch
        {
            CodexContract.SignedOutErrorCode when text == CodexContract.SignedOutMessage =>
                new CodexReadFailed(CodexUnavailableReason.SignedOut),
            CodexContract.NetworkFailureErrorCode when text.StartsWith(CodexContract.NetworkFailureMessagePrefix, StringComparison.Ordinal) =>
                new CodexReadFailed(CodexUnavailableReason.NetworkFailed),
            _ => Unknown,
        };
    }

    private static CodexReadResult ReadRateLimits(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("rateLimits", out var snapshot)
            || !IsQualifiedBucket(snapshot)
            || !HasOnlyQualifiedBuckets(result))
        {
            return Unknown;
        }

        CodexLimitWindow? shortWindow = null;
        CodexLimitWindow? weeklyWindow = null;
        foreach (var name in (ReadOnlySpan<string>)["primary", "secondary"])
        {
            if (!snapshot.TryGetProperty(name, out var element) || ReadWindow(element) is not { } read)
            {
                return Unknown;
            }

            var (minutes, window) = read;

            // Windows are identified by duration, not by position.
            switch (minutes)
            {
                case CodexContract.ShortWindowMinutes when shortWindow is null:
                    shortWindow = window;
                    break;
                case CodexContract.WeeklyWindowMinutes when weeklyWindow is null:
                    weeklyWindow = window;
                    break;
                default:
                    return Unknown;
            }
        }

        return new CodexReadSucceeded(new CodexLimitSnapshot(shortWindow!, weeklyWindow!));
    }

    private static bool IsQualifiedBucket(JsonElement snapshot) =>
        snapshot.ValueKind == JsonValueKind.Object
        && snapshot.TryGetProperty("limitId", out var limitId)
        && limitId.ValueKind == JsonValueKind.String
        && limitId.ValueEquals(CodexContract.QualifiedLimitId);

    /// <summary><c>rateLimitsByLimitId</c> is absent, <c>null</c>, or holds the <c>codex</c> bucket only.</summary>
    private static bool HasOnlyQualifiedBuckets(JsonElement result)
    {
        if (!result.TryGetProperty("rateLimitsByLimitId", out var buckets) || buckets.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (buckets.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var count = 0;
        foreach (var bucket in buckets.EnumerateObject())
        {
            if (++count > 1 || !bucket.NameEquals(CodexContract.QualifiedLimitId) || !IsQualifiedBucket(bucket.Value))
            {
                return false;
            }
        }

        return count == 1;
    }

    private static (long Minutes, CodexLimitWindow Window)? ReadWindow(JsonElement window)
    {
        if (window.ValueKind != JsonValueKind.Object
            || !TryGetInteger(window, "usedPercent", out var used) || used is < int.MinValue or > int.MaxValue
            || !TryGetInteger(window, "windowDurationMins", out var minutes)
            || !TryGetInteger(window, "resetsAt", out var resetsAt)
            || resetsAt <= 0 || resetsAt > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return null;
        }

        return CodexLimitWindow.Create((int)used, DateTimeOffset.FromUnixTimeSeconds(resetsAt)) is { } created
            ? (minutes, created)
            : null;
    }

    private static bool TryGetInteger(JsonElement element, string name, out long value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetInt64(out value);
    }
}
