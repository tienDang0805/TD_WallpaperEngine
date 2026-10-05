global using System;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows;
using TienDang.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("Use <video>, --render <video> <screenshot.png>, or --desktop <app.exe> <video> <image>.");
            if (args[0] == "--hardware-matrix") return HardwareMatrixChecks.Run(args[1], args[2]);
            if (args[0] == "--fps") { FrameRateChecks.Run(args[1], 0, true).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "--render" || args[0] == "--fps-render")
            {
                var application = new Application();
                var surface = new MpvVideoSurface();
                var window = new Window { Title = "TienDang · Video render check", Width = 720, Height = 450, Content = surface };
                var result = 1;
                window.Loaded += async (_, _) =>
                {
                    try { if (args[0] == "--fps-render") await FrameRateChecks.Run(args[1], surface.Handle, false); else await CheckPlayer(args[1], surface.Handle, false, args[2]); result = 0; }
                    catch (Exception ex) { Console.Error.WriteLine(ex); }
                    finally { window.Close(); }
                };
                application.Run(window);
                return result;
            }
            if (args[0] == "--failure") { Failure(args[1]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "--desktop") Desktop(args[1], args[2], args[3]).GetAwaiter().GetResult();
            else CheckPlayer(args[0], 0, true, null).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
    private static async Task CheckPlayer(string path, nint window, bool headless, string? screenshot)
    {
        using var player = new MpvPlayer(window, headless);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = Channel.CreateUnbounded<bool>();
        var failures = Channel.CreateUnbounded<string>();
        player.Loaded += () => loaded.TrySetResult();
        player.Ended += () => ended.Writer.TryWrite(true);
        player.Failed += error => { loaded.TrySetException(new Exception(error)); failures.Writer.TryWrite(error); };
        await player.StartAsync(path, true, 20, "Fill", true);
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Check((await player.GetPropertyAsync("video-codec")).GetString() != null, "video decoder opened");
        Console.WriteLine("Codec: " + await player.GetPropertyAsync("video-codec"));
        Check((await player.GetPropertyAsync("pause")).GetBoolean(), "initial pause retained");
        await player.SetOptionsAsync(false, 37, "Stretch");
        Check(!(await player.GetPropertyAsync("mute")).GetBoolean(), "unmute");
        Check(Math.Abs((await player.GetPropertyAsync("volume")).GetDouble() - 37) < .01, "volume");
        Check(!(await player.GetPropertyAsync("keepaspect")).GetBoolean(), "stretch");
        await player.SetOptionsAsync(true, 20, "Fill");
        Check((await player.GetPropertyAsync("mute")).GetBoolean(), "mute");
        Check((await player.GetPropertyAsync("panscan")).GetDouble() == 1, "fill crop");
        await player.SetOptionsAsync(true, 20, "Fit");
        Check((await player.GetPropertyAsync("keepaspect")).GetBoolean() && (await player.GetPropertyAsync("panscan")).GetDouble() == 0, "fit");
        await player.SetPausedAsync(false);
        await Task.Delay(700);
        Check((await player.GetPropertyAsync("time-pos")).GetDouble() > .1, "playback advances");
        await player.SetPausedAsync(true);
        await Task.Delay(150);
        var frozen = (await player.GetPropertyAsync("time-pos")).GetDouble();
        await Task.Delay(450);
        Check(Math.Abs((await player.GetPropertyAsync("time-pos")).GetDouble() - frozen) < .06, "pause freezes video");
        if (screenshot != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshot))!);
            Console.WriteLine("GPU: " + await player.GetPropertyAsync("current-vo"));
            await Task.Delay(500);
            await player.ScreenshotAsync(Path.GetFullPath(screenshot));
            Check(new FileInfo(screenshot).Length > 10000, "GPU rendered screenshot");
            Console.WriteLine("GPU: " + await player.GetPropertyAsync("current-vo"));
        }
        var duration = (await player.GetPropertyAsync("duration")).GetDouble();
        await player.SeekAsync(Math.Max(0, duration - .25));
        await player.SetPausedAsync(false);
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15))) await ended.Reader.ReadAsync(timeout.Token);
        Check((await player.GetPropertyAsync("eof-reached")).GetBoolean(), "end event");
        await player.RestartAsync();
        await Task.Delay(350);
        Check(!(await player.GetPropertyAsync("eof-reached")).GetBoolean() &&
            !(await player.GetPropertyAsync("pause")).GetBoolean() &&
            (await player.GetPropertyAsync("time-pos")).GetDouble() < 2, "loop restarts and keeps playing");
        Check(!failures.Reader.TryRead(out var failure), "no player failures" + (failure == null ? "" : ": " + failure));
        var ownedProcess = player.ProcessId;
        player.Dispose();
        await Task.Delay(200);
        Check(!IsRunning(ownedProcess), "mpv disposed without orphan");
    }

    private static async Task Failure(string path)
    {
        using var player = new MpvPlayer(0, true);
        var failure = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.Loaded += () => failure.TrySetException(new Exception("Invalid video was reported loaded."));
        player.Failed += error => failure.TrySetResult(error);
        await player.StartAsync(path, true, 0, "Fit", false);
        var message = await failure.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Check(!string.IsNullOrWhiteSpace(message), "damaged video reports useful failure");
        var id = player.ProcessId;
        player.Dispose();
        await Task.Delay(200);
        Check(!IsRunning(id), "failed player cleaned up");
    }

    private static bool IsRunning(int id)
    {
        try { using var p = Process.GetProcessById(id); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }
    private static async Task Desktop(string app, string video, string image)
    {
        var pipeName = "TienDang-test-" + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(Path.GetFullPath(app)) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--wallpaper"); start.ArgumentList.Add(pipeName);
        start.ArgumentList.Add(System.Windows.Forms.Screen.PrimaryScreen!.DeviceName);
        using var worker = Process.Start(start) ?? throw new Exception("Worker did not start.");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
            await pipe.WaitForConnectionAsync(timeout.Token);
            using var reader = new StreamReader(pipe);
            using var writer = new StreamWriter(pipe) { AutoFlush = true };
            async Task Expect(string expected, int generation = 0)
            {
                var line = await reader.ReadLineAsync(timeout.Token) ?? throw new Exception("Worker disconnected.");
                using var doc = JsonDocument.Parse(line);
                Console.WriteLine("Worker: " + line);
                Check(doc.RootElement.GetProperty("Command").GetString() == expected, "desktop " + expected);
                if (generation != 0) Check(doc.RootElement.GetProperty("Generation").GetInt32() == generation, "load generation");
            }
            async Task Send(string command, int generation = 0, string? path = null, bool isVideo = false)
                => await writer.WriteLineAsync(JsonSerializer.Serialize(new { Command = command, Generation = generation, Path = path, IsVideo = isVideo, Muted = true, Volume = 20, Fit = "Fill" }));
            await Expect("ready");
            await Send("load", 1, video, true); await Expect("loaded", 1);
            await Send("pause"); await Task.Delay(500); await Send("play");
            await Expect("ended", 1);
            // The old video must loop, then switching to an image must discard its pending events.
            await Task.Delay(600);
            await Send("load", 2, image); await Expect("loaded", 2);
            await Send("load", 3, video, true); await Expect("loaded", 3);
            await Send("options"); await Send("close");
            await worker.WaitForExitAsync(timeout.Token);
            Check(worker.ExitCode == 0, "desktop worker closes cleanly");
        }
        finally
        {
            if (!worker.HasExited) worker.Kill();
        }
    }
}