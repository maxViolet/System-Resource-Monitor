using System.Buffers;
using System.ComponentModel;
using System.IO;
using SystemResourceMonitor.AIResources.Core.Codex;
using SystemResourceMonitor.Desktop.Lifecycle;

namespace SystemResourceMonitor.Desktop.Adapters.Codex;

/// <summary>
/// One Codex poll per call: a fresh, verified <c>codex app-server --listen stdio://</c> child in its own kill-on-close
/// job, the three qualified request messages, and bounded concurrent reads of stdout and stderr. Raw output is
/// handed only to <see cref="CodexResponseReader"/> and is never stored or logged.
/// </summary>
/// <remarks>
/// The request timeout is <see cref="RequestTimeout"/>, about four times B010's slowest observed answer (2.3 s, a
/// network failure through an unreachable proxy) and far inside the one-minute poll interval. After the answer the
/// client gets <see cref="ExitGrace"/> to exit after its stdin closes; then its tree is terminated. On timeout, an
/// output limit, or cancellation, the tree is terminated at once and its exit is checked for up to
/// <see cref="TerminationDeadline"/>. An unconfirmed job remains owned and prevents another client from starting, so
/// polls never overlap.
/// </remarks>
/// <param name="requestTimeout">Tests shorten the timeout; production uses <see cref="RequestTimeout"/>.</param>
/// <param name="exitGrace">Tests shorten the exit grace; production uses <see cref="ExitGrace"/>.</param>
/// <param name="environment">Extra child environment for the test host's fake client; production passes none.</param>
internal sealed class CodexAppServerSource(
    ICodexClientLocator locator,
    TimeProvider time,
    TimeSpan? requestTimeout = null,
    TimeSpan? exitGrace = null,
    IReadOnlyDictionary<string, string>? environment = null) : ICodexLimitSource
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(5);

    /// <summary>How long a terminated client tree may take to exit: the Desktop shutdown deadline.</summary>
    public static readonly TimeSpan TerminationDeadline = ShutdownCoordinator.WaitDeadline;

    /// <summary>The most stderr bytes accepted. The signed-out warning B010 observed is 256 bytes.</summary>
    public const int MaxStderrBytes = 64 * 1024;

    private const int ReadBufferBytes = 4096;

    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private ProcessTreeJob? _unconfirmedJob;

    public async Task<CodexReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        await _pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_unconfirmedJob is { } previousJob)
            {
                if (!await previousJob.WaitForEmptyAsync(TimeSpan.Zero, time, CancellationToken.None).ConfigureAwait(false))
                {
                    return new CodexReadFailed(CodexUnavailableReason.ReadFailed);
                }

                previousJob.Dispose();
                _unconfirmedJob = null;
            }

            using var client = locator.Locate(out var reason);
            if (client is null)
            {
                return new CodexReadFailed(reason);
            }

            var job = ProcessTreeJob.Create();
            try
            {
                CodexChildProcess process;
                try
                {
                    process = CodexChildProcess.Start(client.Path, job, environment);
                }
                catch (Win32Exception)
                {
                    return new CodexReadFailed(CodexUnavailableReason.ReadFailed);
                }

                using (process)
                {
                    // The image is mapped now; the lease no longer needs to hold the file.
                    client.Dispose();
                    return await RunSessionAsync(process, job, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                if (!ReferenceEquals(_unconfirmedJob, job))
                {
                    job.Dispose();
                }
            }
        }
        finally
        {
            _pollGate.Release();
        }
    }


    private async Task<CodexReadResult> RunSessionAsync(
        CodexChildProcess process,
        ProcessTreeJob job,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(requestTimeout ?? RequestTimeout, time);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var stdout = ReadStdoutAsync(process.Output, session.Token);
        var stderr = DrainStderrAsync(process.Error, session.Token);
        // Only a graceful exit sets this; every other path, including cancellation, terminates the tree in finally.
        var ended = false;
        try
        {
            CodexReadResult result;
            try
            {
                await WriteRequestsAsync(process.Input, session.Token).ConfigureAwait(false);
                if (await Task.WhenAny(stdout, stderr).ConfigureAwait(false) == stderr && await stderr.ConfigureAwait(false))
                {
                    result = new CodexReadFailed(CodexUnavailableReason.UnknownFormat);
                }
                else
                {
                    result = await stdout.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return new CodexReadFailed(CodexUnavailableReason.TimedOut);
            }

            if (result is CodexReadFailed)
            {
                // A decided failure needs nothing more from the client.
                return result;
            }

            CloseQuietly(process.Input);
            ended = await job.WaitForEmptyAsync(exitGrace ?? ExitGrace, time, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            if (!ended && !await TerminateAsync(job).ConfigureAwait(false))
            {
                _unconfirmedJob = job;
            }

            // Termination closes the pipes, so both readers end; never leave their faults unobserved.
            await Task.WhenAll(Observe(stdout), Observe(stderr))
                .WaitAsync(TerminationDeadline, time, CancellationToken.None)
                .ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
            CloseQuietly(process.Input);
        }
    }

    private static async Task WriteRequestsAsync(Stream input, CancellationToken cancellationToken)
    {
        try
        {
            await input.WriteAsync(CodexContract.RequestMessages, cancellationToken).ConfigureAwait(false);
            await input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The client exited before reading its requests; stdout ends and the reader decides the result.
        }
    }

    /// <summary>Splits stdout into lines of at most <see cref="CodexContract.MaxLineBytes"/> until the reader decides.</summary>
    private static async Task<CodexReadResult> ReadStdoutAsync(Stream output, CancellationToken cancellationToken)
    {
        var reader = new CodexResponseReader();
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        var line = ArrayPool<byte>.Shared.Rent(CodexContract.MaxLineBytes + 1);
        var length = 0;
        try
        {
            while (true)
            {
                var read = await output.ReadAsync(buffer.AsMemory(0, ReadBufferBytes), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return reader.Complete();
                }

                foreach (var value in buffer.AsSpan(0, read))
                {
                    if (value != (byte)'\n')
                    {
                        if (length > CodexContract.MaxLineBytes)
                        {
                            return new CodexReadFailed(CodexUnavailableReason.UnknownFormat);
                        }

                        line[length++] = value;
                        continue;
                    }

                    var end = length > 0 && line[length - 1] == (byte)'\r' ? length - 1 : length;
                    length = 0;
                    if (end > 0 && reader.Accept(line.AsMemory(0, end)) is { } result)
                    {
                        return result;
                    }
                }
            }
        }
        finally
        {
            // Overwrite account data before the buffers return to the shared pool.
            Array.Clear(line);
            Array.Clear(buffer);
            ArrayPool<byte>.Shared.Return(line);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Discards stderr; returns <see langword="true"/> when it exceeds <see cref="MaxStderrBytes"/>.</summary>
    private static async Task<bool> DrainStderrAsync(Stream error, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes);
        var total = 0L;
        try
        {
            while (true)
            {
                var read = await error.ReadAsync(buffer.AsMemory(0, ReadBufferBytes), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return false;
                }

                total += read;
                if (total > MaxStderrBytes)
                {
                    return true;
                }
            }
        }
        finally
        {
            Array.Clear(buffer);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<bool> TerminateAsync(ProcessTreeJob job)
    {
        try
        {
            job.Terminate();
        }
        catch (Win32Exception)
        {
            // Confirmation below decides whether the tree ended.
        }

        try
        {
            return await job.WaitForEmptyAsync(TerminationDeadline, time).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static Task Observe(Task task) => task.ContinueWith(
        static completed => _ = completed.Exception,
        CancellationToken.None,
        TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private static void CloseQuietly(Stream input)
    {
        try
        {
            input.Close();
        }
        catch (IOException)
        {
            // The pipe is already broken because the client exited.
        }
    }
}
