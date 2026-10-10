using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using TD_Wallpaper.App;

internal static class DesktopLeaseChecks
{
    internal static int Child(string id, string ready)
    {
        using var lease = DesktopRenderLease.Acquire(id, () => false);
        File.WriteAllText(ready, "ready"); Thread.Sleep(Timeout.Infinite); return 0;
    }
    internal static async Task Run()
    {
        var id = "owned-test-" + Guid.NewGuid().ToString("N");
        var ready = Path.Combine(Path.GetTempPath(), "TD-lease-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        foreach (var arg in new[] { "--desktop-lease-child", id, ready }) start.ArgumentList.Add(arg);
        using var child = Process.Start(start) ?? throw new Exception("Lease test child did not start.");
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(ready)) { if (child.HasExited || deadline.Elapsed.TotalSeconds > 10) throw new Exception("Lease child readiness timeout."); await Task.Delay(20); }
            try { using var denied = DesktopRenderLease.Acquire(id, () => false); throw new Exception("Concurrent desktop renderer was allowed."); }
            catch (DesktopBusyException) { Console.WriteLine("PASS desktop lease rejects a concurrent process for the same display"); }
            using (DesktopRenderLease.Acquire(id + "-other", () => false)) Console.WriteLine("PASS desktop lease permits a separate display");
            child.Kill(); await child.WaitForExitAsync();
            using (DesktopRenderLease.Acquire(id, () => false)) Console.WriteLine("PASS desktop lease recovers after renderer process crash");
            using (DesktopRenderLease.Acquire(id, () => false)) Console.WriteLine("PASS desktop lease releases ownership on disposal");
            try { using var denied = DesktopRenderLease.Acquire(id, () => true); throw new Exception("Legacy conflict was allowed."); }
            catch (DesktopBusyException) { Console.WriteLine("PASS desktop lease rejects older uncoordinated renderer"); }
            using (DesktopRenderLease.Acquire(id, () => false)) Console.WriteLine("PASS failed legacy acquisition does not leak ownership");
        }
        finally { if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); } File.Delete(ready); }
    }
}
