using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TienDang.App;
using TienDang.Core;

internal static class ControllerChecks
{
    private sealed class Host(Action<PlayerMessage> callback) : IPlayerHost
    {
        internal readonly List<PlayerMessage> Commands = [];
        internal bool Disposed;
        internal Func<PlayerMessage, Task>? Sending;
        internal void Emit(string command, int generation = 0) => callback(new() { Command = command, Generation = generation, Error = command is "failed" or "disconnected" ? "injected fault" : null });
        public async Task Send(PlayerMessage message, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Commands.Add(message);
            if (Sending != null) await Sending(message);
        }
        public Task<PlaybackDiagnostics> CaptureDiagnosticsAsync() => Task.FromResult(new PlaybackDiagnostics());
        public Task ExitCompletion => Task.CompletedTask;
        public void Dispose() => Disposed = true;
    }
    private sealed class Rig : IDisposable
    {
        internal readonly LibraryState State = new();
        internal readonly List<Host> Hosts = [];
        internal readonly WallpaperItem A, B;
        internal readonly WallpaperEngine Engine;
        internal readonly PlaybackRuntime Runtime;
        internal double Seconds;
        internal readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "TienDang-controller-" + Guid.NewGuid().ToString("N"));
        internal Rig()
        {
            Directory.CreateDirectory(DirectoryPath);
            A = new() { Id = "a", Name = "A user title", Path = Path.Combine(DirectoryPath, "a.mp4"), IsVideo = true, DurationSeconds = 7 };
            B = new() { Id = "b", Name = "B user title", Path = Path.Combine(DirectoryPath, "b.mp4"), IsVideo = true };
            File.WriteAllText(A.Path, "fake transport media"); File.WriteAllText(B.Path, "fake transport media");
            State.Items = [A, B]; State.Settings.Shuffle = false;
            Runtime = new PlaybackRuntime
            {
                Automatic = false, Seconds = () => Seconds, Remote = () => false, Battery = () => false, Fullscreen = _ => false,
                Displays = () => [new("test-monitor", "Test monitor", 0, 0, 1920, 1080)],
                StartHost = (_, callback, token) => { token.ThrowIfCancellationRequested(); var host = new Host(callback); Hosts.Add(host); return Task.FromResult<IPlayerHost>(host); }
            };
            Engine = new(State, new StateStore(DirectoryPath), Runtime);
        }
        internal Host Current => Hosts.Last();
        internal ControllerSnapshot Snapshot => Engine.ControllerState.Single();
        internal async Task Commit() { Current.Emit("loaded", Snapshot.RequestedGeneration); await Drain(); }
        public void Dispose() => Engine.Dispose();
    }
    private static Task Drain() => Task.Delay(30);
    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS controller " + name);
    }
    internal static int Run(Application app)
    {
        var result = 1;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try { await Check(); result = 0; }
            catch (Exception ex) { Console.WriteLine("FAIL controller " + ex); }
            finally { app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        Dispatcher.Run(); return result;
    }
    private static async Task Check()
    {
        L.SetLanguage("en");
        using (var rig = new Rig())
        {
            await rig.Engine.Apply("*", Playlist.AllId, "a");
            Require(rig.Snapshot.ActiveId == null && rig.Snapshot.RequestedId == "a" && !rig.Snapshot.Loaded && rig.State.Settings.Monitors[0].LastItemId == null, "request does not claim playback or persist last-item before loaded");
            rig.Seconds = 5; await rig.Engine.Tick();
            Require(rig.Snapshot.RemainingSeconds == 0 && rig.Current.Commands.Count(c => c.Command == "load") == 1, "pending request does not advance or reload on timer");
            await rig.Commit();
            Require(rig.Snapshot.ActiveId == "a" && rig.Snapshot.RequestedId == null && rig.Snapshot.RemainingSeconds == 7 && rig.State.Settings.Monitors[0].LastItemId == "a", "loaded atomically commits active identity and item duration");
            rig.Seconds += 1; await rig.Engine.Tick();
            var remaining = rig.Snapshot.RemainingSeconds;
            await rig.Engine.NextAll();
            Require(rig.Snapshot.ActiveId == "a" && rig.Snapshot.RequestedId == "b" && rig.Engine.CurrentNames == rig.A.Name && rig.Snapshot.RemainingSeconds == remaining && rig.State.Settings.Monitors[0].LastItemId == "a", "next preserves real active title clock and persisted identity while loading");
            rig.Current.Emit("loaded", rig.Snapshot.ActiveGeneration); rig.Current.Emit("ended", rig.Snapshot.ActiveGeneration); rig.Current.Emit("failed", rig.Snapshot.ActiveGeneration); await Drain();
            Require(rig.Snapshot.RequestedId == "b" && rig.Snapshot.ActiveId == "a", "obsolete loaded ended failed cannot replace current request");
            await rig.Commit();
            Require(rig.Snapshot.ActiveId == "b" && rig.State.Settings.Monitors[0].LastItemId == "b", "replacement commits only with matching acknowledgement");
            await rig.Engine.Pin(rig.A, "*");
            Require(!rig.Snapshot.Pinned && rig.Snapshot.RequestedId == "a", "pin does not claim pending wallpaper is active");
            await rig.Commit(); Require(rig.Snapshot.Pinned, "pin commits on readiness");
            rig.State.Playlists.Add(new() { Id = "bad-only", Name = "bad", ItemIds = ["b"] });
            await rig.Engine.Apply("*", "bad-only", "b");
            rig.Current.Emit("failed", rig.Snapshot.RequestedGeneration); await Drain(); await rig.Engine.Tick();
            Require(rig.Snapshot.ActiveId == "a" && rig.Snapshot.RequestedId == null && rig.Snapshot.Holding && rig.Snapshot.Paused && !rig.Snapshot.Pinned, "all-invalid new collection freezes prior committed wallpaper with explicit holding state");
            var loadCount = rig.Current.Commands.Count(c => c.Command == "load");
            for (var i = 0; i < 10; i++) { rig.Seconds += 1; await rig.Engine.Tick(); }
            Require(rig.Current.Commands.Count(c => c.Command == "load") == loadCount, "all-invalid collection does not retry media endlessly");
            rig.Engine.LibraryChanged(); await rig.Engine.Tick();
            Require(rig.Snapshot.RequestedId == "b", "library update re-enables previously rejected media");
            rig.State.Items.Remove(rig.B); rig.Engine.LibraryChanged(); await rig.Engine.Tick();
            Require(rig.Snapshot.RequestedId == null && rig.Snapshot.ActiveId == "a" && rig.Snapshot.Holding && rig.Current.Commands.Any(c => c.Command == "cancel"), "removing pending media cancels it and retains committed wallpaper");
            rig.Engine.Stop(); rig.Current.Emit("loaded", 999); await Drain();
            Require(!rig.Engine.Running && rig.Current.Disposed, "stop closes owned host and ignores queued callbacks");
        }
        using (var rig = new Rig())
        {
            rig.State.Items = [rig.B];
            await rig.Engine.Apply("*", Playlist.AllId);
            rig.Current.Emit("failed", rig.Snapshot.RequestedGeneration); await Drain(); await rig.Engine.Tick();
            Require(rig.Snapshot.ActiveId == null && rig.Snapshot.RequestedId == null && !rig.Snapshot.Loaded && rig.Current.Disposed, "only-invalid initial collection has no false active player");
            rig.Engine.LibraryChanged(); await rig.Engine.Tick();
            Require(rig.Hosts.Count == 2 && rig.Snapshot.RequestedId == "b", "corrected initial collection can retry after refresh");
        }
        using (var rig = new Rig())
        {
            await rig.Engine.Apply("*", Playlist.AllId, "a"); await rig.Commit();
            var firstHost = rig.Current;
            firstHost.Emit("disconnected"); await Drain();
            Require(rig.Snapshot.RestartCount == 1 && rig.Snapshot.RetryAt == 1 && firstHost.Disposed, "first disconnect schedules bounded one-second backoff");
            rig.Seconds = .99; await rig.Engine.Tick(); Require(rig.Hosts.Count == 1, "no tight restart before backoff expires");
            rig.Seconds = 1; await rig.Engine.Tick();
            Require(rig.Hosts.Count == 2 && rig.Snapshot.RequestedId == "a", "restart requests committed media after delay");
            firstHost.Emit("disconnected"); firstHost.Emit("fatal"); firstHost.Emit("loaded", rig.Snapshot.RequestedGeneration); await Drain();
            Require(rig.Hosts.Count == 2 && !rig.Current.Disposed && rig.Snapshot.RequestedId == "a" && rig.Snapshot.RestartCount == 1, "old worker epoch cannot kill or commit new worker");
            await rig.Commit(); rig.Current.Emit("disconnected"); await Drain();
            Require(rig.Snapshot.RestartCount == 2 && rig.Snapshot.RetryAt == 3, "immediate post-load crash does not reset crash budget");
            rig.Seconds = 3; await rig.Engine.Tick(); await rig.Commit(); rig.Current.Emit("disconnected"); await Drain();
            Require(rig.Snapshot.Fatal && rig.Snapshot.RestartCount == 3, "third unstable crash stops automatic restart");
            rig.Seconds = 100; await rig.Engine.Tick(); Require(rig.Hosts.Count == 3, "fatal state stays bounded across timer ticks");
            await rig.Engine.Apply("*", Playlist.AllId, "b"); await rig.Commit();
            Require(!rig.Snapshot.Fatal && rig.Snapshot.ActiveId == "b" && rig.Snapshot.RestartCount == 0, "explicit apply resets recovery budget");
        }
        using (var rig = new Rig())
        {
            await rig.Engine.Apply("*", Playlist.AllId, "a");
            rig.Seconds = 35; await rig.Engine.Tick();
            Require(rig.Current.Disposed && rig.Snapshot.RestartCount == 1 && rig.Snapshot.RequestedId == null, "unacknowledged load has finite readiness deadline");
            rig.Seconds = 36; await rig.Engine.Tick(); await rig.Commit();
            rig.Current.Emit("disconnected"); await Drain(); rig.Seconds = 38; await rig.Engine.Tick(); await rig.Commit();
            rig.Seconds = 69; await rig.Engine.Tick();
            Require(rig.Snapshot.RestartCount == 0, "only stable thirty-second playback resets crash budget");
        }
        using (var rig = new Rig())
        {
            rig.State.Settings.WasRunning = true;
            rig.State.Settings.Monitors.Add(new() { MonitorId = "test-monitor", LastItemId = "a", RemainingSeconds = 3 });
            await rig.Engine.Restore(); await rig.Commit();
            Require(rig.Snapshot.RemainingSeconds == 3, "restore applies saved remaining time only at loaded commit");
            rig.Current.Sending = _ => throw new OperationCanceledException("injected acknowledgement timeout");
            await rig.Engine.ApplySavedSettings(false);
            Require(rig.Current.Disposed && rig.Snapshot.RestartCount == 1, "command cancellation enters recovery rather than escaping timer indefinitely");
        }
        using (var rig = new Rig())
        {
            await rig.Engine.Apply("*", Playlist.AllId, "a"); await rig.Commit();
            rig.Current.Sending = async message => { if (message.Command == "load") { rig.Current.Emit("failed", message.Generation); await Task.Delay(50); } };
            await rig.Engine.NextAll(); rig.Current.Sending = null; await rig.Engine.Tick();
            Require(rig.Snapshot.RequestedId == "a", "failure arriving before transport acknowledgement still schedules fallback");
            await rig.Commit(); await rig.Engine.Pin(rig.B, "*");
            rig.Current.Emit("decoder-crashed", rig.Snapshot.RequestedGeneration); await Drain();
            rig.Seconds = 1; await rig.Engine.Tick();
            Require(rig.Snapshot.RequestedId == "b", "decoder infrastructure failure retries requested item instead of blacklisting media");
            await rig.Commit(); Require(rig.Snapshot.Pinned && rig.Snapshot.ActiveId == "b", "recovery retains pending pin intent");
            await rig.Engine.Apply("*", Playlist.AllId, "a");
            rig.Current.Emit("transition-failed", rig.Snapshot.RequestedGeneration); await Drain();
            Require(rig.Snapshot.Holding && rig.Snapshot.ActiveId == "b" && rig.Snapshot.RequestedId == null, "frame capture failure keeps prior committed state without false media rejection");
            await rig.Engine.Apply("*", Playlist.AllId, "b");
            Require(rig.Snapshot.RequestedId == "b" && !rig.Snapshot.Holding, "explicit apply reloads prior item after transition failure instead of treating held frame as a ready decoder");
        }
        using (var rig = new Rig())
        {
            var first = new DisplayInfo("test-monitor", "left", -1920, 0, 1920, 1080);
            var second = new DisplayInfo("right", "right", 0, 0, 1920, 1080);
            rig.Runtime.Displays = () => [first, second];
            var blocked = true;
            rig.Runtime.Maximized = screen => blocked && screen.Id == "right";
            rig.State.Settings.PauseMaximized = true;
            await rig.Engine.Apply("*", Playlist.AllId, "a");
            foreach (var host in rig.Hosts) host.Emit("loaded", 1); await Drain();
            rig.Engine.RequestPolicyRefresh(unchecked((uint)Environment.TickCount)); await Drain();
            Require(!rig.Engine.ControllerState[0].Paused && rig.Engine.ControllerState[1].Paused, "maximized policy pauses only the covered monitor");
            rig.Engine.TogglePause(); await Drain(); blocked = false;
            rig.Engine.RequestPolicyRefresh(unchecked((uint)Environment.TickCount)); await Drain();
            Require(rig.Engine.ControllerState.All(s => s.Paused), "window restore cannot override manual pause across monitors");
            rig.Engine.TogglePause(); await Drain();
            rig.Seconds = 1; await rig.Engine.Tick(); var remaining = rig.Engine.ControllerState[0].RemainingSeconds;
            rig.Engine.DisplayPower(true); await Drain(); rig.Seconds = 100; await rig.Engine.Tick();
            Require(rig.Engine.ControllerState.All(s => s.Paused) && rig.Engine.ControllerState[0].RemainingSeconds == remaining, "display-off policy freezes rotation clock");
            rig.Engine.DisplayPower(false); await Drain(); await rig.Engine.Tick();
            Require(rig.Engine.ControllerState.All(s => !s.Paused && s.RequestedId == null) && rig.Engine.ControllerState[0].RemainingSeconds == remaining, "display-on resumes without burst rotation or startup delay");
        }
        using (var rig = new Rig())
        {
            await rig.Engine.Apply("*", Playlist.AllId, "a"); await rig.Commit();
            var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Current.Sending = async message => { if (message.Command == "load") { sent.TrySetResult(); await release.Task; } };
            var apply = rig.Engine.Apply("*", Playlist.AllId, "b"); await sent.Task;
            rig.Engine.Stop(); release.TrySetResult(); await apply;
            Require(!rig.Engine.Running && !rig.State.Settings.WasRunning && rig.Current.Disposed, "stop during outstanding load cannot resurrect a session");
        }
        using (var rig = new Rig())
        {
            await rig.Engine.Apply("*", Playlist.AllId, "a"); await rig.Commit();
            var faulted = rig.Current;
            faulted.Sending = async _ => { faulted.Emit("disconnected"); await Task.Delay(50); throw new IOException("same disconnect canceled command"); };
            await rig.Engine.ApplySavedSettings(false); await Drain();
            Require(rig.Snapshot.RestartCount == 1, "disconnect plus command failure counts as one recovery attempt");
        }
        using (var rig = new Rig())
        {
            await rig.Engine.Apply("*", Playlist.AllId, "a"); await rig.Commit();
            rig.Seconds = 1; await rig.Engine.Tick(); var remaining = rig.Snapshot.RemainingSeconds;
            rig.Engine.SetSessionLocked(true); await Drain(); rig.Seconds = 99; await rig.Engine.Tick();
            Require(rig.Snapshot.Paused && rig.Snapshot.RemainingSeconds == remaining, "session lock pauses immediately and freezes clock");
            rig.Engine.TogglePause(); rig.Engine.SetSessionLocked(false); await Drain();
            Require(rig.Snapshot.Paused && rig.Engine.ManualPaused, "unlock preserves manual pause");
            rig.Engine.TogglePause(); await Drain();
            rig.State.Settings.PauseOnBattery = true; rig.Runtime.Battery = () => true;
            rig.Engine.RequestPolicyRefresh(0); await Drain(); Require(rig.Snapshot.Paused, "battery is an independent pause reason");
            rig.Runtime.Battery = () => false; rig.Runtime.Remote = () => true;
            rig.Engine.RequestPolicyRefresh(0); await Drain(); Require(rig.Snapshot.Paused, "remote connection prevents premature battery resume");
            rig.Runtime.Remote = () => false; rig.Engine.RequestPolicyRefresh(0); await Drain(); await rig.Engine.Tick();
            Require(!rig.Snapshot.Paused && rig.Snapshot.RemainingSeconds == remaining && rig.Snapshot.RequestedId == null, "policy resume keeps remaining time without burst");
            var priorHost = rig.Current;
            rig.Runtime.Displays = () => [new("test-monitor", "Test monitor", -2560, 0, 2560, 1440)];
            await rig.Engine.RefreshDisplaysAsync(); await rig.Commit();
            Require(priorHost.Disposed && rig.Snapshot.ActiveId == "a" && rig.Snapshot.RemainingSeconds == remaining && rig.Snapshot.RestartCount == 0, "resolution change restarts owned host and retains clock without crash penalty");
            await rig.Engine.Pin(rig.B, "*");
            rig.Runtime.Displays = () => [new("test-monitor", "Test monitor", 0, 0, 1920, 1080)];
            await rig.Engine.RefreshDisplaysAsync();
            Require(rig.Snapshot.RequestedId == "b", "display change preserves requested wallpaper instead of reverting to old active item");
            await rig.Commit(); Require(rig.Snapshot.Pinned, "display recovery keeps pin intent");
            var unplugged = rig.Current; rig.Runtime.Displays = () => [];
            await rig.Engine.RefreshDisplaysAsync();
            Require(!rig.Engine.Running && unplugged.Disposed && rig.State.Settings.WasRunning, "simulated unplug stops owned host but remembers resume intent");
            rig.Runtime.Displays = () => [new("test-monitor", "Test monitor", 0, 0, 1920, 1080)];
            await rig.Engine.RefreshDisplaysAsync();
            Require(rig.Snapshot.RequestedId == "b" && rig.Snapshot.ActiveId == null, "simulated replug requests last committed item without premature active state");
            await rig.Commit(); Require(rig.Snapshot.ActiveId == "b", "replug commits once ready");
        }
        Require(L.Text("Bộ không có file hợp lệ · Giữ nền trước đó") == "No usable files in this collection · Keeping the previous wallpaper", "holding state has complete English translation");
        using (var rig = new Rig())
        {
            rig.State.Settings.ReleaseWhenBusy = true;
            await rig.Engine.Apply("*", Playlist.AllId, "a"); await rig.Commit(); var remaining = rig.Snapshot.RemainingSeconds; var prior = rig.Current;
            rig.Runtime.Fullscreen = _ => true; rig.Engine.RequestPolicyRefresh(0); await Drain();
            Require(prior.Disposed && rig.Snapshot.Paused && !rig.Snapshot.Loaded, "saver releases owned host instead of merely pausing it");
            rig.Runtime.Fullscreen = _ => false; rig.Engine.RequestPolicyRefresh(0); await Drain(); await rig.Commit();
            Require(rig.Snapshot.ActiveId == "a" && rig.Snapshot.RemainingSeconds == remaining, "saver resumes same wallpaper and remaining clock");
            rig.State.Settings.ReleaseWhenBusy = false; rig.State.Settings.AppRules.Add(new() { ProcessName = "game.exe", Action = "Release" });
            rig.Runtime.ForegroundProcess = _ => "game.exe"; rig.Engine.RequestPolicyRefresh(0); await Drain();
            Require(rig.Current.Disposed && rig.Snapshot.Paused, "explicit application release rule applies");
            rig.Runtime.ForegroundProcess = _ => ""; await rig.Engine.Tick(); await Drain(); await rig.Commit();
            Require(rig.Snapshot.Loaded && !rig.Snapshot.Paused, "poll fallback restores application rule without counting a crash");
        }
        using (var rig = new Rig())
        {
            var starts = 0; var notice = "";
            rig.Runtime.StartHost = (_, _, _) => { starts++; throw new DesktopBusyException(); };
            rig.Engine.Problem += text => notice = text;
            await rig.Engine.Pin(rig.A, "*");
            for (var i = 0; i < 10; i++) { rig.Seconds += 10; await rig.Engine.Tick(); }
            Require(starts == 1 && rig.Snapshot.Fatal && rig.Snapshot.RestartCount == 0 && rig.Snapshot.RequestedId == null && notice.Contains("system tray"), "desktop ownership conflict gives actionable English notice without retry loop");
            rig.Runtime.StartHost = (_, callback, _) => { starts++; var host = new Host(callback); rig.Hosts.Add(host); return Task.FromResult<IPlayerHost>(host); };
            await rig.Engine.Pin(rig.A, "*"); await rig.Commit();
            Require(rig.Snapshot.Loaded && rig.Snapshot.Pinned && !rig.Snapshot.Fatal, "explicit retry resumes after other instance exits");
        }
        Console.WriteLine("PASS controller suite");
    }
}
