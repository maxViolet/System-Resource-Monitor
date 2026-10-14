using TestHostProgram = SystemResourceMonitor.Desktop.TestHost.Program;

namespace SystemResourceMonitor.Desktop.Tests;

/// <summary>
/// Starts the TestHost executable as a separate process and checks its exit codes.
/// </summary>
public sealed class ExecutableSmokeTests
{
    private const int ExitTimeoutMilliseconds = 30_000;

    [Fact(Timeout = ExitTimeoutMilliseconds)]
    public async Task TestHost_smoke_invocation_exits_with_success()
    {
        using var host = ChildProcess.Start("SystemResourceMonitor.Desktop.TestHost.exe", "--smoke");

        Assert.Equal(TestHostProgram.Success, await host.WaitForExitAsync(TestContext.Current.CancellationToken));
    }

    [Fact(Timeout = ExitTimeoutMilliseconds)]
    public async Task TestHost_invalid_invocation_exits_with_failure()
    {
        using var host = ChildProcess.Start("SystemResourceMonitor.Desktop.TestHost.exe", "--unknown");

        Assert.Equal(TestHostProgram.InvalidInvocation, await host.WaitForExitAsync(TestContext.Current.CancellationToken));
    }
}
