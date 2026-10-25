using System.Runtime.CompilerServices;
using static HowWieYoong.CmdPalLauncher.Desktop.NativeMethods;

namespace HowWieYoong.CmdPalLauncher.Tests;

/// <summary>Guards the hand-written Win32 structs against layout mistakes; sizes are from the Windows SDK headers.</summary>
public class NativeMethodsTests
{
    [Fact]
    public void StructSizes_MatchWindows()
    {
        Assert.Equal(104, Unsafe.SizeOf<MONITORINFOEXW>());
        Assert.Equal(72, Unsafe.SizeOf<DISPLAYCONFIG_PATH_INFO>());
        Assert.Equal(64, Unsafe.SizeOf<DISPLAYCONFIG_MODE_INFO>());
        Assert.Equal(20, Unsafe.SizeOf<DISPLAYCONFIG_DEVICE_INFO_HEADER>());
        Assert.Equal(84, Unsafe.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>());
        Assert.Equal(420, Unsafe.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>());
        Assert.Equal(40, Unsafe.SizeOf<BITMAPINFOHEADER>());
        Assert.Equal(nint.Size == 8 ? 16 : 8, Unsafe.SizeOf<UNICODE_STRING>());
    }
}
