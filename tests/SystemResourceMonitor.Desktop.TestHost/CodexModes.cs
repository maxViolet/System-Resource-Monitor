using System.Diagnostics;
using System.Globalization;
using System.IO;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Desktop.Adapters.Codex;

namespace SystemResourceMonitor.Desktop.TestHost;

/// <summary>
/// Codex modes. <c>--codex-probe</c> runs the production locator and source against the installed client and prints
/// only classifications, remaining shares, and reset times, never raw client output. Started with the client's own
/// arguments, the test host is a fake app-server whose behavior <see cref="FakeScenarioVariable"/> selects, for the
/// Desktop tests of process ownership, bounds, and timeouts.
/// </summary>
public static partial class Program
{
    /// <summary>The fake app-server scenario: success, signed-out, hang, hang-with-child, stderr-flood, or linger.</summary>
    public const string FakeScenarioVariable = "SRM_FAKE_CODEX";

    /// <summary>A file to which the fake app-server writes the process ID of the grandchild it starts.</summary>
    public const string FakeGrandchildPidVariable = "SRM_FAKE_CODEX_GRANDCHILD_PID";

    public const string FakeGrandchildArgument = "--fake-codex-grandchild";

    /// <summary>The synthetic rate-limit result of the fake app-server: 27 % and 4 % used.</summary>
    private const string FakeRateLimits =
        """{"id":2,"result":{"ordinaryUsageAllowed":true,"rateLimits":{"limitId":"codex","limitName":null,"primary":{"usedPercent":27,"windowDurationMins":300,"resetsAt":1792500924},"secondary":{"usedPercent":4,"windowDurationMins":10080,"resetsAt":1793087724},"credits":null,"planType":"plus"},"rateLimitsByLimitId":null,"accountId":"fake"}}""";

    private const string FakeInitialize =
        """{"id":1,"result":{"userAgent":"system-resource-monitor/0.159.0 (Windows 10.0.26300; x86_64) fake","platformFamily":"windows","platformOs":"windows"}}""";

    private const string FakeSignedOut =
        """{"id":2,"error":{"code":-32600,"message":"codex account authentication required to read rate limits"}}""";

    private static int CodexProbe(int polls, TimeSpan interval)
    {
        var source = new CodexAppServerSource(CodexClientLocator.ForCurrentUser(), TimeProvider.System);
        var failed = false;
        for (var poll = 1; poll <= polls; poll++)
        {
            var started = Stopwatch.StartNew();
            var result = source.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            var at = DateTimeOffset.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var line = result switch
            {
                CodexReadSucceeded { Snapshot: var snapshot } => string.Create(CultureInfo.InvariantCulture,
                    $"result=succeeded 5h-used={snapshot.ShortWindow.UsedPercent} 5h-left={snapshot.ShortWindow.RemainingPercent} "
                    + $"5h-resets={Local(snapshot.ShortWindow.ResetsAt)} weekly-used={snapshot.WeeklyWindow.UsedPercent} "
                    + $"weekly-left={snapshot.WeeklyWindow.RemainingPercent} weekly-resets={Local(snapshot.WeeklyWindow.ResetsAt)}"),
                CodexReadFailed { Reason: var reason } => $"result=failed reason={reason}",
                _ => "result=unknown",
            };
            failed |= result is not CodexReadSucceeded;
            Console.WriteLine($"poll {poll} at {at} ms={started.ElapsedMilliseconds} {line}");
            if (poll < polls)
            {
                Thread.Sleep(interval);
            }
        }

        return failed ? AssertionFailed : Success;

        static string Local(DateTimeOffset time) => time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static int FakeCodexAppServer()
    {
        var scenario = Environment.GetEnvironmentVariable(FakeScenarioVariable);
        switch (scenario)
        {
            case "success":
                Answer("""{"method":"remoteControl/status/changed","params":{"status":"disabled"}}""", FakeRateLimits);
                _ = Console.In.ReadToEnd();
                return Success;
            case "signed-out":
                Answer(FakeSignedOut);
                _ = Console.In.ReadToEnd();
                return Success;
            case "linger":
                Answer(FakeRateLimits);
                Thread.Sleep(Timeout.Infinite);
                return Success;
            case "hang":
                Thread.Sleep(Timeout.Infinite);
                return Success;
            case "hang-with-child":
                var grandchild = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, FakeGrandchildArgument)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }) ?? throw new InvalidOperationException("The grandchild did not start.");
                File.WriteAllText(Environment.GetEnvironmentVariable(FakeGrandchildPidVariable)!,
                    grandchild.Id.ToString(CultureInfo.InvariantCulture));
                Thread.Sleep(Timeout.Infinite);
                return Success;
            case "stderr-flood":
                var block = new string('x', 4096);
                for (var index = 0; index < 256; index++)
                {
                    Console.Error.Write(block);
                }

                Console.Error.Flush();
                Thread.Sleep(Timeout.Infinite);
                return Success;
            default:
                Console.Error.WriteLine($"Unknown {FakeScenarioVariable} scenario.");
                return InvalidInvocation;
        }

        // Reads the three requests, then writes the initialize answer and the given messages.
        static void Answer(params string[] messages)
        {
            for (var line = 0; line < 3; line++)
            {
                _ = Console.In.ReadLine();
            }

            Console.Out.WriteLine(FakeInitialize);
            foreach (var message in messages)
            {
                Console.Out.WriteLine(message);
            }

            Console.Out.Flush();
        }
    }
}
