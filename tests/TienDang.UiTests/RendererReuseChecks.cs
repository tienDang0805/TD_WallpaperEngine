using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TienDang.App;

internal static class RendererReuseChecks
{
    internal static int Run(Application app, string manifest)
    {
        var result = 1;
        var surface = new MpvVideoSurface();
        var window = new Window { Title = "TD renderer codec replacement regression", ShowActivated = false, Width = 640, Height = 400, Content = surface };
        window.Loaded += async (_, _) =>
        {
            MpvPlayer? player = null;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var files = document.RootElement.EnumerateArray().Where(e => e.GetProperty("Name").GetString() is "h264-1080p-mp4" or "hevc-1080p-mp4" or "vp9-1080p-webm" or "av1-1080p-webm").ToArray();
                if (files.Length != 4) throw new InvalidOperationException("Four distinct codec fixtures required.");
                player = new MpvPlayer(surface.Handle);
                var pid = 0; string? previous = null;
                for (var i = 0; i < 20; i++)
                {
                    var file = files[i % files.Length]; var path = file.GetProperty("Path").GetString()!;
                    if (i == 0) { await player.StartAsync(path, true, 20, "Fit", true); pid = player.ProcessId; }
                    else await player.ReloadAsync(path, true, 20, "Fit", true, i % 2 == 0 ? 30 : 0);
                    var actual = (await player.GetPropertyAsync("path")).GetString();
                    if (!string.Equals(Path.GetFullPath(actual!), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase) || player.MediaVersion != i + 1 || player.ProcessId != pid)
                        throw new InvalidOperationException("Stale file/event or changed renderer PID.");
                    if (previous != null)
                    {
                        // Exclusive open proves the old media handle was closed.
                        using var released = File.Open(previous, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    }
                    var snapshot = await player.CaptureDiagnosticsAsync();
                    if (snapshot.Codec?.Replace(".", "").Contains(file.GetProperty("Codec").GetString()!, StringComparison.OrdinalIgnoreCase) != true || snapshot.VideoOutput != "gpu" || snapshot.DecodeMode != "hardware" || snapshot.FirstFrameMs == null)
                        throw new InvalidOperationException("New codec/decoded frame/hardware output not confirmed: " + JsonSerializer.Serialize(snapshot));
                    Console.WriteLine($"PASS replacement {i + 1}: {snapshot.Codec}, PID={pid}, frame={snapshot.FirstFrameMs:F1}ms, old file released");
                    previous = path;
                }
                await player.DisposeAsync();
                try { using var process = Process.GetProcessById(pid); if (!process.HasExited) throw new InvalidOperationException("Owned renderer survived disposal."); }
                catch (ArgumentException) { }
                Console.WriteLine("PASS 20 mixed-codec replacements and owned renderer cleanup"); result = 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL renderer reuse " + ex); }
            finally { if (player != null) await player.DisposeAsync(); surface.Dispose(); window.Close(); app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run(); return result;
    }
}
