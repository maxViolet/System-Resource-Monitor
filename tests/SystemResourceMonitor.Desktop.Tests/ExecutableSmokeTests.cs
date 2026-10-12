using System.Diagnostics;
using TestHostProgram = SystemResourceMonitor.Desktop.TestHost.Program;

namespace SystemResourceMonitor.Desktop.Tests;

/// <summary>
/// Starts the Desktop and TestHost executables as separate processes and checks their exit codes.
/// </summary>
public sealed class ExecutableSmokeTests
{
    private const int ExitTimeoutMilliseconds = 30_000;

    [Fact(Timeout = ExitTimeoutMilliseconds)]
    public async Task TestHost_smoke_invocation_exits_with_success()
    {
        await AssertExitCodeAsync(TestContext.Current.CancellationToken, TestHostProgram.Success, "SystemResourceMonitor.Desktop.TestHost.exe", "--smoke");
    }

    [Fact(Timeout = ExitTimeoutMilliseconds)]
    public async Task TestHost_invalid_invocation_exits_with_failure()
    {
        await AssertExitCodeAsync(TestContext.Current.CancellationToken, TestHostProgram.InvalidInvocation, "SystemResourceMonitor.Desktop.TestHost.exe", "--unknown");
    }

    [Fact(Timeout = ExitTimeoutMilliseconds)]
    public async Task Desktop_starts_and_exits_with_success()
    {
        await AssertExitCodeAsync(TestContext.Current.CancellationToken, 0, "SystemResourceMonitor.Desktop.exe");
    }

    private static async Task AssertExitCodeAsync(CancellationToken cancellation, int expected, string executable, params string[] arguments)
    {
        var path = Path.Combine(AppContext.BaseDirectory, executable);
        Assert.True(File.Exists(path), $"{executable} was not copied to the test output.");

        var startInfo = new ProcessStartInfo(path, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"{executable} did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var error = process.StandardError.ReadToEndAsync(cancellation);

        try
        {
            await process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        Assert.True(
            process.ExitCode == expected,
            $"{executable} exited with {process.ExitCode}, expected {expected}.\nstdout: {await output}\nstderr: {await error}");
    }
}
