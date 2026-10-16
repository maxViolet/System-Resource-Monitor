using System.Runtime.InteropServices;

namespace SystemResourceMonitor.Desktop.Adapters.Metrics;

[StructLayout(LayoutKind.Sequential)]
internal struct PdhFmtCounterValueItem
{
    public nint Name;
    public PdhFmtCounterValue Value;
}

[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct PdhFmtCounterValue
{
    [FieldOffset(0)] public uint Status;
    [FieldOffset(8)] public double Value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeLuid
{
    public uint LowPart;
    public int HighPart;
}
