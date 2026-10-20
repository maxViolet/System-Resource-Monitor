using System.Text;

namespace SystemResourceMonitor.AIResources.Core.Codex;

/// <summary>
/// The Codex source contract that B010 accepted: the signed standalone <c>codex-cli 0.159.0</c>, its
/// <c>app-server</c> stdio JSON-RPC session, and the <c>account/rateLimits/read</c> response. Anything outside this
/// contract is unavailable, never guessed.
/// </summary>
public static class CodexContract
{
    /// <summary>The only client version whose response contract is qualified.</summary>
    public const string QualifiedVersion = "0.159.0";

    /// <summary>The only qualified rate-limit bucket: the aggregate Codex bucket.</summary>
    public const string QualifiedLimitId = "codex";

    public const int ShortWindowMinutes = 300;
    public const int WeeklyWindowMinutes = 10080;

    /// <summary>The fixed argument tokens for the client executable.</summary>
    public static IReadOnlyList<string> Arguments { get; } = ["app-server", "--listen", "stdio://"];

    /// <summary>JSON-RPC <c>id</c> of the <c>initialize</c> request.</summary>
    public const int InitializeRequestId = 1;

    /// <summary>JSON-RPC <c>id</c> of the <c>account/rateLimits/read</c> request.</summary>
    public const int RateLimitsRequestId = 2;

    /// <summary>The exact message of the signed-out error (<c>-32600</c>) at the qualified version.</summary>
    public const string SignedOutMessage = "codex account authentication required to read rate limits";

    /// <summary>The message prefix of the network error (<c>-32603</c>) at the qualified version.</summary>
    public const string NetworkFailureMessagePrefix = "failed to fetch codex rate limits:";

    public const int SignedOutErrorCode = -32600;
    public const int NetworkFailureErrorCode = -32603;

    /// <summary>
    /// The three newline-terminated UTF-8 request messages, in order: <c>initialize</c>, <c>initialized</c>, and
    /// <c>account/rateLimits/read</c> with <c>excludeResetCreditDetails</c>. No other method is ever sent; in particular
    /// never <c>account/rateLimitResetCredit/consume</c>.
    /// </summary>
    public static ReadOnlyMemory<byte> RequestMessages { get; } = Encoding.UTF8.GetBytes(
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientInfo":{"name":"system-resource-monitor","version":"1"},"capabilities":{"experimentalApi":false}}}""" + "\n"
        + """{"jsonrpc":"2.0","method":"initialized"}""" + "\n"
        + """{"jsonrpc":"2.0","id":2,"method":"account/rateLimits/read","params":{"excludeResetCreditDetails":true}}""" + "\n");

    /// <summary>The longest accepted stdout line in bytes, without its terminator. Observed responses are about 1.3 KB.</summary>
    public const int MaxLineBytes = 64 * 1024;

    /// <summary>The most stdout messages accepted in one session, the rate-limit response included.</summary>
    public const int MaxMessages = 32;

    /// <summary>The deepest accepted JSON nesting. The qualified response nests five levels.</summary>
    public const int MaxJsonDepth = 16;
}
