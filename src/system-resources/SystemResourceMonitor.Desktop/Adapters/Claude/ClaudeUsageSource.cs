using System.IO;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using SystemResourceMonitor.AIResources.Core.Claude;
using SystemResourceMonitor.Desktop.Adapters.Codex;
using SystemResourceMonitor.Desktop.Lifecycle;

namespace SystemResourceMonitor.Desktop.Adapters.Claude;

/// <summary>Reads only the verified Claude Code client's auth and /usage output in owned process trees.</summary>
internal sealed class ClaudeUsageSource(
    IClaudeClientLocator locator,
    TimeProvider time,
    TimeSpan? requestTimeout = null,
    IReadOnlyDictionary<string, string>? environment = null) : IClaudeLimitSource
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TerminationDeadline = ShutdownCoordinator.WaitDeadline;
    private const ulong ImagePolicy = CodexNativeMethods.BLOCK_NON_MICROSOFT_BINARIES_ALWAYS_ON
        | CodexNativeMethods.IMAGE_LOAD_NO_REMOTE_ALWAYS_ON
        | CodexNativeMethods.IMAGE_LOAD_PREFER_SYSTEM32_ALWAYS_ON;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ProcessTreeJob? _unconfirmedJob;

    public async Task<ClaudeReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        using var shutdown = new ShutdownWindow(time, cancellationToken);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_unconfirmedJob is { } oldJob)
            {
                if (!await oldJob.WaitForEmptyAsync(TimeSpan.Zero, time).ConfigureAwait(false))
                    return new ClaudeReadFailed(ClaudeUnavailableReason.ReadFailed);
                oldJob.Dispose();
                _unconfirmedJob = null;
            }

            using var client = locator.Locate(out var reason);
            cancellationToken.ThrowIfCancellationRequested();
            if (client is null) return new ClaudeReadFailed(reason);

            var auth = await RunAsync(client, ClaudeContract.AuthArguments, shutdown).ConfigureAwait(false);
            if (auth is null) return new ClaudeReadFailed(ClaudeUnavailableReason.TimedOut);
            if (auth.Value.IsLaunchFailure) return new ClaudeReadFailed(ClaudeUnavailableReason.ReadFailed);
            if (AuthState(auth.Value.Output, false)) return new ClaudeReadFailed(ClaudeUnavailableReason.SignedOut);
            if (auth.Value.ExitCode != 0 || !AuthState(auth.Value.Output, true))
                return new ClaudeReadFailed(ClaudeUnavailableReason.UnknownFormat);

            cancellationToken.ThrowIfCancellationRequested();
            var usage = await RunAsync(client, ClaudeContract.UsageArguments, shutdown).ConfigureAwait(false);
            if (usage is null) return new ClaudeReadFailed(ClaudeUnavailableReason.TimedOut);
            if (usage.Value.IsLaunchFailure) return new ClaudeReadFailed(ClaudeUnavailableReason.ReadFailed);
            if (usage.Value.ExitCode != 0 || usage.Value.Error.Length != 0)
                return new ClaudeReadFailed(ClaudeUnavailableReason.UnknownFormat);
            var snapshot = ClaudeUsageReader.Read(Encoding.UTF8.GetBytes(usage.Value.Output), time.GetUtcNow());
            return snapshot is null ? new ClaudeReadFailed(ClaudeUnavailableReason.UnknownFormat)
                : new ClaudeReadSucceeded(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException)
        { return new ClaudeReadFailed(ClaudeUnavailableReason.UnknownFormat); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        { return new ClaudeReadFailed(ClaudeUnavailableReason.ReadFailed); }
        finally { _gate.Release(); }
    }

    private async Task<ProcessResult?> RunAsync(ClaudeClient client, IReadOnlyList<string> arguments,
        ShutdownWindow shutdown)
    {
        var cancellationToken = shutdown.Token;
        cancellationToken.ThrowIfCancellationRequested();
        var job = ProcessTreeJob.Create();
        var ended = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var child = CodexChildProcess.Start(client.Path, job, environment, arguments,
                client.EnforceImagePolicy ? ImagePolicy : null);
            child.Input.Dispose();
            var stdout = ReadBoundedAsync(child.Output, cancellationToken);
            var stderr = ReadBoundedAsync(child.Error, cancellationToken);
            try
            {
                if (!await job.WaitForEmptyAsync(requestTimeout ?? RequestTimeout, time, cancellationToken).ConfigureAwait(false))
                    return null;
                ended = true;
                return new ProcessResult(await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false),
                    child.ExitCode);
            }
            finally
            {
                // Draining readers never owns the process; termination in the outer finally ends the tree.
                _ = stdout.ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
                _ = stderr.ContinueWith(static task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
        finally
        {
            if (!ended)
            {
                ended = await TerminateAsync(job, shutdown).ConfigureAwait(false);
            }
            if (ended) job.Dispose();
            else _unconfirmedJob = job;
        }
    }

    private async Task<bool> TerminateAsync(ProcessTreeJob job, ShutdownWindow shutdown)
    {
        try { job.Terminate(); }
        catch (Win32Exception) { /* Job emptiness below decides whether termination completed. */ }
        try
        {
            try { return await job.WaitForEmptyAsync(shutdown.Remaining(), time, shutdown.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (shutdown.Token.IsCancellationRequested)
            {
                // Shutdown started before or during the wait: only the rest of its wait remains.
                return await job.WaitForEmptyAsync(shutdown.Remaining(), time).ConfigureAwait(false);
            }
        }
        catch (Win32Exception) { return false; }
    }

    private static async Task<string> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var result = new MemoryStream();
        var buffer = new byte[4096];
        var exceeded = false;
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (result.Length + count > ClaudeUsageReader.MaxOutputBytes) exceeded = true;
            if (!exceeded) result.Write(buffer, 0, count);
        }
        if (exceeded) throw new InvalidDataException("Claude output exceeds the read bound.");
        return new UTF8Encoding(false, true).GetString(result.GetBuffer().AsSpan(0, (int)result.Length));
    }

    private static bool AuthState(string output, bool expected)
    {
        try
        {
            using var document = JsonDocument.Parse(output, new JsonDocumentOptions { MaxDepth = 8 });
            return document.RootElement.TryGetProperty("loggedIn", out var loggedIn)
                && loggedIn.ValueKind is JsonValueKind.True or JsonValueKind.False
                && loggedIn.GetBoolean() == expected;
        }
        catch (JsonException) { return false; }
    }

    private readonly record struct ProcessResult(string Output, string Error, uint ExitCode)
    {
        /// <summary>
        /// The client ended with an NTSTATUS error and printed nothing, as when the image-loading policy blocks one of
        /// its images: it never ran, so this is a read failure, not an answer in an unknown format.
        /// </summary>
        public bool IsLaunchFailure => ExitCode >= 0xC0000000 && Output.Length == 0;
    }

    /// <summary>
    /// The termination budget of one poll. Before cancellation it is the full <see cref="TerminationDeadline"/>; once
    /// the poll is cancelled for shutdown it is what is left of the shutdown wait, measured from the cancellation, so a
    /// terminated client never extends the wait.
    /// </summary>
    internal sealed class ShutdownWindow : IDisposable
    {
        private readonly Lock _gate = new();
        private readonly TimeProvider _time;
        private readonly CancellationTokenRegistration _registration;
        private long? _cancelledAt;

        public ShutdownWindow(TimeProvider time, CancellationToken cancellationToken)
        {
            _time = time;
            Token = cancellationToken;
            _registration = cancellationToken.Register(() =>
            {
                lock (_gate)
                {
                    _cancelledAt ??= _time.GetTimestamp();
                }
            });
        }

        public CancellationToken Token { get; }

        public TimeSpan Remaining()
        {
            lock (_gate)
            {
                if (_cancelledAt is not { } cancelledAt) return TerminationDeadline;
                var remaining = TerminationDeadline - _time.GetElapsedTime(cancelledAt);
                return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            }
        }

        public void Dispose() => _registration.Dispose();
    }
}
