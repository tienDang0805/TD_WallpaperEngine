using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TD_Wallpaper.App;

internal static class ResourceIsolationChecks
{
    internal static int Run(Application app, string root)
    {
        var result = 1; var surface = new MpvVideoSurface(); var window = new Window { Width = 640, Height = 400, Content = null };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var directory = Path.Combine(Path.GetTempPath(), "TD_Wallpaper-isolation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
                var fixtures = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts", "stage3-hardware-fixtures.json")));
                var fourK = System.Linq.Enumerable.First(fixtures.RootElement.EnumerateArray(), f => f.GetProperty("Name").GetString() == "h264-4K-mp4").GetProperty("Path").GetString()!;
                var child = new Window { Width = 640, Height = 400, Content = surface, ShowActivated = false, ShowInTaskbar = false };
                NativeDesktop.AttachLayer(child, new System.Windows.Interop.WindowInteropHelper(window).Handle, 0);
                for (var i = 1; i <= 16; i++)
                {
                    if (i > 1) { NativeDesktop.AttachLayer(child, new System.Windows.Interop.WindowInteropHelper(window).Handle, 0); await Task.Delay(30); }
                    var player = new MpvPlayer(surface.Handle);
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    player.FrameReady += () => ready.TrySetResult(); player.Failed += error => ready.TrySetException(new Exception(error));
                    var fit = i % 3 == 0 ? "Fit" : i % 3 == 1 ? "Fill" : "Stretch";
                    await player.StartAsync(fourK, true, 30, fit, true);
                    await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); await player.SetOptionsAsync(true, 30, fit); await player.SetOptionsAsync(true, 30, fit); await player.SetPausedAsync(false); await Task.Delay(400); NativeDesktop.PositionLayer(surface.Handle, NativeDesktop.GetParent(surface.Handle), 0); NativeDesktop.DwmFlush(); await player.ScreenshotAsync(Path.Combine(directory, "frame-" + i + ".png")); using (var metrics = Process.GetProcessById(player.ProcessId)) { metrics.Refresh(); Console.WriteLine("Metrics " + metrics.PrivateMemorySize64 + " " + metrics.HandleCount); } await player.DisposeAsync(); child.Hide();
                    if (i is 4 or 16) Console.WriteLine("MPV 4K screenshot constant HWND " + i + ": " + System.Text.Json.JsonSerializer.Serialize(HandleProbe.Capture(true)));
                }
                surface.Dispose(); child.Content = null; child.Close();
                using var bitmaps = new BitmapDecodeWorker();
                var image = new System.Windows.Controls.Image(); window.Content = image;
                for (var i = 1; i <= 16; i++)
                {
                    image.Source = await bitmaps.RunAsync(() => WallpaperPresenter.LoadBitmap(Path.Combine(directory, "frame-" + i + ".png"), 2_073_600), default);
                    await Task.Delay(100);
                    if (i is 4 or 16) Console.WriteLine("Scaled PNG " + i + ": " + System.Text.Json.JsonSerializer.Serialize(HandleProbe.Capture(true)));
                }
                image.Source = null;
                result = 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL isolation " + ex); }
            finally { window.Close(); app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run(); return result;
    }
}