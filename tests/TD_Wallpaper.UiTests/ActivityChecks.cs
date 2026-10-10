using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using TD_Wallpaper.App;
using TD_Wallpaper.Core;

internal static class ActivityChecks
{
    internal static int Child(string pipeName)
    {
        var app = new Application();
        var window = new Window { Title = "TD_Wallpaper owned activity peer", Width = 500, Height = 300, ShowActivated = false };
        window.Loaded += async (_, _) =>
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(5000);
                using var reader = new StreamReader(pipe); using var writer = new StreamWriter(pipe) { AutoFlush = true };
                await writer.WriteLineAsync(new WindowInteropHelper(window).Handle.ToInt64().ToString());
                while (await reader.ReadLineAsync() is {} command)
                {
                    if (command == "maximize") { window.WindowState = WindowState.Normal; window.WindowStyle = WindowStyle.SingleBorderWindow; window.WindowState = WindowState.Maximized; }
                    if (command == "minimize") window.WindowState = WindowState.Minimized;
                    if (command == "restore") { window.WindowState = WindowState.Normal; window.WindowStyle = WindowStyle.SingleBorderWindow; window.Width = 500; window.Height = 300; }
                    if (command == "fullscreen")
                    {
                        window.WindowState = WindowState.Normal; window.WindowStyle = WindowStyle.None;
                        var screen = NativeDesktop.Displays()[0];
                        NativeDesktop.SetWindowPos(new WindowInteropHelper(window).Handle, 0, screen.X, screen.Y, screen.Width, screen.Height, 0x0050);
                    }
                    if (command == "hide") window.Hide();
                    if (command == "show") window.Show();
                    if (command == "close") { await writer.WriteLineAsync("ok"); window.Close(); return; }
                    await writer.WriteLineAsync("ok");
                }
            }
            finally { window.Close(); }
        };
        app.Run(window); return 0;
    }
    internal static int Run(Application app, string root)
    {
        var result = 1;
        var window = new Window { Title = "TD_Wallpaper event pause test", Width = 640, Height = 400 };
        window.Loaded += async (_, _) =>
        {
            try { await Check(new WindowInteropHelper(window).Handle, root); result = 0; }
            catch (Exception ex) { Console.WriteLine("FAIL activity " + ex); }
            finally { window.Close(); app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run(); return result;
    }
    private static async Task Check(nint surface, string root)
    {
        void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS activity " + name); }
        async Task Until(Func<Task<bool>> condition)
        {
            var watch = Stopwatch.StartNew();
            while (!await condition()) { if (watch.Elapsed.TotalSeconds > 5) throw new TimeoutException("activity pause state"); await Task.Delay(10); }
        }
        var directory = Path.Combine(Path.GetTempPath(), "TD_Wallpaper-activity-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "artifacts", "TD_Wallpaper-fps-60.mp4");
        var display = NativeDesktop.Displays()[0];
        var state = new LibraryState { Items = [new() { Id = "video", Name = "test", Path = path, IsVideo = true } ] };
        state.Settings.PauseMaximized = true; state.Settings.PauseFullscreen = true;
        var pipeName = "TD_Wallpaper-activity-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--activity-child"); start.ArgumentList.Add(pipeName);
        using var peer = Process.Start(start)!; using var job = OwnedProcessJob.Attach(peer);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await server.WaitForConnectionAsync(deadline.Token);
        using var reader = new StreamReader(server); using var writer = new StreamWriter(server) { AutoFlush = true };
        var hwnd = (nint)long.Parse((await reader.ReadLineAsync(deadline.Token))!);
        PlayerHost? nativeHost = null;
        using var engine = new WallpaperEngine(state, new StateStore(directory), new PlaybackRuntime
        {
            Automatic = false, Remote = () => false, Battery = () => false, Displays = () => [display],
            // Isolate geometry of the owned peer; user's windows are untouched.
            Fullscreen = screen => NativeDesktop.WindowCovers(hwnd, screen, false), Maximized = screen => NativeDesktop.WindowCovers(hwnd, screen, true),
            StartHost = async (id, callback, token) => nativeHost = await PlayerHost.Start(id, callback, token, name => WorkerChecks.Start(name, id, surface))
        });
        using var watcher = new WindowActivityWatcher(Application.Current.Dispatcher, engine.RequestPolicyRefresh);
        Require(watcher.HookCount == 3, "three real out-of-context hooks installed");
        await engine.Apply("*", Playlist.AllId, "video");
        await Until(() => Task.FromResult(engine.ControllerState[0].Loaded));
        var workerWindow = NativeDesktop.GetWindow(surface, 5);
        Require(workerWindow != 0 && NativeDesktop.GetParent(workerWindow) == surface, "worker attaches to owned test parent");
        NativeDesktop.SetParent(workerWindow, 0);
        await Until(() => Task.FromResult(NativeDesktop.GetParent(workerWindow) == surface));
        Require(engine.ControllerState[0].Loaded, "worker watchdog restores detached parent without losing committed wallpaper");
        var samples = new List<double>();
        async Task Change(string command, bool paused)
        {
            var clock = Stopwatch.StartNew();
            await writer.WriteLineAsync(command); Require(await reader.ReadLineAsync(deadline.Token) == "ok", "peer " + command);
            await Until(async () => (await nativeHost!.CaptureDiagnosticsAsync()).ActiveVideo?.Paused == paused);
            samples.Add(clock.Elapsed.TotalMilliseconds);
        }
        for (var i = 0; i < 10; i++) { await Change("maximize", true); await Change("minimize", false); }
        Require(NativeDesktop.IsIconic(hwnd), "minimized peer is excluded");
        await Change("fullscreen", true);
        Require(NativeDesktop.Fullscreen(display), "fullscreen peer is found by production window enumeration");
        var other = new DisplayInfo("virtual-right", "test", display.X + display.Width, display.Y, display.Width, display.Height);
        Require(!NativeDesktop.WindowCovers(hwnd, other, false) && !NativeDesktop.WindowCovers(hwnd, other, true), "geometry does not pause a different monitor");
        engine.TogglePause(); await Task.Delay(30); await Change("restore", true);
        Require(engine.ManualPaused, "restoring window never overrides manual pause");
        engine.TogglePause(); await Until(async () => (await nativeHost!.CaptureDiagnosticsAsync()).ActiveVideo?.Paused == false);
        state.Settings.PauseFullscreen = false; state.Settings.PauseMaximized = false;
        await Change("maximize", false); await Change("fullscreen", false);
        Require((await nativeHost!.CaptureDiagnosticsAsync()).ActiveVideo?.Paused == false, "disabled policies leave playback running");
        state.Settings.PauseFullscreen = true; await Change("minimize", false); await Change("fullscreen", true);
        await Change("hide", false); await Change("show", true); await Change("close", false);
        await peer.WaitForExitAsync(deadline.Token);
        var sorted = samples.Order().ToArray(); var p95 = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1];
        var trace = engine.PolicyEvidence.ToArray();
        var eventTimes = trace.Select(e => e.EventToAckMs).Order().ToArray();
        var eventP95 = eventTimes[(int)Math.Ceiling(eventTimes.Length * .95) - 1];
        Require(trace.Length >= 20 && trace.Any(e => e.Paused) && trace.Any(e => !e.Paused), "native events produce acknowledged pause and resume without timer polling");
        Console.WriteLine($"Activity samples={sorted.Length} command-to-MPV-pause p95={p95:F2}ms max={sorted.Last():F2}ms event-to-worker-to-MPV-ack p95={eventP95}ms max={trace.Max(e => e.EventToAckMs)}ms");
        File.WriteAllText(Path.Combine(root, "artifacts", "stage3-activity-results.json"), System.Text.Json.JsonSerializer.Serialize(new { SamplesMs = samples, P95Ms = p95, MaxMs = sorted.Last(), EventToAckMs = trace.Select(e => e.EventToAckMs), EventP95Ms = eventP95, HookCount = watcher.HookCount, Screen = display, Scope = "one physical screen, real native peer, PlayerHost pipe, WallpaperWindow worker and MPV; other-monitor geometry simulated; user windows untouched" }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        watcher.Dispose(); Require(watcher.HookCount == 0, "closing watcher removes hooks");
        Require(eventP95 <= 150, "normal-load event-to-worker-to-MPV pause acknowledgement p95 meets 150ms target");
        var decoderPid = (await nativeHost!.CaptureDiagnosticsAsync()).ActiveVideo!.ProcessId;
        var workerPid = nativeHost.ProcessId;
        engine.Dispose(); await nativeHost.ExitCompletion;
        bool Alive(int id) { try { using var process = Process.GetProcessById(id); return !process.HasExited; } catch (ArgumentException) { return false; } }
        Require(!Alive(workerPid) && !Alive(decoderPid), "closing host confirms worker and nested owned decoder exit");
        Console.WriteLine("PASS activity suite");
    }
}