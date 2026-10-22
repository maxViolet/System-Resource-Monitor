namespace SystemResourceMonitor.AIResources.Core.Claude;

public static class ClaudeContract
{
    /// <summary>The oldest supported client. Later versions are accepted; each poll's envelope proves it stayed local.</summary>
    public static Version MinimumVersion { get; } = new(2, 1, 304, 0);

    public static bool IsSupportedFileVersion(string? version) =>
        Version.TryParse(version, out var parsed) && parsed >= MinimumVersion;
    public const int ShortWindowMinutes = 300;
    public static IReadOnlyList<string> AuthArguments { get; } = ["auth", "status", "--json"];
    public static IReadOnlyList<string> UsageArguments { get; } =
        ["--safe-mode", "--tools", "", "--no-session-persistence", "--print", "--output-format", "json", "/usage"];
}
