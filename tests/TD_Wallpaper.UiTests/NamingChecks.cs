using System;
using System.IO;
using TD_Wallpaper.App;

internal static class NamingChecks
{
    internal static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "TD_Wallpaper-naming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Require(App.DefaultDataDirectory(root) == Path.Combine(root, "TD_Wallpaper"), "fresh library uses new name");
        var legacy = Path.Combine(root, "TienDangWallpaper");
        Directory.CreateDirectory(legacy);
        var state = Path.Combine(legacy, "library.json");
        File.WriteAllText(state, "legacy library fixture");
        Require(App.DefaultDataDirectory(root) == legacy && File.ReadAllText(state) == "legacy library fixture", "existing library stays in place");
        Require(App.DataDirectory(["--data-dir", root]) == root, "explicit data directory remains supported");
        Require(Startup.IsOwnedCommand("\"C:\\Programs\\TienDang.Wallpaper.exe\" --startup --minimized"), "recognizes legacy startup");
        Require(Startup.IsOwnedCommand("\"C:\\Programs\\TD_Wallpaper.exe\" --startup --minimized"), "recognizes renamed startup");
        Require(!Startup.IsOwnedCommand("\"C:\\Programs\\other.exe\" --startup") && !Startup.IsOwnedCommand(null), "unrelated startup stays untouched");
        Require(!Startup.IsOwnedCommand("\"C:\\Programs\\other.exe\" \"C:\\Programs\\TienDang.Wallpaper.exe\""), "legacy name in arguments cannot claim startup ownership");
        Require(typeof(App).Assembly.GetName().Name == "TD_Wallpaper", "app assembly renamed");
        Require(typeof(TD_Wallpaper.Core.StateStore).Assembly.GetName().Name == "TD_Wallpaper.Core", "core assembly renamed");
        L.SetLanguage("en");
        Require(L.Text("Taskbar trong suốt") == "Transparent taskbar", "renamed embedded localization still loads");
        L.SetLanguage("vi");
        Console.WriteLine("PASS Naming: library/startup compatibility, assembly names and localization");
        return 0;
    }
    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception("Naming: " + name);
    }
}
