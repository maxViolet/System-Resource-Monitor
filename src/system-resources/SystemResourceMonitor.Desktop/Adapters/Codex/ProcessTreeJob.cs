using System.ComponentModel;
using System.Runtime.InteropServices;
using static SystemResourceMonitor.Desktop.Adapters.Codex.CodexNativeMethods;

namespace SystemResourceMonitor.Desktop.Adapters.Codex;

/// <summary>
/// A kill-on-close job that owns one client process and every descendant it starts. Terminating the job ends the
/// whole tree; <see cref="WaitForEmptyAsync"/> confirms that no process of the tree remains.
/// </summary>
internal sealed class ProcessTreeJob : IDisposable
{
    private static readonly TimeSpan EmptyPollInterval = TimeSpan.FromMilliseconds(20);

    private readonly SafeJobHandle _handle;

    internal SafeJobHandle Handle => _handle;

    private unsafe ProcessTreeJob(SafeJobHandle handle)
    {
        _handle = handle;
        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE },
        };
        if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, "SetInformationJobObject failed.");
        }
    }

    public static ProcessTreeJob Create()
    {
        var handle = CreateJobObject(0, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, "CreateJobObject failed.");
        }

        return new ProcessTreeJob(handle);
    }


    /// <summary>Ends every process in the job. Terminating an empty or already terminated job succeeds.</summary>
    public void Terminate()
    {
        if (!TerminateJobObject(_handle, 1))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "TerminateJobObject failed.");
        }
    }

    /// <summary>
    /// Waits until no process of the tree remains; returns <see langword="false"/> at the deadline and throws
    /// <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> is cancelled first.
    /// </summary>
    public async Task<bool> WaitForEmptyAsync(TimeSpan deadline, TimeProvider time, CancellationToken cancellationToken = default)
    {
        var start = time.GetTimestamp();
        while (Accounting().ActiveProcesses > 0)
        {
            var remaining = deadline - time.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(remaining < EmptyPollInterval ? remaining : EmptyPollInterval, time, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public void Dispose() => _handle.Dispose();

    private unsafe JOBOBJECT_BASIC_ACCOUNTING_INFORMATION Accounting()
    {
        JOBOBJECT_BASIC_ACCOUNTING_INFORMATION information;
        if (!QueryInformationJobObject(_handle, JobObjectBasicAccountingInformation, &information,
            (uint)sizeof(JOBOBJECT_BASIC_ACCOUNTING_INFORMATION), 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "QueryInformationJobObject failed.");
        }

        return information;
    }
}
