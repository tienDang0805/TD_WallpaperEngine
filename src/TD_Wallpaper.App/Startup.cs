using Microsoft.Win32;

namespace TD_Wallpaper.App;

internal static class Startup
{
    internal const int DelaySeconds = 3;
    internal static TimeSpan LaunchDelay(string[] args) => args.Contains("--startup") && !args.Contains("--wallpaper")
        ? TimeSpan.FromSeconds(DelaySeconds) : TimeSpan.Zero;
    internal static string CommandLine(string exe, string dataDirectory) =>
        "\"" + exe + "\" --startup --minimized --data-dir \"" + dataDirectory + "\"";
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "TD_Wallpaper";
    private const string LegacyName = "TienDangWallpaper";
    internal static bool IsOwnedCommand(string? command)
    {
        if (command == null || !command.StartsWith('"')) return false;
        var end = command.IndexOf('"', 1);
        if (end < 0) return false;
        var name = Path.GetFileName(command[1..end]);
        return name.Equals("TD_Wallpaper.exe", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("TienDang.Wallpaper.exe", StringComparison.OrdinalIgnoreCase);
    }
    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(Key); return
            IsOwnedCommand(key?.GetValue(Name) as string) || IsOwnedCommand(key?.GetValue(LegacyName) as string); }
    }
    public static void RefreshExisting(string dataDirectory)
    {
        // A custom/test library must never steal the personal startup registration.
        var personal = App.DataDirectory(Array.Empty<string>());
        if (!string.Equals(Path.GetFullPath(dataDirectory), personal, StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(Path.GetFileName(Environment.ProcessPath), "TD_Wallpaper.exe", StringComparison.OrdinalIgnoreCase)) return;
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
        else if (IsOwnedCommand(key.GetValue(Name) as string)) key.DeleteValue(Name, false);
        // Replace only our legacy startup entry; unrelated registry values stay untouched.
        if (IsOwnedCommand(key.GetValue(LegacyName) as string)) key.DeleteValue(LegacyName, false);
    }
}
