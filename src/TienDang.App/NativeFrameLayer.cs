using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace TienDang.App;

// A held video frame must reach the desktop before media replacement or MPV exit.
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
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    private delegate nint WindowProc(nint hwnd, uint message, nint wp, nint lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassW(ref WindowClass cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint hwnd, uint message, nint wp, nint lp);
    [DllImport("user32.dll")] private static extern nint BeginPaint(nint hwnd, out PaintInfo info);
    [DllImport("user32.dll")] private static extern bool EndPaint(nint hwnd, ref PaintInfo info);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref NativeDesktop.Rect rect, nint brush);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int objectId);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern int StretchDIBits(nint dc, int x, int y, int width, int height, int sx, int sy, int sw, int sh, byte[] pixels, ref BitmapInfo info, uint usage, uint operation);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint hwnd, nint destinationDc, nint position, ref Size size, nint sourceDc, ref NativeDesktop.Point source, uint key, nint blend, uint flags);
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
        // A layered child has an independent DWM bitmap. Plain child GDI paint
        // can disappear with an outgoing D3D swapchain on the Explorer desktop.
        Handle = CreateWindowExW(0x08080000, "TDWallpaperHeldFrame", "", 0x40000000 | 0x04000000, 0, 0, 1, 1, parent, 0, 0, 0);
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
        if (Source == null || _pixels == null) return;
        if (!NativeDesktop.GetClientRect(Handle, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (rect.Right <= 0 || rect.Bottom <= 0) return;
        if ((long)rect.Right * rect.Bottom > 16_777_216) throw new IOException("Held-frame display buffer exceeds 64 MiB.");
        var dc = CreateCompatibleDC(0);
        if (dc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint dib = 0, old = 0;
        try
        {
            var info = new BitmapInfo { Size = 40, Width = rect.Right, Height = -rect.Bottom, Planes = 1, Bits = 32 };
            dib = CreateDIBSection(dc, ref info, 0, out _, 0, 0);
            if (dib == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            old = SelectObject(dc, dib);
            DrawFrame(dc, rect);
            var size = new Size { Width = rect.Right, Height = rect.Bottom };
            var point = new NativeDesktop.Point();
            if (!UpdateLayeredWindow(Handle, 0, 0, ref size, dc, ref point, 0, 0, 4)) throw new Win32Exception(Marshal.GetLastWin32Error());
            PaintCount++;
        }
        finally
        {
            if (old != 0) SelectObject(dc, old);
            if (dib != 0) DeleteObject(dib);
            DeleteDC(dc);
        }
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
            // No managed exception may cross the unmanaged window procedure.
            try { layer.DrawFrame(dc, rect); } catch (IOException) { }
            return 0;
        }
        finally { EndPaint(hwnd, ref paint); }
    }
    private void DrawFrame(nint dc, NativeDesktop.Rect rect)
    {
            FillRect(dc, ref rect, GetStockObject(4));
            if (Source is not {} bitmap || _pixels == null) return;
            var scale = _fit == "Stretch" ? 0 : _fit == "Fit"
                ? Math.Min((double)rect.Right / bitmap.PixelWidth, (double)rect.Bottom / bitmap.PixelHeight)
                : Math.Max((double)rect.Right / bitmap.PixelWidth, (double)rect.Bottom / bitmap.PixelHeight);
            var width = scale == 0 ? rect.Right : (int)Math.Round(bitmap.PixelWidth * scale);
            var height = scale == 0 ? rect.Bottom : (int)Math.Round(bitmap.PixelHeight * scale);
            var info = new BitmapInfo { Size = 40, Width = bitmap.PixelWidth, Height = -bitmap.PixelHeight, Planes = 1, Bits = 32 };
            SetStretchBltMode(dc, 3);
            if (StretchDIBits(dc, (rect.Right - width) / 2, (rect.Bottom - height) / 2, width, height, 0, 0, bitmap.PixelWidth, bitmap.PixelHeight, _pixels, ref info, 0, 0x00CC0020) == -1)
                throw new IOException("Held-frame copy failed.");
    }
    public void Dispose() { Layers.Remove(Handle); NativeVideoLayer.Destroy(Handle); Source = null; _pixels = null; }
}
