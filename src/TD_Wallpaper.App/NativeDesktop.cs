using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace TD_Wallpaper.App;

public sealed record DisplayInfo(string Id, string Label, int X, int Y, int Width, int Height)
{
    public override string ToString() => Label;
}

internal static class NativeDesktop
{
    internal delegate bool EnumWindowProc(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string? cls, string? name);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowProc callback, nint data);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowProc callback, nint data);
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] internal static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wparam, nint lparam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")] internal static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLong(nint hwnd, int index);
    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLong(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] internal static extern int MapWindowPoints(nint from, nint to, ref Point points, uint count);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int length);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern nint RegisterPowerSettingNotification(nint hwnd, ref Guid setting, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnregisterPowerSettingNotification(nint handle);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);

    public static List<DisplayInfo> Displays() => Forms.Screen.AllScreens.Select((s, i) =>
        new DisplayInfo(s.DeviceName, L.Text($"Màn hình {i + 1}{(s.Primary ? L.Text(" · Chính") : "")} · {s.Bounds.Width}×{s.Bounds.Height}"),
            s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height)).ToList();

    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);

    internal static string ForegroundProcess(DisplayInfo screen)
    {
        var hwnd = GetForegroundWindow();
        if (!EligibleActivityWindow(hwnd) || !GetWindowRect(hwnd, out var rect) || !ContainsCenter(screen, rect)) return "";
        GetWindowThreadProcessId(hwnd, out var pid);
        try { using var process = System.Diagnostics.Process.GetProcessById((int)pid); return process.ProcessName + ".exe"; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return ""; }
    }
    public static bool Fullscreen(DisplayInfo screen) => Covered(screen, false);
    internal static bool Maximized(DisplayInfo screen) => Covered(screen, true);
    private static bool Covered(DisplayInfo screen, bool maximized)
    {
        var found = false;
        EnumWindows((hwnd, _) =>
        {
            if (WindowCovers(hwnd, screen, maximized)) found = true;
            return !found;
        }, 0);
        return found;
    }
    internal static bool WindowCovers(nint hwnd, DisplayInfo screen, bool maximized)
    {
        if (!EligibleActivityWindow(hwnd) || !GetWindowRect(hwnd, out var r)) return false;
        return maximized ? IsZoomed(hwnd) && ContainsCenter(screen, r)
            : r.Left <= screen.X + 2 && r.Top <= screen.Y + 2 &&
              r.Right >= screen.X + screen.Width - 2 && r.Bottom >= screen.Y + screen.Height - 2;
    }
    internal static bool ContainsCenter(DisplayInfo screen, Rect rect)
    {
        var x = ((long)rect.Left + rect.Right) / 2; var y = ((long)rect.Top + rect.Bottom) / 2;
        return x >= screen.X && x < (long)screen.X + screen.Width && y >= screen.Y && y < (long)screen.Y + screen.Height;
    }
    internal static bool EligibleActivityWindow(nint hwnd)
    {
        if (hwnd == 0 || !IsWindowVisible(hwnd) || IsIconic(hwnd) || GetAncestor(hwnd, 2) != hwnd) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId || ((long)GetWindowLong(hwnd, -20) & 0x80) != 0) return false;
        var cls = new StringBuilder(256); GetClassName(hwnd, cls, cls.Capacity);
        if (cls.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        return DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }
    internal static bool OtherRenderer(string displayId)
    {
        var display = Displays().Find(d => d.Id == displayId);
        if (display == null) return false;
        var found = false;
        EnumChildWindows(GetDesktopWindow(), (hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == Environment.ProcessId || !IsWindowVisible(hwnd)) return true;
            var title = new StringBuilder(128); GetWindowText(hwnd, title, title.Capacity);
            if (title.ToString() is not ("TD_Wallpaper Player" or "TD-WallpaperEngine Player")) return true;
            var rootClass = new StringBuilder(128); GetClassName(GetAncestor(hwnd, 2), rootClass, rootClass.Capacity);
            if (rootClass.ToString() is not ("Progman" or "WorkerW")) return true;
            if (GetWindowRect(hwnd, out var rect) && ContainsCenter(display, rect)) found = true;
            return !found;
        }, 0);
        return found;
    }
    private static nint DesktopParent()
    {
        var progman = FindWindow("Progman", null);
        if (progman == 0) throw new InvalidOperationException(L.Text("Không tìm thấy Explorer desktop."));
        SendMessageTimeout(progman, 0x052C, 0xD, 0, 2, 1000, out _);
        SendMessageTimeout(progman, 0x052C, 0xD, 1, 2, 1000, out _);
        // Windows 11 24H2+: the wallpaper WorkerW can be a child of Progman.
        var childWorker = FindWindowEx(progman, 0, "WorkerW", null);
        if (childWorker != 0 && FindWindowEx(childWorker, 0, "SHELLDLL_DefView", null) == 0) return childWorker;
        nint worker = 0;
        EnumWindows((window, _) =>
        {
            if (FindWindowEx(window, 0, "SHELLDLL_DefView", null) != 0)
            {
                var candidate = FindWindowEx(0, window, "WorkerW", null);
                if (candidate != 0 && FindWindowEx(candidate, 0, "SHELLDLL_DefView", null) == 0) worker = candidate;
            }
            return true;
        }, 0);
        if (worker == 0) throw new InvalidOperationException(L.Text("Không tìm thấy lớp wallpaper. Hãy khởi động lại Explorer rồi thử lại."));
        return worker;
    }

    public static nint Attach(Window window, DisplayInfo display)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var parent = DesktopParent();
        var style = (long)GetWindowLong(handle, -16);
        SetWindowLong(handle, -16, (nint)((style & ~0x80000000L) | 0x40000000L));
        var exStyle = (long)GetWindowLong(handle, -20);
        SetWindowLong(handle, -20, (nint)((exStyle & ~0x40000L) | 0x08000080L)); // no activate; tool window
        SetParent(handle, parent);
        if (GetParent(handle) != parent) throw new Win32Exception(Marshal.GetLastWin32Error(), L.Text("Không gắn được wallpaper vào desktop."));
        Position(handle, parent, display);
        return parent;
    }

    internal static void AttachLayer(Window layer, nint parent, nint behind)
    {
        var handle = new WindowInteropHelper(layer).EnsureHandle();
        var style = (long)GetWindowLong(handle, -16);
        SetWindowLong(handle, -16, (nint)((style & ~0x80000000L) | 0x40000000L));
        var exStyle = (long)GetWindowLong(handle, -20);
        SetWindowLong(handle, -20, (nint)((exStyle & ~0x40000L) | 0x08000080L));
        SetParent(handle, parent);
        if (GetParent(handle) != parent) throw new Win32Exception(Marshal.GetLastWin32Error(), L.Text("Không gắn được lớp wallpaper."));
        PositionLayer(handle, parent, behind);
        layer.Show();
        PositionLayer(handle, parent, behind);
    }
    internal static void PositionLayer(nint handle, nint parent, nint behind, bool preserveOrder = false)
    {
        if (!GetClientRect(parent, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!SetWindowPos(handle, behind, 0, 0, rect.Right, rect.Bottom, 0x0010 | 0x0040 | (preserveOrder ? 0x0004u : 0)))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public static void Position(nint handle, nint parent, DisplayInfo display)
    {
        var point = new Point { X = display.X, Y = display.Y };
        MapWindowPoints(0, parent, ref point, 1);
        if (!SetWindowPos(handle, 0, point.X, point.Y, display.Width, display.Height, 0x0010 | 0x0040))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
