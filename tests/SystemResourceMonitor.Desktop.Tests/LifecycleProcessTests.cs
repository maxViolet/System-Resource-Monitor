using SystemResourceMonitor.Desktop.Lifecycle;
using SystemResourceMonitor.Desktop.TestHost;
using TestHostProgram = SystemResourceMonitor.Desktop.TestHost.Program;

namespace SystemResourceMonitor.Desktop.Tests;

/// <summary>
/// Runs the real Desktop app and test host against the current desktop. Every test registers an AppBar, so the
/// tests of this class run one at a time; they fail if a Desktop bar is already running.
/// </summary>
public sealed class LifecycleProcessTests
{
    private const int TimeoutMilliseconds = 60_000;
    private const string DesktopExe = "SystemResourceMonitor.Desktop.exe";
    private const string TestHostExe = "SystemResourceMonitor.Desktop.TestHost.exe";

    [Theory(Timeout = TimeoutMilliseconds)]
    [InlineData("--fail-after-setpos")]
    [InlineData("--bounded-shutdown")]
    [InlineData("--faulted-metric-run")]
    [InlineData("--edge-switches")]
    [InlineData("--fullscreen-reconciliation")]
    [InlineData("--settings-read-failure")]
    [InlineData("--settings-backup-failure")]
    [InlineData("--settings-write-failure")]
    [InlineData("--shutdown-during-settings-load")]
    [InlineData("--metric-display")]
    public async Task TestHost_lifecycle_mode_passes_its_work_area_and_marker_assertions(string mode)
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var host = ChildProcess.Start(TestHostExe, mode);

        var exitCode = await host.WaitForExitAsync(cancellation);

        Assert.True(exitCode == TestHostProgram.Success, $"{mode} exited with {exitCode}.\n{host.Describe()}");
    }

    [Theory(Timeout = TimeoutMilliseconds)]
    [InlineData("--metric-freshness", "cpu")]
    [InlineData("--metric-freshness", "ram")]
    [InlineData("--metric-freshness", "gpu")]
    [InlineData("--gpu-correlation", "1")]
    [InlineData("--native-reads", "10")]
    public async Task TestHost_metric_mode_passes_its_assertions(string mode, string argument)
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var host = ChildProcess.Start(TestHostExe, mode, argument);

        var exitCode = await host.WaitForExitAsync(cancellation);

        Assert.True(exitCode == TestHostProgram.Success, $"{mode} {argument} exited with {exitCode}.\n{host.Describe()}");
    }

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task Second_process_exits_before_settings_while_the_owner_holds_the_guard()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var owner = ChildProcess.Start(TestHostExe, "--hold-bar");
        await owner.ReadUntilAsync("ready", cancellation);
        var ownerLines = owner.OutputLines.ToList();
        Assert.True(
            ownerLines.IndexOf("guard-acquired") is >= 0 and var guard && guard < ownerLines.IndexOf("settings-start"),
            owner.Describe());

        using var second = ChildProcess.Start(TestHostExe, "--hold-bar");
        Assert.Equal(ExitCodes.Success, await second.WaitForExitAsync(cancellation));
        Assert.Contains("non-owner", second.OutputLines);
        Assert.DoesNotContain("settings-start", second.OutputLines);

        owner.CloseInput();
        Assert.Equal(ExitCodes.Success, await owner.WaitForExitAsync(cancellation));
    }

    [Fact(Timeout = TimeoutMilliseconds)]
    public async Task Desktop_reserves_one_strip_ignores_a_second_launch_and_restores_the_work_area_on_close()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var baseline = DesktopProbe.WorkArea();

        using var owner = ChildProcess.Start(DesktopExe);
        var bar = await WaitForSingleWindowAsync(owner, cancellation);
        var barRect = DesktopProbe.WindowRect(bar);
        var reserved = DesktopProbe.WorkArea();
        Assert.True(
            reserved == baseline with { Top = baseline.Top + barRect.Height } ||
            reserved == baseline with { Bottom = baseline.Bottom - barRect.Height },
            $"work area {reserved} is not a one-edge reservation from {baseline} for bar {barRect}");

        using (var second = ChildProcess.Start(DesktopExe))
        {
            Assert.Equal(ExitCodes.Success, await second.WaitForExitAsync(cancellation));
        }

        Assert.Equal([bar], DesktopProbe.VisibleWindows(owner.Id));
        Assert.Equal(reserved, DesktopProbe.WorkArea());

        Assert.True(DesktopProbe.RequestClose(bar));
        Assert.Equal(ExitCodes.Success, await owner.WaitForExitAsync(cancellation));
        Assert.Equal(baseline, DesktopProbe.WorkArea());
    }

    private static async Task<nint> WaitForSingleWindowAsync(ChildProcess process, CancellationToken cancellation)
    {
        while (true)
        {
            var windows = DesktopProbe.VisibleWindows(process.Id);
            if (windows.Count > 0)
            {
                return Assert.Single(windows);
            }

            Assert.False(process.HasExited, $"The Desktop process exited before showing the bar.\n{process.Describe()}");
            await Task.Delay(100, cancellation);
        }
    }
}
