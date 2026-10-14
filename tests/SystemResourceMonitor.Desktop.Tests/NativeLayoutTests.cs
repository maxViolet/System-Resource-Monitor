using System.Runtime.InteropServices;
using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.Tests;

/// <summary>
/// The Win32 x64 sizes the bar passes as <c>cbSize</c>; a wrong field width would make Windows reject or misread them.
/// </summary>
public sealed class NativeLayoutTests
{
    [Fact]
    public void Structures_match_the_x64_Win32_layout()
    {
        Assert.Equal(16, Marshal.SizeOf<PixelRect>());
        Assert.Equal(48, Marshal.SizeOf<NativeMethods.APPBARDATA>());
        Assert.Equal(40, Marshal.SizeOf<NativeMethods.MONITORINFO>());
        Assert.Equal(976, Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>());
    }
}
