using System.IO;
using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Desktop.Adapters.Claude;
using SystemResourceMonitor.Desktop.Lifecycle;

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

    [Fact]
    public async Task Stderr_over_its_own_bound_is_unknown_format_although_stdout_is_valid()
    {
        var result = await Source("large-stderr").ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ClaudeUnavailableReason.UnknownFormat, Assert.IsType<ClaudeReadFailed>(result).Reason);
    }

    [Fact(Timeout = 30000)]
    public async Task Enforced_image_policy_either_runs_protected_or_fails_without_an_unprotected_retry()
    {
        var log = Path.GetTempFileName();
        try
        {
            var source = new ClaudeUsageSource(new FakeLocator(enforceImagePolicy: true), TimeProvider.System, null,
                new Dictionary<string, string>
                {
                    ["SRM_FAKE_CLAUDE_MODE"] = "normal",
                    ["SRM_FAKE_CLAUDE_LAUNCH_LOG"] = log,
                });

            var result = await source.ReadAsync(TestContext.Current.CancellationToken);

            var launches = File.ReadAllLines(log);
            Assert.DoesNotContain(launches, launch => launch.EndsWith(":unprotected", StringComparison.Ordinal));
            if (result is ClaudeReadSucceeded)
            {
                Assert.Equal(["auth:protected", "usage:protected"], launches);
            }
            else
            {
                // A blocked image load ends the poll; the source never starts the client again without the policy.
                Assert.Equal(ClaudeUnavailableReason.ReadFailed, Assert.IsType<ClaudeReadFailed>(result).Reason);
                Assert.True(launches.Length <= 1);
            }
        }
        finally { File.Delete(log); }
    }

    [Fact]
    public void Termination_budget_is_the_rest_of_the_shutdown_wait_after_cancellation()
    {
        var time = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        using var window = new ClaudeUsageSource.ShutdownWindow(time, cancellation.Token);

        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(ShutdownCoordinator.WaitDeadline, window.Remaining());

        cancellation.Cancel();
        time.Advance(TimeSpan.FromMilliseconds(1500));
        Assert.Equal(ShutdownCoordinator.WaitDeadline - TimeSpan.FromMilliseconds(1500), window.Remaining());

        time.Advance(ShutdownCoordinator.WaitDeadline);
        Assert.Equal(TimeSpan.Zero, window.Remaining());
    }

    [Fact]
    public void Termination_budget_of_an_already_cancelled_poll_starts_at_the_poll()
    {
        var time = new FakeTimeProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var window = new ClaudeUsageSource.ShutdownWindow(time, cancellation.Token);

        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(ShutdownCoordinator.WaitDeadline - TimeSpan.FromSeconds(1), window.Remaining());
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

            var cancelled = Stopwatch.StartNew();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
            // The terminated tree is confirmed inside the shutdown wait that the cancellation started.
            Assert.True(cancelled.Elapsed <= ShutdownCoordinator.WaitDeadline, $"The read ended {cancelled.Elapsed} after cancellation.");
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

    private sealed class FakeLocator(bool enforceImagePolicy = false) : IClaudeClientLocator
    {
        public ClaudeClient? Locate(out ClaudeUnavailableReason reason)
        {
            reason = default;
            return new ClaudeClient(FakeClient,
                File.OpenHandle(FakeClient, FileMode.Open, FileAccess.Read, FileShare.Read),
                enforceImagePolicy);
        }
    }
}
