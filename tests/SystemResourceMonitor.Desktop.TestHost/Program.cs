using SystemResourceMonitor.Desktop.Lifecycle;

namespace SystemResourceMonitor.Desktop.TestHost;

/// <summary>
/// Entry point of the test host. Fault modes compose the production lifecycle with a real AppBar; the published
/// Desktop app has none of them.
/// </summary>
public static class Program
{
    public const int Success = 0;
    public const int InvalidInvocation = 2;
    public const int AssertionFailed = 3;

    private const string Usage = "Usage: SystemResourceMonitor.Desktop.TestHost --smoke | --fail-after-setpos | --bounded-shutdown | --hold-bar";

    [STAThread]
    public static int Main(string[] args)
    {
        switch (args)
        {
            case ["--smoke"]:
                Console.WriteLine("SystemResourceMonitor.Desktop.TestHost: smoke OK");
                return Success;
            case ["--fail-after-setpos"]:
                return FailAfterSetPos();
            case ["--bounded-shutdown"]:
                return BoundedShutdown();
            case ["--hold-bar"]:
                return HoldBar();
            default:
                Console.Error.WriteLine(Usage);
                return InvalidInvocation;
        }
    }

    /// <summary>
    /// A handled startup failure after <c>ABM_SETPOS</c> restores the work area before the bounded wait.
    /// </summary>
    private static int FailAfterSetPos()
    {
        var run = new LifecycleRun();
        var injected = false;
        run.Run((bar, _) =>
        {
            run.CheckPositionedBar(bar);
            injected = true;
            run.Record("inject-failure");
            throw new InjectedStartupFailureException();
        });

        run.Check(injected, "the failure was not injected");
        run.Check(run.ExitCode == ExitCodes.Failure, $"coordinator exit code {run.ExitCode}, expected {ExitCodes.Failure}");
        run.CheckOrder(
            "guard-acquired",
            "settings-start",
            "bar-positioned",
            "inject-failure",
            $"startup-failed:{nameof(InjectedStartupFailureException)}",
            "remove",
            "restored",
            "wait-start",
            "exit");
        return run.Finish();
    }

    /// <summary>
    /// Normal close with handle-free pending work: the work area is restored before the wait, the wait ends at the
    /// deadline with the pending operations recorded, and a result that arrives afterwards cannot publish.
    /// </summary>
    private static int BoundedShutdown()
    {
        var run = new LifecycleRun();
        var neverCompleting = new TaskCompletionSource();
        var late = new TaskCompletionSource();
        run.Run((bar, shutdown) =>
        {
            run.CheckPositionedBar(bar);
            shutdown.Track("never-completing-source", neverCompleting.Task);
            shutdown.Track("late-source", late.Task);
            bar.Dispatcher.BeginInvoke(() => run.Check(DesktopProbe.RequestClose(bar.Handle), "WM_CLOSE was not processed"));
        });

        late.SetResult();
        var published = false;
        run.Check(run.Shutdown?.TryPublish(() => published = true) == false && !published, "a late result published after shutdown");

        var waited = run.Between("wait-start", "exit");
        LifecycleRun.Info($"wait-ms {waited.TotalMilliseconds:F0}");
        run.Check(
            waited >= ShutdownCoordinator.WaitDeadline - TimeSpan.FromMilliseconds(20) && waited <= ShutdownCoordinator.WaitDeadline + TimeSpan.FromMilliseconds(500),
            $"waited {waited.TotalMilliseconds:F0} ms, expected the {ShutdownCoordinator.WaitDeadline.TotalMilliseconds:F0} ms deadline");
        run.Check(run.ExitCode == ExitCodes.Success, $"coordinator exit code {run.ExitCode}, expected {ExitCodes.Success}");
        run.CheckOrder(
            "bar-positioned",
            "remove",
            "restored",
            "wait-start",
            "pending:never-completing-source",
            "pending:late-source",
            "exit");
        return run.Finish();
    }

    /// <summary>
    /// Holds the bar until a line or end of input arrives on stdin, then closes it with <c>WM_CLOSE</c>.
    /// A second process exits as a non-owner. Returns the coordinator's exit code.
    /// </summary>
    private static int HoldBar()
    {
        var run = new LifecycleRun();
        run.Run((bar, _) =>
        {
            run.Record("ready");
            new Thread(() =>
            {
                Console.In.ReadLine();
                bar.Dispatcher.BeginInvoke(() => DesktopProbe.RequestClose(bar.Handle));
            }) { IsBackground = true }.Start();
        });

        return run.ExitCode ?? AssertionFailed;
    }

    private sealed class InjectedStartupFailureException() : Exception("Injected failure after ABM_SETPOS.");
}
