using System.Runtime.InteropServices;
using SystemResourceMonitor.Core.Metrics;
using static SystemResourceMonitor.Desktop.Adapters.Metrics.MetricNativeMethods;

namespace SystemResourceMonitor.Desktop.Adapters.Metrics;

/// <summary>The native seam of the CPU source.</summary>
internal interface ISystemTimesApi
{
    /// <summary>Reads the system-wide idle, kernel, and user times; on failure, <paramref name="error"/> is the Win32 error.</summary>
    bool TryRead(out CpuTimes times, out int error);
}

/// <summary>Calls <c>GetSystemTimes</c>.</summary>
internal sealed class Win32SystemTimesApi : ISystemTimesApi
{
    public bool TryRead(out CpuTimes times, out int error)
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            times = default;
            error = Marshal.GetLastPInvokeError();
            return false;
        }

        times = new CpuTimes(idle.Ticks, kernel.Ticks, user.Ticks);
        error = 0;
        return true;
    }
}
