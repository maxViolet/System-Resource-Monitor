namespace SystemResourceMonitor.AIResources.Core.Claude;

public static class ClaudeContract
{
    public static IReadOnlyList<string> QualifiedVersions { get; } = ["2.1.304", "2.1.306"];

    public static bool IsQualifiedFileVersion(string? version) =>
        version is "2.1.304.0" or "2.1.306.0";
    public const int ShortWindowMinutes = 300;
    public static IReadOnlyList<string> AuthArguments { get; } = ["auth", "status", "--json"];
    public static IReadOnlyList<string> UsageArguments { get; } =
        ["--safe-mode", "--tools", "", "--no-session-persistence", "--print", "--output-format", "json", "/usage"];
}
