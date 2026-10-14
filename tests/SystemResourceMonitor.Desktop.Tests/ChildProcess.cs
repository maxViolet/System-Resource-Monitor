using System.Diagnostics;
using System.Text;

namespace SystemResourceMonitor.Desktop.Tests;

/// <summary>
/// An executable from the test output folder running as a separate process; killed on dispose if still running.
/// </summary>
internal sealed class ChildProcess : IDisposable
{
    private readonly Process _process;
    private readonly Task<string> _error;
    private readonly StringBuilder _output = new();

    private ChildProcess(Process process)
    {
        _process = process;
        _error = process.StandardError.ReadToEndAsync();
    }

    public int Id => _process.Id;

    public bool HasExited => _process.HasExited;

    public static ChildProcess Start(string executable, params string[] arguments)
    {
        var path = Path.Combine(AppContext.BaseDirectory, executable);
        Assert.True(File.Exists(path), $"{executable} was not copied to the test output.");

        var startInfo = new ProcessStartInfo(path, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        return new ChildProcess(Process.Start(startInfo) ?? throw new InvalidOperationException($"{executable} did not start."));
    }

    /// <summary>Reads stdout lines until <paramref name="marker"/>; fails if the process ends first.</summary>
    public async Task ReadUntilAsync(string marker, CancellationToken cancellation)
    {
        while (await _process.StandardOutput.ReadLineAsync(cancellation) is { } line)
        {
            _output.AppendLine(line);
            if (line == marker)
            {
                return;
            }
        }

        Assert.Fail($"The process ended before printing '{marker}'.\n{Describe()}");
    }

    public void CloseInput() => _process.StandardInput.Close();

    public async Task<int> WaitForExitAsync(CancellationToken cancellation)
    {
        _output.Append(await _process.StandardOutput.ReadToEndAsync(cancellation));
        await _process.WaitForExitAsync(cancellation);
        return _process.ExitCode;
    }

    public IReadOnlyList<string> OutputLines =>
        _output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

    public string Describe() =>
        $"stdout:\n{_output}\nstderr:\n{(_error.IsCompletedSuccessfully ? _error.Result : string.Empty)}";

    public void Dispose()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }

        _process.Dispose();
    }
}
