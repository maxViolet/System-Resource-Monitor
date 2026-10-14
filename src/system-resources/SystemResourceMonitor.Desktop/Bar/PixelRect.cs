using System.Runtime.InteropServices;

namespace SystemResourceMonitor.Desktop.Bar;

/// <summary>
/// A rectangle in physical pixels with the layout of the Win32 <c>RECT</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public override string ToString() => $"({Left}, {Top}, {Right}, {Bottom})";
}
