using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace TienDang.App;

// A held video frame must reach the desktop synchronously before MPV exits.
// WPF Rendering events can be suppressed while the desktop is occluded by a game.
internal sealed class NativeFrameLayer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct WindowClass
    {
        public uint Style; public WindowProc Procedure; public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Menu;
        [MarshalAs(UnmanagedType.LPWStr)] public string Name;
    }
    [StructLayout(LayoutKind.Sequential)] private struct PaintInfo
    {
        public nint Dc; public int Erase; public NativeDesktop.Rect Rect; public int Restore, Update;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, Bits;
        public uint Compression, ImageSize; public int XResolution, YResolution; public uint Colors, Important;
    }
    private delegate nint WindowProc(nint hwnd, uint message, nint wp, nint lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassW(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint hwnd, uint message, nint wp, nint lp);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hwnd, out PaintInfo info);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hwnd, ref PaintInfo info);
    [DllImport("user32.dll")] private static extern bool RedrawWindow(nint hwnd, nint rect, nint region, uint flags);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref NativeDesktop.Rect rect, nint brush);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int objectId);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern int StretchDIBits(nint dc, int x, int y, int width, int height, int sx, int sy, int sw, int sh, byte[] pixels, ref BitmapInfo info, uint usage, uint operation);
    private static readonly WindowProc Callback = Paint;
    private static readonly Dictionary<nint, NativeFrameLayer> Layers = [];
    private static readonly ushort Registered = Register();
    private byte[]? _pixels;
    private string _fit = "Fill";
    internal readonly nint Handle;
    internal BitmapSource? Source { get; private set; }
    internal bool Busy;
    internal int PaintCount { get; private set; }
    private static ushort Register()
    {
        var cls = new WindowClass { Style = 3, Procedure = Callback, Name = "TDWallpaperHeldFrame" };
        var atom = RegisterClassW(ref cls);
        if (atom == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        return atom;
    }
    internal NativeFrameLayer(nint parent)
    {
        _ = Registered;
        Handle = CreateWindowExW(0x08000000, "TDWallpaperHeldFrame", "", 0x40000000 | 0x04000000, 0, 0, 1, 1, parent, 0, 0, 0);
        if (Handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        Layers.Add(Handle, this);
    }
    internal void SetFrame(BitmapSource bitmap, string fit)
    {
        if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 2_073_600) throw new ArgumentException("Held frame exceeds pixel budget.");
        Source = bitmap; _pixels = new byte[checked(bitmap.PixelWidth * bitmap.PixelHeight * 4)];
        bitmap.CopyPixels(_pixels, bitmap.PixelWidth * 4, 0); SetFit(fit);
    }
    internal void SetFit(string fit) { _fit = fit; Present(); }
    internal void Present()
    {
        if (!RedrawWindow(Handle, 0, 0, 0x0001 | 0x0100)) throw new Win32Exception(Marshal.GetLastWin32Error());
        NativeDesktop.DwmFlush();
    }
    internal void Release() { NativeDesktop.ShowWindow(Handle, 0); Source = null; _pixels = null; Busy = false; }
    private static nint Paint(nint hwnd, uint message, nint wp, nint lp)
    {
        if (message != 0x000F || !Layers.TryGetValue(hwnd, out var layer)) return DefWindowProcW(hwnd, message, wp, lp);
        var dc = BeginPaint(hwnd, out var paint);
        try
        {
            if (!NativeDesktop.GetClientRect(hwnd, out var rect)) return 0;
            FillRect(dc, ref rect, GetStockObject(4));
            if (layer.Source is not {} bitmap || layer._pixels == null) return 0;
            var scale = layer._fit == "Stretch" ? 0 : layer._fit == "Fit"
                ? Math.Min((double)rect.Right / bitmap.PixelWidth, (double)rect.Bottom / bitmap.PixelHeight)
                : Math.Max((double)rect.Right / bitmap.PixelWidth, (double)rect.Bottom / bitmap.PixelHeight);
            var width = scale == 0 ? rect.Right : (int)Math.Round(bitmap.PixelWidth * scale);
            var height = scale == 0 ? rect.Bottom : (int)Math.Round(bitmap.PixelHeight * scale);
            var info = new BitmapInfo { Size = 40, Width = bitmap.PixelWidth, Height = -bitmap.PixelHeight, Planes = 1, Bits = 32 };
            SetStretchBltMode(dc, 3);
            StretchDIBits(dc, (rect.Right - width) / 2, (rect.Bottom - height) / 2, width, height, 0, 0, bitmap.PixelWidth, bitmap.PixelHeight, layer._pixels, ref info, 0, 0x00CC0020);
            layer.PaintCount++;
            return 0;
        }
        finally { EndPaint(hwnd, ref paint); }
    }
    public void Dispose() { Layers.Remove(Handle); NativeVideoLayer.Destroy(Handle); Source = null; _pixels = null; }
}
