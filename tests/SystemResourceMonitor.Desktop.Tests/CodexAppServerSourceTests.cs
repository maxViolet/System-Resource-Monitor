using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Desktop.Adapters.Codex;
using FakeCodex = SystemResourceMonitor.Desktop.TestHost.Program;

namespace SystemResourceMonitor.Desktop.Tests;

/// <summary>
/// Runs alone: the tests identify the fake client tree as test host processes that appeared during the poll, which
/// other test classes starting the test host would disturb.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CodexProcessCollection
{
    public const string Name = "Codex client processes";
}

[Collection(CodexProcessCollection.Name)]
public sealed class CodexAppServerSourceTests : IDisposable
{
    private static readonly string HostPath = Path.Combine(AppContext.BaseDirectory, "SystemResourceMonitor.Desktop.TestHost.exe");
    private static readonly TimeSpan Slack = TimeSpan.FromSeconds(2);

    private readonly string _pidFile = Path.Combine(Path.GetTempPath(), $"srm-codex-{Guid.NewGuid():N}.pid");
    private readonly HashSet<int> _before = HostProcessIds();

    [Fact]
    public async Task Success_returns_the_snapshot_after_the_client_has_exited()
    {
        var source = Source("success");

        var result = await source.ReadAsync(TestContext.Current.CancellationToken);

        var snapshot = Assert.IsType<CodexReadSucceeded>(result).Snapshot;
        Assert.Equal(27, snapshot.ShortWindow.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792500924), snapshot.ShortWindow.ResetsAt);
        Assert.Equal(4, snapshot.WeeklyWindow.UsedPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793087724), snapshot.WeeklyWindow.ResetsAt);
        Assert.Empty(NewHostProcesses());
    }

    [Fact]
    public async Task Signed_out_client_is_reported_as_signed_out()
    {
        var result = await Source("signed-out").ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CodexUnavailableReason.SignedOut, Assert.IsType<CodexReadFailed>(result).Reason);
        Assert.Empty(NewHostProcesses());
    }

    [Fact]
    public async Task Stderr_beyond_its_limit_ends_the_poll_as_unknown_format()
    {
        var result = await Source("stderr-flood").ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CodexUnavailableReason.UnknownFormat, Assert.IsType<CodexReadFailed>(result).Reason);
        Assert.Empty(NewHostProcesses());
    }

    [Fact]
    public async Task Silent_client_times_out_and_is_terminated()
    {
        var timeout = TimeSpan.FromSeconds(1);
        var started = Stopwatch.StartNew();

        var result = await Source("hang", requestTimeout: timeout).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CodexUnavailableReason.TimedOut, Assert.IsType<CodexReadFailed>(result).Reason);
        Assert.InRange(started.Elapsed, timeout, timeout + CodexAppServerSource.TerminationDeadline + Slack);
        Assert.Empty(NewHostProcesses());
    }

    [Fact]
    public async Task Timeout_terminates_the_grandchild_too()
    {
        // Long enough for the fake client to start its grandchild before the timeout under load.
        var result = await Source("hang-with-child", requestTimeout: TimeSpan.FromSeconds(3)).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CodexUnavailableReason.TimedOut, Assert.IsType<CodexReadFailed>(result).Reason);
        Assert.False(IsRunning(ReadGrandchildPid()));
        Assert.Empty(NewHostProcesses());
    }

    [Fact]
    public async Task Cancellation_terminates_the_tree_and_throws_within_the_termination_deadline()
    {
        using var cancellation = new CancellationTokenSource();
        var read = Source("hang-with-child", requestTimeout: TimeSpan.FromMinutes(1)).ReadAsync(cancellation.Token);
        var grandchild = await WaitForGrandchildPidAsync(read);
        Assert.True(IsRunning(grandchild));

        var cancelled = Stopwatch.StartNew();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.InRange(cancelled.Elapsed, TimeSpan.Zero, CodexAppServerSource.TerminationDeadline + Slack);
        Assert.False(IsRunning(grandchild));
        Assert.Empty(NewHostProcesses());
    }

    [Fact]
    public async Task Lingering_client_is_terminated_after_the_exit_grace()
    {
        var grace = TimeSpan.FromMilliseconds(500);
        var started = Stopwatch.StartNew();

        var result = await Source("linger", exitGrace: grace).ReadAsync(TestContext.Current.CancellationToken);

        Assert.IsType<CodexReadSucceeded>(result);
        Assert.InRange(started.Elapsed, grace, grace + CodexAppServerSource.TerminationDeadline + Slack);
        Assert.Empty(NewHostProcesses());
    }

    [Fact]
    public async Task Missing_client_fails_without_starting_a_process()
    {
        var locator = new MissingLocator();
        var source = new CodexAppServerSource(locator, TimeProvider.System);

        var result = await source.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CodexUnavailableReason.ClientNotFound, Assert.IsType<CodexReadFailed>(result).Reason);
        Assert.Equal(1, locator.Calls);
        Assert.Empty(NewHostProcesses());
    }

    public void Dispose()
    {
        foreach (var process in NewHostProcesses())
        {
            using (process)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(TimeSpan.FromSeconds(5));
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                {
                    // Already exited.
                }
            }
        }

        File.Delete(_pidFile);
    }

    private CodexAppServerSource Source(string scenario, TimeSpan? requestTimeout = null, TimeSpan? exitGrace = null) => new(
        new TestHostLocator(),
        TimeProvider.System,
        requestTimeout ?? TimeSpan.FromSeconds(30),
        exitGrace ?? TimeSpan.FromSeconds(30),
        new Dictionary<string, string>
        {
            [FakeCodex.FakeScenarioVariable] = scenario,
            [FakeCodex.FakeGrandchildPidVariable] = _pidFile,
        });

    private int ReadGrandchildPid()
    {
        Assert.True(File.Exists(_pidFile), "The fake client did not start its grandchild.");
        return int.Parse(File.ReadAllText(_pidFile), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>Waits until the fake client has written its grandchild's ID; fails if the poll ends first.</summary>
    private async Task<int> WaitForGrandchildPidAsync(Task read)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            Assert.False(read.IsCompleted, "The poll ended before the grandchild started.");
            try
            {
                if (File.Exists(_pidFile)
                    && int.TryParse(File.ReadAllText(_pidFile), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                {
                    return pid;
                }
            }
            catch (IOException)
            {
                // The fake client is still writing the file.
            }

            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Fail("The fake client did not start its grandchild.");
        return 0;
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Running test host processes that did not exist when the test started: the fake client tree.</summary>
    private List<Process> NewHostProcesses()
    {
        var found = new List<Process>();
        foreach (var process in HostProcesses())
        {
            if (_before.Contains(process.Id))
            {
                process.Dispose();
            }
            else
            {
                found.Add(process);
            }
        }

        return found;
    }

    private static HashSet<int> HostProcessIds()
    {
        var ids = new HashSet<int>();
        foreach (var process in HostProcesses())
        {
            using (process)
            {
                ids.Add(process.Id);
            }
        }

        return ids;
    }

    /// <summary>Running processes of this test output's test host executable; another checkout's are ignored.</summary>
    private static List<Process> HostProcesses()
    {
        var found = new List<Process>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(HostPath)))
        {
            try
            {
                if (!process.HasExited && string.Equals(process.MainModule?.FileName, HostPath, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(process);
                    continue;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
            {
                // Exited meanwhile or not inspectable.
            }

            process.Dispose();
        }

        return found;
    }

    private sealed class TestHostLocator : ICodexClientLocator
    {
        public CodexClient? Locate(out CodexUnavailableReason reason)
        {
            reason = default;
            return new CodexClient(HostPath, File.OpenHandle(HostPath, FileMode.Open, FileAccess.Read, FileShare.Read));
        }
    }

    private sealed class MissingLocator : ICodexClientLocator
    {
        public int Calls { get; private set; }

        public CodexClient? Locate(out CodexUnavailableReason reason)
        {
            Calls++;
            reason = CodexUnavailableReason.ClientNotFound;
            return null;
        }
    }
}
