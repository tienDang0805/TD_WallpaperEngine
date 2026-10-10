using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TD_Wallpaper.App;

internal sealed class MpvVideoSurface : HwndHost
{
    protected override HandleRef BuildWindowCore(HandleRef parent)
    {
        var window = CreateWindowEx(0, "STATIC", "", 0x40000000 | 0x10000000 | 0x02000000,
            0, 0, 1, 1, parent.Handle, 0, 0, 0);
        if (window == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return new(this, window);
    }
    protected override void DestroyWindowCore(HandleRef window) => DestroyWindow(window.Handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(int extendedStyle, string className, string name, int style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
}