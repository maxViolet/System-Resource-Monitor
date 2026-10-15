using System.Runtime.InteropServices;
using static SystemResourceMonitor.Desktop.Adapters.Metrics.MetricNativeMethods;

namespace SystemResourceMonitor.Desktop.Adapters.Metrics;

/// <summary>The native seam of the RAM source.</summary>
internal interface IMemoryStatusApi
{
    /// <summary>Reads total and available physical memory; on failure, <paramref name="error"/> is the Win32 error.</summary>
    bool TryRead(out ulong totalPhys, out ulong availPhys, out int error);
}

/// <summary>Calls <c>GlobalMemoryStatusEx</c>.</summary>
internal sealed class Win32MemoryStatusApi : IMemoryStatusApi
{
    private static readonly uint StructureLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();

    public bool TryRead(out ulong totalPhys, out ulong availPhys, out int error) =>
        TryRead(StructureLength, out totalPhys, out availPhys, out error);

    /// <summary>Calls with an explicit <c>dwLength</c>, so integration tests can exercise the failure path.</summary>
    internal bool TryRead(uint length, out ulong totalPhys, out ulong availPhys, out int error)
    {
        var status = new MEMORYSTATUSEX { dwLength = length };
        if (!GlobalMemoryStatusEx(ref status))
        {
            totalPhys = 0;
            availPhys = 0;
            error = Marshal.GetLastPInvokeError();
            return false;
        }

        totalPhys = status.ullTotalPhys;
        availPhys = status.ullAvailPhys;
        error = 0;
        return true;
    }
}
