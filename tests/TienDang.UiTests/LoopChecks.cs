using System;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TienDang.App;

internal static class LoopChecks
{
    internal static int Run(Application app, string video)
    {
        var result = 1;
        var window = new Window { Title = "TD video loop regression", Width = 640, Height = 360, ShowActivated = false };
        var presenter = new WallpaperPresenter(window);
        window.Loaded += async (_, _) =>
        {
            try
            {
                var errors = 0; var ends = 0;
                presenter.Failed += (_, _) => errors++;
                presenter.Ended += _ => ends++;
                await presenter.LoadAsync(new() { Command = "load", Path = video, IsVideo = true, LoopVideo = true, Generation = 1, Muted = true });
                var layer = typeof(WallpaperPresenter).GetField("_active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presenter)!;
                var player = (MpvPlayer)layer.GetType().GetField("Video", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(layer)!;
                var pid = player.ProcessId;
                var loop = await player.GetPropertyAsync("loop-file");
                if (loop.GetString() != "inf") throw new Exception("Presenter did not enable native looping.");
                async Task<double> Position() { var value = await player.GetPropertyAsync("time-pos"); if (value.ValueKind != JsonValueKind.Number) throw new Exception("Loop lost current media."); return value.GetDouble(); }
                var wraps = 0; var previous = await Position(); var timer = Stopwatch.StartNew();
                while (wraps < 4)
                {
                    if (timer.Elapsed.TotalSeconds > 12) throw new TimeoutException("Four consecutive native loops.");
                    await Task.Delay(40); var position = await Position();
                    if (previous - position > .4) wraps++;
                    previous = position;
                    if (presenter.ActiveGeneration != 1 || presenter.ActiveProcessId != pid || errors > 0) throw new Exception("Single wallpaper ended or changed renderer.");
                }
                if (ends != 0) throw new Exception("Native loop reported an EOF rotation event.");
                await presenter.SetPausedAsync(true); await Task.Delay(150);
                var paused = await Position(); await Task.Delay(400);
                if (Math.Abs(await Position() - paused) > .1) throw new Exception("Paused looping video kept advancing.");
                await presenter.SetPausedAsync(false); await Task.Delay(200);
                if (Math.Abs(await Position() - paused) < .08) throw new Exception("Loop did not resume after pause.");
                await presenter.SetOptionsAsync(new() { LoopVideo = false, Muted = true, Fit = "Fill" });
                var disabled = await player.GetPropertyAsync("loop-file");
                if (disabled.ValueKind != JsonValueKind.False && !(disabled.ValueKind == JsonValueKind.String && disabled.GetString() == "no")) throw new Exception("EOF rotation did not disable native looping.");
                presenter.Dispose(); await player.ExitCompletion;
                Console.WriteLine("PASS single wallpaper: four native loops, same renderer/media, no EOF rotation, pause/resume, loop policy update and cleanup");
                result = 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL looping " + error); }
            finally { presenter.Dispose(); window.Close(); app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run(); return result;
    }
}
