using Microsoft.Win32;

namespace TienDang.App;

internal static class Startup
{
    internal const int DelaySeconds = 3;
    internal static TimeSpan LaunchDelay(string[] args) => args.Contains("--startup") && !args.Contains("--wallpaper")
        ? TimeSpan.FromSeconds(DelaySeconds) : TimeSpan.Zero;
    internal static string CommandLine(string exe, string dataDirectory) =>
        "\"" + exe + "\" --startup --minimized --data-dir \"" + dataDirectory + "\"";
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "TienDangWallpaper";
    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(Key); return key?.GetValue(Name) != null; }
    }
    public static void RefreshExisting(string dataDirectory)
    {
        // A custom/test library must never steal the personal startup registration.
        var personal = App.DataDirectory(Array.Empty<string>());
        if (!string.Equals(Path.GetFullPath(dataDirectory), personal, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(Path.GetFileName(Environment.ProcessPath), "TienDang.Wallpaper.exe", StringComparison.OrdinalIgnoreCase)) return;
        if (Enabled) Set(true, dataDirectory);
    }
    public static void Set(bool enabled, string dataDirectory)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException(L.Text("Không tìm thấy app."));
            if (System.IO.Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(L.Text("Hãy chạy bản .exe đã đóng gói để bật khởi động cùng Windows."));
            key.SetValue(Name, CommandLine(exe, dataDirectory));
        }
        else key.DeleteValue(Name, false);
    }
}