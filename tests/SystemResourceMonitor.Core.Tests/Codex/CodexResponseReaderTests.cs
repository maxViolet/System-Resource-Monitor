using System.Text;
using SystemResourceMonitor.AIResources.Core.Codex;

namespace SystemResourceMonitor.Core.Tests.Codex;

/// <summary>Feeds synthetic, sanitized app-server stdout lines to the reader, one UTF-8 line per call.</summary>
public sealed class CodexResponseReaderTests
{
    private const string Initialize =
        """{"id":1,"result":{"userAgent":"codex_vscode/0.159.0 (Windows 10.0.26300; x86_64) unknown (srm; 1)","codexHome":"C:/synthetic/.codex","platformFamily":"windows","platformOs":"windows"}}""";

    private const string Notification = """{"jsonrpc":"2.0","method":"remoteControl/status/changed","params":{"status":"disabled"}}""";
    private const string AccountUpdated = """{"jsonrpc":"2.0","method":"account/updated","params":{"authMode":"chatgpt","planType":"plus"}}""";

    private const string Short = """{"usedPercent":27,"windowDurationMins":300,"resetsAt":1792500924}""";
    private const string Weekly = """{"usedPercent":4,"windowDurationMins":10080,"resetsAt":1793087724}""";

    private const string SignedOut =
        """{"id":2,"error":{"code":-32600,"message":"codex account authentication required to read rate limits"}}""";

    private const string NetworkFailed =
        """{"id":2,"error":{"code":-32603,"message":"failed to fetch codex rate limits: error sending request for url (https://synthetic.invalid/usage)"}}""";

    private static readonly string Qualified = Response(Bucket(Short, Weekly));

    /// <summary>Rate-limit responses that arrive after a valid initialize result and are outside the contract.</summary>
    private static readonly Dictionary<string, string> UnknownResponses = new()
    {
        ["missing secondary"] = Response(BucketWithout("secondary", Short)),
        ["missing primary"] = Response(BucketWithout("primary", Weekly)),
        ["null secondary"] = Response(Bucket(Short, "null")),
        ["null resetsAt"] = Response(Bucket(Window("27", "300", "null"), Weekly)),
        ["missing resetsAt"] = Response(Bucket("""{"usedPercent":27,"windowDurationMins":300}""", Weekly)),
        ["zero resetsAt"] = Response(Bucket(Window("27", "300", "0"), Weekly)),
        ["null windowDurationMins"] = Response(Bucket(Window("27", "null", "1792500924"), Weekly)),
        ["missing windowDurationMins"] = Response(Bucket("""{"usedPercent":27,"resetsAt":1792500924}""", Weekly)),
        ["other windowDurationMins"] = Response(Bucket(Window("27", "60", "1792500924"), Weekly)),
        ["two short windows"] = Response(Bucket(Short, Short)),
        ["two weekly windows"] = Response(Bucket(Weekly, Weekly)),
        ["usedPercent 101"] = Response(Bucket(Window("101", "300", "1792500924"), Weekly)),
        ["usedPercent -1"] = Response(Bucket(Short, Window("-1", "10080", "1793087724"))),
        ["usedPercent 27.5"] = Response(Bucket(Window("27.5", "300", "1792500924"), Weekly)),
        ["usedPercent string"] = Response(Bucket(Window("\"27\"", "300", "1792500924"), Weekly)),
        ["usedPercent null"] = Response(Bucket(Window("null", "300", "1792500924"), Weekly)),
        ["other limitId"] = Response(Bucket(Short, Weekly, "\"codex_other\"")),
        ["null limitId"] = Response(Bucket(Short, Weekly, "null")),
        ["extra bucket by limit id"] = Response(
            Bucket(Short, Weekly), $$"""{"codex":{{Bucket(Short, Weekly)}},"codex_other":{{Bucket(Short, Weekly, "\"codex_other\"")}}}"""),
        ["only other bucket by limit id"] = Response(Bucket(Short, Weekly), $$"""{"codex_other":{{Bucket(Short, Weekly)}}}"""),
        ["empty buckets by limit id"] = Response(Bucket(Short, Weekly), "{}"),
        ["bucket by limit id with other limitId"] = Response(Bucket(Short, Weekly), $$"""{"codex":{{Bucket(Short, Weekly, "\"codex_other\"")}}}"""),
        ["buckets by limit id as array"] = Response(Bucket(Short, Weekly), "[]"),
        ["missing rateLimits"] = """{"id":2,"result":{"rateLimitsByLimitId":null}}""",
        ["null result"] = """{"id":2,"result":null}""",
        ["neither result nor error"] = """{"id":2}""",
        ["unexpected id 3"] = """{"id":3,"result":{}}""",
        ["string id"] = """{"id":"2","result":{}}""",
        ["fractional id"] = """{"id":2.5,"result":{}}""",
        ["repeated initialize result"] = Initialize,
        ["message without id or method"] = """{"jsonrpc":"2.0","params":{}}""",
        ["method that is not a string"] = """{"jsonrpc":"2.0","method":7}""",
        ["error with other code"] = """{"id":2,"error":{"code":-32601,"message":"codex account authentication required to read rate limits"}}""",
        ["signed-out code with other message"] = """{"id":2,"error":{"code":-32600,"message":"codex account authentication required"}}""",
        ["signed-out message with suffix"] = """{"id":2,"error":{"code":-32600,"message":"codex account authentication required to read rate limits."}}""",
        ["network code with other message"] = """{"id":2,"error":{"code":-32603,"message":"internal error"}}""",
        ["network prefix with other code"] = """{"id":2,"error":{"code":-32600,"message":"failed to fetch codex rate limits: timeout"}}""",
        ["error without message"] = """{"id":2,"error":{"code":-32600}}""",
        ["error as string"] = """{"id":2,"error":"codex account authentication required to read rate limits"}""",
        ["malformed JSON"] = """{"id":2,"result":""",
        ["array root"] = "[]",
        ["number root"] = "42",
        ["string root"] = "\"codex\"",
        ["null root"] = "null",
        ["empty line"] = "",
    };

    public static TheoryData<string> UnknownResponseNames => new(UnknownResponses.Keys);

    [Fact]
    public void Qualified_response_reports_both_windows_with_used_share_remaining_limit_and_utc_reset()
    {
        var snapshot = AssertSucceeded(Decide(Initialize, Qualified));

        Assert.Equal(27, snapshot.ShortWindow.UsedPercent);
        Assert.Equal(73, snapshot.ShortWindow.RemainingPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792500924), snapshot.ShortWindow.ResetsAt);
        Assert.Equal(TimeSpan.Zero, snapshot.ShortWindow.ResetsAt.Offset);
        Assert.Equal(4, snapshot.WeeklyWindow.UsedPercent);
        Assert.Equal(96, snapshot.WeeklyWindow.RemainingPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793087724), snapshot.WeeklyWindow.ResetsAt);
        Assert.Equal(TimeSpan.Zero, snapshot.WeeklyWindow.ResetsAt.Offset);
    }

    [Fact]
    public void Windows_are_identified_by_duration_not_by_position()
    {
        var snapshot = AssertSucceeded(Decide(Initialize, Response(Bucket(Weekly, Short))));

        Assert.Equal(27, snapshot.ShortWindow.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792500924), snapshot.ShortWindow.ResetsAt);
        Assert.Equal(4, snapshot.WeeklyWindow.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793087724), snapshot.WeeklyWindow.ResetsAt);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    public void Used_share_at_either_end_of_the_range_is_accepted(int used, int remaining)
    {
        var shortWindow = Window(used.ToString(System.Globalization.CultureInfo.InvariantCulture), "300", "1792500924");

        var snapshot = AssertSucceeded(Decide(Initialize, Response(Bucket(shortWindow, Weekly))));

        Assert.Equal(used, snapshot.ShortWindow.UsedPercent);
        Assert.Equal(remaining, snapshot.ShortWindow.RemainingPercent);
    }

    [Fact]
    public void Notifications_before_and_after_the_initialize_result_are_skipped()
    {
        var snapshot = AssertSucceeded(Decide(Notification, Initialize, Notification, AccountUpdated, Qualified));

        Assert.Equal(27, snapshot.ShortWindow.UsedPercent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Absent_or_null_buckets_by_limit_id_are_accepted(bool present)
    {
        var response = Response(Bucket(Short, Weekly), present ? "null" : null);

        var snapshot = AssertSucceeded(Decide(Initialize, response));

        Assert.Equal(4, snapshot.WeeklyWindow.UsedPercent);
    }

    [Theory]
    [MemberData(nameof(UnknownResponseNames))]
    public void Response_outside_the_contract_is_unknown_format(string name)
    {
        AssertReason(CodexUnavailableReason.UnknownFormat, Decide(Initialize, UnknownResponses[name]));
    }

    [Theory]
    [InlineData(SignedOut)]
    [InlineData(NetworkFailed)]
    [InlineData("""{"id":2,"result":{}}""")]
    public void Rate_limit_response_before_the_initialize_result_is_unknown_format(string response)
    {
        AssertReason(CodexUnavailableReason.UnknownFormat, Decide(Notification, response));
    }

    [Theory]
    [InlineData("""{"id":1,"result":{"codexHome":"C:/synthetic/.codex"}}""")]
    [InlineData("""{"id":1,"result":{"userAgent":null}}""")]
    [InlineData("""{"id":1,"result":{"userAgent":159}}""")]
    [InlineData("""{"id":1,"result":null}""")]
    [InlineData("""{"id":1,"error":{"code":-32600,"message":"not initialized"}}""")]
    public void Initialize_result_without_a_string_user_agent_is_unknown_format(string initialize)
    {
        AssertReason(CodexUnavailableReason.UnknownFormat, Decide(initialize));
    }

    [Theory]
    [InlineData("codex_vscode/0.160.0 (Windows 10.0.26300; x86_64) unknown")]
    [InlineData("codex_vscode/0.158.0 (Windows 10.0.26300; x86_64) unknown")]
    [InlineData("codex_vscode/0.159.01 (Windows 10.0.26300; x86_64) unknown")]
    [InlineData("codex_vscode/0.159.0-alpha.1 (Windows 10.0.26300; x86_64) unknown")]
    [InlineData("codex_vscode/0.159.0(Windows 10.0.26300; x86_64) unknown")]
    [InlineData("codex_vscode/0.159.0")]
    [InlineData("/0.159.0 (Windows 10.0.26300; x86_64) unknown")]
    [InlineData("0.159.0 (Windows 10.0.26300; x86_64) unknown")]
    [InlineData("")]
    public void Initialize_result_from_another_client_version_is_unsupported_version(string userAgent)
    {
        AssertReason(CodexUnavailableReason.UnsupportedVersion, Decide(InitializeWith(userAgent)));
    }

    [Theory]
    [InlineData("codex_vscode/0.159.0 (Windows 10.0.26300; x86_64) unknown")]
    [InlineData("system-resource-monitor/0.159.0 (Windows 10.0.26300; x86_64) unknown")]
    [InlineData("codex_cli_rs/0.159.0 x")]
    [InlineData("vendor/tool/0.159.0 (Windows 10.0.26300; x86_64)")]
    public void Originator_name_does_not_affect_the_version_check(string userAgent)
    {
        AssertSucceeded(Decide(InitializeWith(userAgent), Qualified));
    }

    [Fact]
    public void Exact_signed_out_error_is_signed_out()
    {
        AssertReason(CodexUnavailableReason.SignedOut, Decide(Initialize, SignedOut));
    }

    [Theory]
    [InlineData(NetworkFailed)]
    [InlineData("""{"id":2,"error":{"code":-32603,"message":"failed to fetch codex rate limits:"}}""")]
    public void Network_error_with_the_qualified_prefix_is_network_failed(string response)
    {
        AssertReason(CodexUnavailableReason.NetworkFailed, Decide(Initialize, response));
    }

    [Fact]
    public void Completing_before_any_response_is_unknown_format()
    {
        var reader = new CodexResponseReader();

        AssertReason(CodexUnavailableReason.UnknownFormat, reader.Complete());
    }

    [Fact]
    public void Completing_after_only_the_initialize_result_and_notifications_is_unknown_format()
    {
        var reader = new CodexResponseReader();
        Assert.Null(Accept(reader, Initialize));
        Assert.Null(Accept(reader, Notification));

        AssertReason(CodexUnavailableReason.UnknownFormat, reader.Complete());
    }

    [Theory]
    [InlineData(SignedOut)]
    [InlineData("[]")]
    public void Decided_reader_returns_the_same_result_for_later_lines_and_completion(string decidedBy)
    {
        var reader = new CodexResponseReader();
        Assert.Null(Accept(reader, Initialize));
        var decided = Accept(reader, decidedBy);
        Assert.NotNull(decided);

        Assert.Same(decided, Accept(reader, Qualified));
        Assert.Same(decided, Accept(reader, "not json"));
        Assert.Same(decided, reader.Complete());
        Assert.Same(decided, reader.Result);
    }

    [Fact]
    public void Successful_reader_ignores_a_later_error_line()
    {
        var reader = new CodexResponseReader();
        Assert.Null(Accept(reader, Initialize));
        var decided = AssertSucceededResult(Accept(reader, Qualified));

        Assert.Same(decided, Accept(reader, SignedOut));
        Assert.Same(decided, reader.Complete());
    }

    [Fact]
    public void Unsupported_version_decides_the_reader_before_the_rate_limit_response()
    {
        var reader = new CodexResponseReader();
        var decided = Accept(reader, InitializeWith("codex_vscode/0.160.0 (Windows 10.0.26300; x86_64)"));

        AssertReason(CodexUnavailableReason.UnsupportedVersion, decided);
        Assert.Same(decided, Accept(reader, Qualified));
    }

    [Fact]
    public void Line_at_the_byte_limit_is_read_and_one_byte_longer_is_unknown_format()
    {
        var atLimit = PaddedNotification(CodexContract.MaxLineBytes);
        var overLimit = PaddedNotification(CodexContract.MaxLineBytes + 1);

        var reader = new CodexResponseReader();
        Assert.Null(Accept(reader, Initialize));
        Assert.Null(Accept(reader, atLimit));
        AssertSucceeded(Accept(reader, Qualified));

        AssertReason(CodexUnavailableReason.UnknownFormat, Decide(Initialize, overLimit));
    }

    [Fact]
    public void Message_limit_counts_every_line_including_the_response()
    {
        var withinLimit = new List<string> { Initialize };
        withinLimit.AddRange(Enumerable.Repeat(Notification, CodexContract.MaxMessages - 2));
        withinLimit.Add(Qualified);

        AssertSucceeded(Decide([.. withinLimit]));
    }

    [Fact]
    public void Message_beyond_the_limit_is_unknown_format_even_when_it_is_a_notification()
    {
        var reader = new CodexResponseReader();
        Assert.Null(Accept(reader, Initialize));
        for (var message = 2; message <= CodexContract.MaxMessages; message++)
        {
            Assert.Null(Accept(reader, Notification));
        }

        AssertReason(CodexUnavailableReason.UnknownFormat, Accept(reader, Notification));
        Assert.Same(reader.Result, Accept(reader, Qualified));
    }

    [Fact]
    public void Nesting_at_the_depth_limit_is_read_and_one_level_deeper_is_unknown_format()
    {
        // The notification object is one level; its params array adds the rest.
        var atLimit = NestedNotification(CodexContract.MaxJsonDepth - 1);
        var overLimit = NestedNotification(CodexContract.MaxJsonDepth);

        var reader = new CodexResponseReader();
        Assert.Null(Accept(reader, Initialize));
        Assert.Null(Accept(reader, atLimit));
        AssertSucceeded(Accept(reader, Qualified));

        AssertReason(CodexUnavailableReason.UnknownFormat, Decide(Initialize, overLimit));
    }

    private static string Window(string used, string minutes, string resetsAt) =>
        $$"""{"usedPercent":{{used}},"windowDurationMins":{{minutes}},"resetsAt":{{resetsAt}}}""";

    private static string Bucket(string primary, string secondary, string limitId = "\"codex\"") =>
        $$"""{"limitId":{{limitId}},"limitName":null,"normalModelSlug":null,"primary":{{primary}},"secondary":{{secondary}},"credits":{"hasCredits":false,"unlimited":false,"balance":"0"},"individualLimit":null,"spendControlReached":false,"planType":"plus","rateLimitReachedType":null}""";

    private static string BucketWithout(string missing, string present) =>
        $$"""{"limitId":"codex","limitName":null,"{{(missing == "primary" ? "secondary" : "primary")}}":{{present}},"credits":null,"planType":"plus"}""";

    /// <summary>A rate-limit response whose buckets by limit ID hold the same codex bucket.</summary>
    private static string Response(string bucket) => Response(bucket, $$"""{"codex":{{bucket}}}""");

    /// <summary>A rate-limit response; <paramref name="byLimitId"/> is omitted when null.</summary>
    private static string Response(string bucket, string? byLimitId) => byLimitId is null
        ? $$$"""{"id":2,"result":{"ordinaryUsageAllowed":true,"rateLimits":{{{bucket}}},"rateLimitResetCredits":null,"rateLimitUpsell":null}}"""
        : $$$"""{"id":2,"result":{"ordinaryUsageAllowed":true,"rateLimits":{{{bucket}}},"rateLimitsByLimitId":{{{byLimitId}}},"rateLimitResetCredits":null,"rateLimitUpsell":null}}""";

    private static string InitializeWith(string userAgent) =>
        $$$"""{"id":1,"result":{"userAgent":"{{{userAgent}}}","platformFamily":"windows","platformOs":"windows"}}""";

    private static string PaddedNotification(int bytes)
    {
        const string Prefix = """{"method":"n","params":" """;
        const string Suffix = "\"}";
        return Prefix + new string('x', bytes - Prefix.Length - Suffix.Length) + Suffix;
    }

    private static string NestedNotification(int arrays) =>
        """{"method":"n","params":""" + new string('[', arrays) + new string(']', arrays) + "}";

    private static CodexReadResult? Accept(CodexResponseReader reader, string line) => reader.Accept(Encoding.UTF8.GetBytes(line));

    /// <summary>Feeds every line to a new reader; every line but the last must leave the poll undecided.</summary>
    private static CodexReadResult? Decide(params string[] lines)
    {
        var reader = new CodexResponseReader();
        foreach (var line in lines[..^1])
        {
            Assert.Null(Accept(reader, line));
        }

        return Accept(reader, lines[^1]);
    }

    private static CodexLimitSnapshot AssertSucceeded(CodexReadResult? result) => AssertSucceededResult(result).Snapshot;

    private static CodexReadSucceeded AssertSucceededResult(CodexReadResult? result) => Assert.IsType<CodexReadSucceeded>(result);

    private static void AssertReason(CodexUnavailableReason expected, CodexReadResult? result) =>
        Assert.Equal(expected, Assert.IsType<CodexReadFailed>(result).Reason);
}
