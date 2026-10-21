using System.IO;
using System.Diagnostics;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Desktop.Adapters.Claude;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class ClaudeUsageSourceTests
{
    private static readonly string FakeClient = Path.Combine(AppContext.BaseDirectory,
        "SystemResourceMonitor.Desktop.TestHost.exe");

    [Theory]
    [InlineData("normal", typeof(ClaudeReadSucceeded))]
    [InlineData("signed-out", typeof(ClaudeReadFailed))]
    [InlineData("unknown", typeof(ClaudeReadFailed))]
    [InlineData("auth-nonzero", typeof(ClaudeReadFailed))]
    [InlineData("usage-nonzero", typeof(ClaudeReadFailed))]
    [InlineData("large-utf8", typeof(ClaudeReadFailed))]
    public async Task Client_output_is_classified_without_reading_credentials(string mode, Type expected)
    {
        var source = Source(mode);

        var result = await source.ReadAsync(TestContext.Current.CancellationToken);

        Assert.IsType(expected, result);
        if (mode == "signed-out")
            Assert.Equal(ClaudeUnavailableReason.SignedOut, ((ClaudeReadFailed)result).Reason);
        if (mode is "unknown" or "auth-nonzero" or "usage-nonzero" or "large-utf8")
            Assert.Equal(ClaudeUnavailableReason.UnknownFormat, ((ClaudeReadFailed)result).Reason);
        if (mode == "normal")
            Assert.Equal(100, ((ClaudeReadSucceeded)result).Snapshot.ShortWindow.RemainingPercent);
    }

    [Fact(Timeout = 10000)]
    public async Task Timed_out_client_tree_is_terminated_before_source_returns()
    {
        var marker = Path.GetTempFileName();
        try
        {
            var source = Source("hang", TimeSpan.FromSeconds(2), marker);

            var result = await source.ReadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(ClaudeUnavailableReason.TimedOut, Assert.IsType<ClaudeReadFailed>(result).Reason);
            Assert.True(int.TryParse(File.ReadAllText(marker), out var grandchildId));
            Assert.False(IsRunning(grandchildId));
        }
        finally { File.Delete(marker); }
    }

    [Fact(Timeout = 10000)]
    public async Task Cancellation_terminates_the_running_client_tree()
    {
        var marker = Path.GetTempFileName();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var source = Source("hang", TimeSpan.FromSeconds(8), marker);
        var read = source.ReadAsync(cancellation.Token);
        try
        {
            var started = Stopwatch.StartNew();
            var grandchildId = 0;
            while (started.Elapsed < TimeSpan.FromSeconds(5)
                && !int.TryParse(File.ReadAllText(marker), out grandchildId))
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.True(grandchildId > 0, "The fake Claude grandchild did not start.");

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            Assert.False(IsRunning(grandchildId));
        }
        finally
        {
            cancellation.Cancel();
            File.Delete(marker);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task Cancellation_after_client_resolution_prevents_child_creation()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var source = new ClaudeUsageSource(new BlockingLocator(entered, release), TimeProvider.System);
        var read = Task.Run(() => source.ReadAsync(cancellation.Token), TestContext.Current.CancellationToken);

        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            cancellation.Cancel();
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        }
        finally { release.Set(); }
    }

    private static bool IsRunning(int id)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static ClaudeUsageSource Source(string mode, TimeSpan? timeout = null, string? marker = null) => new(
        new FakeLocator(), TimeProvider.System, timeout,
        new Dictionary<string, string>
        {
            ["SRM_FAKE_CLAUDE_MODE"] = mode,
            ["SRM_FAKE_CLAUDE_PID_FILE"] = marker ?? string.Empty,
        });

    private sealed class BlockingLocator(ManualResetEventSlim entered, ManualResetEventSlim release) : IClaudeClientLocator
    {
        public ClaudeClient? Locate(out ClaudeUnavailableReason reason)
        {
            entered.Set();
            release.Wait();
            reason = default;
            return new ClaudeClient(Path.Combine(AppContext.BaseDirectory, "missing-claude-client.exe"),
                File.OpenHandle(FakeClient, FileMode.Open, FileAccess.Read, FileShare.Read),
                enforceImagePolicy: false);
        }
    }

    private sealed class FakeLocator : IClaudeClientLocator
    {
        public ClaudeClient? Locate(out ClaudeUnavailableReason reason)
        {
            reason = default;
            return new ClaudeClient(FakeClient,
                File.OpenHandle(FakeClient, FileMode.Open, FileAccess.Read, FileShare.Read),
                enforceImagePolicy: false);
        }
    }
}
