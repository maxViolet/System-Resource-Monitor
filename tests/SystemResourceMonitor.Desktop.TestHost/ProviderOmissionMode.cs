using System.Windows.Controls;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.TestHost;

public static partial class Program
{
    private static readonly TimeSpan FirstPollDeadline = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Composes the production bar with the named providers left out, as an unqualified provider is. Each omitted
    /// provider has no section, the remaining sections keep their order, and each composed provider keeps its section
    /// and shows the client-not-found text after its first poll.
    /// </summary>
    private static int ProviderOmission(string omitted)
    {
        var codex = omitted is "codex" or "both"
            ? null
            : new CodexLimitMonitor(new MissingCodexClient(), TimeProvider.System);
        var claude = omitted is "claude" or "both"
            ? null
            : new ClaudeLimitMonitor(new MissingClaudeClient(), TimeProvider.System);
        var run = new LifecycleRun(codex: codex, claude: claude);
        run.Run((bar, shutdown) =>
        {
            run.CheckPositionedBar(bar);
            _ = CheckAsync();

            async Task CheckAsync()
            {
                try
                {
                    var (cpu, ram, gpu) = LifecycleRun.Sections(bar);
                    var expected = new List<TextBlock> { cpu, ram, gpu };
                    if (codex is not null) expected.Add(bar.CodexSection);
                    if (claude is not null) expected.Add(bar.ClaudeSection);
                    var shown = bar.Content is StackPanel row ? row.Children.Cast<object>().ToList() : [];
                    run.Check(shown.SequenceEqual(expected),
                        $"--provider-omission {omitted} shows {shown.Count} sections, not CPU, RAM, GPU, then the composed providers in order");

                    var providers = LifecycleRun.ProviderSections(bar);
                    run.Check((codex is null) == (providers.Codex is null),
                        $"Codex section presence does not match --provider-omission {omitted}");
                    run.Check((claude is null) == (providers.Claude is null),
                        $"Claude section presence does not match --provider-omission {omitted}");

                    var missingCodex = new CodexLimitsUnavailable(CodexUnavailableReason.ClientNotFound);
                    var missingClaude = new ClaudeLimitsUnavailable(ClaudeUnavailableReason.ClientNotFound);
                    var codexText = CodexSectionFormat.Format(missingCodex, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
                    var claudeText = ClaudeSectionFormat.Format(missingClaude, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
                    static bool Shows(TextBlock? section, MetricSection content) =>
                        section is null || (section.Text == content.Text && Equals(section.ToolTip, content.ToolTip));
                    var deadline = run.Now + FirstPollDeadline;
                    while (!(Shows(providers.Codex, codexText) && Shows(providers.Claude, claudeText)) && run.Now < deadline)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(50));
                    }

                    if (providers.Codex is { } codexSection)
                    {
                        run.Check(codexSection.Text == codexText.Text && Equals(codexSection.ToolTip, codexText.ToolTip),
                            $"the Codex section shows '{codexSection.Text}', not its client-not-found text '{codexText.Text}'");
                    }

                    if (providers.Claude is { } claudeSection)
                    {
                        run.Check(claudeSection.Text == claudeText.Text && Equals(claudeSection.ToolTip, claudeText.ToolTip),
                            $"the Claude section shows '{claudeSection.Text}', not its client-not-found text '{claudeText.Text}'");
                    }
                }
                catch (Exception exception)
                {
                    run.Check(false, $"the provider omission check failed with {exception.GetType().Name}");
                }
                finally
                {
                    run.Check(DesktopProbe.RequestClose(bar.Handle), "the provider omission check could not close the bar");
                }
            }
        });

        return run.Finish();
    }

    private sealed class MissingCodexClient : ICodexLimitSource
    {
        public Task<CodexReadResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<CodexReadResult>(new CodexReadFailed(CodexUnavailableReason.ClientNotFound));
    }

    private sealed class MissingClaudeClient : IClaudeLimitSource
    {
        public Task<ClaudeReadResult> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ClaudeReadResult>(new ClaudeReadFailed(ClaudeUnavailableReason.ClientNotFound));
    }
}
