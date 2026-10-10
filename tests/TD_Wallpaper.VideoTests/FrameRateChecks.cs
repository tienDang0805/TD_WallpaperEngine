using System;
using System.Diagnostics;
using System.Threading.Tasks;
using TD_Wallpaper.App;

internal static class FrameRateChecks
{
    internal static async Task Run(string path, nint handle, bool headless)
    {
        using var player = new MpvPlayer(handle, headless);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.FrameReady += () => ready.TrySetResult();
        player.Failed += error => ready.TrySetException(new Exception(error));
        await player.StartAsync(path, true, 0, "Fill", true, 15);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var original = (await player.GetPropertyAsync("container-fps")).GetDouble();
        Console.WriteLine("Source FPS: " + original);
        foreach (var limit in new[] { 15, 30, 60, 0 })
        {
            await player.SetPausedAsync(true);
            await player.SetOptionsAsync(true, 0, "Fit", limit);
            await player.SeekAsync(0);
            await player.SetPausedAsync(false);
            await Task.Delay(1600);
            var actual = (await player.GetPropertyAsync("estimated-vf-fps")).GetDouble();
            var expected = limit == 0 ? original : Math.Min(original, limit);
            Require(Math.Abs(actual - expected) < 1, $"actual filtered FPS {actual:0.##} matches {expected:0.##}, limit={limit}");
            Console.WriteLine("Filters: " + await player.GetPropertyAsync("vf"));
            Console.WriteLine("hwdec at limit " + limit + ": " + await player.GetPropertyAsync("hwdec-current"));
            var t0 = (await player.GetPropertyAsync("time-pos")).GetDouble();
            var watch = Stopwatch.StartNew(); await Task.Delay(800);
            var delta = (await player.GetPropertyAsync("time-pos")).GetDouble() - t0;
            Require(Math.Abs(delta - watch.Elapsed.TotalSeconds) < .25, "FPS cap preserves playback speed");
        }
        Require((await player.GetPropertyAsync("vf")).GetArrayLength() == 0, "Original removes the FPS filter");
        Console.WriteLine("Video output: " + await player.GetPropertyAsync("current-vo"));
        Console.WriteLine("Hardware decoder: " + await player.GetPropertyAsync("hwdec-current"));
        Require(MpvPlayer.ValidFrameRate(-1) == 0 && MpvPlayer.ValidFrameRate(24) == 0, "unknown FPS limits normalize to Original");
        var pid = player.ProcessId; player.Dispose(); await Task.Delay(200);
        var alive = false;
        try { using var p = Process.GetProcessById(pid); alive = !p.HasExited; } catch (ArgumentException) { }
        Require(!alive, "FPS test cleans up its own decoder");
    }
    private static void Require(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }
}