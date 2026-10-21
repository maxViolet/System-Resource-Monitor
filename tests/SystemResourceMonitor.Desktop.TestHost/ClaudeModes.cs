using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Desktop.Adapters.Claude;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32.SafeHandles;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text;

namespace SystemResourceMonitor.Desktop.TestHost;

public static partial class Program
{
    private static int FakeClaudeAuth()
    {
        var mode = Environment.GetEnvironmentVariable("SRM_FAKE_CLAUDE_MODE");
        var signedOut = mode == "signed-out";
        Console.WriteLine(JsonSerializer.Serialize(new { loggedIn = !signedOut }));
        return signedOut || mode == "auth-nonzero" ? AssertionFailed : Success;
    }

    private static int FakeClaudeUsage()
    {
        var mode = Environment.GetEnvironmentVariable("SRM_FAKE_CLAUDE_MODE");
        if (mode == "hang")
        {
            using var grandchild = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--fake-claude-grandchild")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            var marker = Environment.GetEnvironmentVariable("SRM_FAKE_CLAUDE_PID_FILE");
            if (grandchild is not null && marker is not null)
                File.WriteAllText(marker, grandchild.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Thread.Sleep(Timeout.Infinite);
        }
        var result = mode == "unknown" ? "Changed usage rendering"
            : "Current session: 0% used\nCurrent week (all models): 0% used";
        if (mode == "large-utf8") result += "\n" + new string('é', 33_000);
        if (mode == "large-utf8") Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            local_command = "usage", num_turns = 0, duration_api_ms = 0, total_cost_usd = 0, is_error = false, result,
        }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        return mode == "usage-nonzero" ? AssertionFailed : Success;
    }

    private static int ClaudeProbe(int count, TimeSpan interval)
    {
        var source = new ClaudeUsageSource(ClaudeClientLocator.ForCurrentUser(), TimeProvider.System);
        for (var index = 0; index < count; index++)
        {
            if (index > 0) Thread.Sleep(interval);
            ClaudeReadResult result;
            try { result = source.ReadAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception exception)
            {
                Console.WriteLine($"probe {index + 1}: exception {exception.GetType().Name}");
                return AssertionFailed;
            }
            if (result is ClaudeReadFailed failed)
            {
                Console.WriteLine($"probe {index + 1}: {failed.Reason}");
                return AssertionFailed;
            }
            var snapshot = ((ClaudeReadSucceeded)result).Snapshot;
            Console.WriteLine($"probe {index + 1}: short={snapshot.ShortWindow.UsedPercent}% "
                + $"weekly={snapshot.BindingWeeklyWindow.UsedPercent}% rows={snapshot.WeeklyWindows.Count} "
                + $"short-reset-present={snapshot.ShortWindow.ResetsAt is not null}");
        }
        return Success;
    }

    // One-off compatibility check for the image-loading policy; not a source-qualification path.
    private static int ClaudeImageProbe(string imagePath)
    {
        var source = new ClaudeUsageSource(new ExplicitImageLocator(imagePath), TimeProvider.System);
        var result = source.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine(result is ClaudeReadSucceeded ? "image-policy-compatible" :
            $"image-policy-failed:{((ClaudeReadFailed)result).Reason}");
        return result is ClaudeReadSucceeded ? Success : AssertionFailed;
    }

    private sealed class ExplicitImageLocator(string imagePath) : IClaudeClientLocator
    {
        public ClaudeClient? Locate(out ClaudeUnavailableReason reason)
        {
            reason = default;
            SafeFileHandle lease = File.OpenHandle(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return new ClaudeClient(imagePath, lease);
        }
    }
}
