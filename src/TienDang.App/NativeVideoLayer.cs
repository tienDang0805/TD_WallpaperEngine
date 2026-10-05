using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TienDang.App;

// Video pixels belong entirely to MPV; a native child avoids a redundant WPF
// render target between the decoder's D3D swapchain and the wallpaper parent.
internal static class NativeVideoLayer
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll", EntryPoint = "DestroyWindow")] internal static extern bool Destroy(nint hwnd);
    internal static nint Create(nint parent)
    {
        var hwnd = CreateWindowEx(0x08000000, "STATIC", "", 0x40000000 | 0x10000000 | 0x02000000 | 0x04000000, 0, 0, 1, 1, parent, 0, 0, 0);
        if (hwnd == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        return hwnd;
    }
}