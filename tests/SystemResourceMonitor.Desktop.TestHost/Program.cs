namespace SystemResourceMonitor.Desktop.TestHost;

/// <summary>
/// Entry point of the test host. Later bricks add lifecycle fault modes as further commands.
/// </summary>
public static class Program
{
    public const int Success = 0;
    public const int InvalidInvocation = 2;

    public static int Main(string[] args)
    {
        if (args is ["--smoke"])
        {
            Console.WriteLine("SystemResourceMonitor.Desktop.TestHost: smoke OK");
            return Success;
        }

        Console.Error.WriteLine("Usage: SystemResourceMonitor.Desktop.TestHost --smoke");
        return InvalidInvocation;
    }
}
