namespace SystemResourceMonitor.Desktop.Lifecycle;

public static class ExitCodes
{
    /// <summary>Normal exit, including a second process that found another owner.</summary>
    public const int Success = 0;

    /// <summary>A handled startup, dispatcher, or shutdown failure.</summary>
    public const int Failure = 1;
}
