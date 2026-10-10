using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TD_Wallpaper.App;

internal static class TransitionChecks
{
    internal static int Run(Application app, string video, string image)
    {
        var result = 1;
        var window = new Window { Title = "TD_Wallpaper transition regression", ShowActivated = false, Width = 640, Height = 400, Background = System.Windows.Media.Brushes.Magenta };
        var presenter = new WallpaperPresenter(window);
        window.Loaded += async (_, _) =>
        {
            try
            {
                await Check(window, presenter, video, image);
                result = 0;
            }
            catch (Exception ex) { Console.WriteLine("FAIL " + ex); }
            finally { presenter.Dispose(); window.Close(); app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run();
        return result;
    }
    private static async Task Check(Window window, WallpaperPresenter presenter, string video, string image)
    {
        var parent = new WindowInteropHelper(window).Handle;
        var loaded = 0; var failed = 0;
        presenter.Loaded += generation => loaded = generation;
        presenter.Failed += (generation, error) => { failed = generation; Console.WriteLine("Expected/diagnostic failure " + generation + ": " + error); };
        PlayerMessage Item(int generation, string path, bool isVideo = false) => new() { Command = "load", Generation = generation, Path = path, IsVideo = isVideo, Muted = true, Volume = 20, Fit = "Fill", FrameRateLimit = isVideo ? 15 : 0 };
        void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
        async Task LoadGuarded(PlayerMessage message, string name)
        {
            var old = presenter.ActiveHandle; var previousGeneration = presenter.ActiveGeneration; var oldProcess = presenter.ActiveProcessId;
            var load = presenter.LoadAsync(message);
            var samples = 0; var timer = Stopwatch.StartNew();
            while (!load.IsCompleted)
            {
                if (timer.Elapsed.TotalSeconds > 30) throw new TimeoutException(name);
                if (presenter.ActiveGeneration == previousGeneration && old != 0)
                {
                    var visible = presenter.ActiveHandle;
                    if (!NativeDesktop.IsWindow(visible) || !NativeDesktop.IsWindowVisible(visible)) throw new InvalidOperationException(name + " lost committed image/frame during load" );
                    if (NativeDesktop.GetWindow(parent, 5) != presenter.ActiveHandle) throw new InvalidOperationException(name + " exposed pending layer before readiness");
                    if (presenter.BridgePixelCount > 0 && presenter.BridgePaintCount == 0) throw new InvalidOperationException(name + " held frame has no synchronous native paint");
                    samples++;
                }
                await Task.Delay(10);
            }
            await load;
            Require(loaded == message.Generation && presenter.ActiveGeneration == message.Generation, name + " commits ready replacement");
            Require(NativeDesktop.IsWindow(presenter.ActiveHandle) && NativeDesktop.GetWindow(parent, 5) == presenter.ActiveHandle, name + " replacement covers parent");
            if (old != 0) Require(samples > 0 && (old == presenter.ActiveHandle || !NativeDesktop.IsWindow(old) || !NativeDesktop.IsWindowVisible(old)), name + " old layer is replaced through a covered handoff");
            if (oldProcess != 0)
            {
                await Task.Delay(100);
                if (message.IsVideo) Require(presenter.ActiveProcessId == oldProcess && Alive(oldProcess), name + " reuses renderer after replacing media");
                else Require(!Alive(oldProcess), name + " video renderer exits when switching to image");
            }
        }
        await LoadGuarded(Item(1, image), "initial image");
        await LoadGuarded(Item(2, image), "image to image");
        await LoadGuarded(Item(3, video, true), "image to video");
        Require(presenter.ActiveProcessId > 0, "video decoder active");
        await LoadGuarded(Item(4, video, true), "video to video");
        await presenter.SetPausedAsync(true);
        await LoadGuarded(Item(5, image), "paused video to image");
        var warming = presenter.LoadAsync(Item(6, video, true));
        await presenter.SetOptionsAsync(new() { Muted = true, Volume = 37, Fit = "Fit", FrameRateLimit = 30 });
        await presenter.SetPausedAsync(true);
        await warming; Require(loaded == 6 && failed != 6, "options and pause during warm-up do not fail load");
        await presenter.SetOptionsAsync(new() { Muted = true, Volume = 37, Fit = "Fit", FrameRateLimit = 30 });
        await presenter.SetPausedAsync(false);
        var validHandle = presenter.ActiveHandle; var validGeneration = presenter.ActiveGeneration;
        await presenter.LoadAsync(Item(7, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4"), true));
        Require(failed == 7 && presenter.ActiveGeneration == validGeneration && presenter.ActiveHandle == validHandle && NativeDesktop.IsWindow(validHandle), "missing target retains prior wallpaper");
        var invalidPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".webm");
        File.WriteAllText(invalidPath, "invalid video fixture");
        try
        {
            var bad = presenter.LoadAsync(Item(8, invalidPath, true));
            while (!bad.IsCompleted)
            {
                if (presenter.ActiveGeneration != validGeneration || NativeDesktop.GetWindow(parent, 5) != presenter.ActiveHandle) throw new InvalidOperationException("invalid decoder removed prior wallpaper");
                await Task.Delay(15);
            }
            await bad;
            Require(failed == 8 && presenter.ActiveGeneration == validGeneration && presenter.BridgePixelCount > 0 && presenter.BridgePixelCount <= 2_073_600 && presenter.ActiveProcessId == 0, "decode failure preserves bounded prior static frame without decoder");
            Require(presenter.BridgePaintCount > 0, "held frame is painted natively before outgoing decoder cleanup");
        }
        finally { File.Delete(invalidPath); }
        var canceled = presenter.LoadAsync(Item(9, video, true));
        var staleProcess = presenter.PendingProcessId;
        var latest = presenter.LoadAsync(Item(10, image));
        await Task.WhenAll(canceled, latest);
        Require(presenter.ActiveGeneration == 10 && loaded == 10 && presenter.PendingGeneration == 0, "rapid loads keep latest generation");
        if (staleProcess != 0) { await Task.Delay(100); Require(!Alive(staleProcess), "superseded pending decoder is closed"); }
        window.Width = 720; window.Height = 450; presenter.Reposition();
        Require(NativeDesktop.GetWindow(parent, 5) == presenter.ActiveHandle, "resize preserves active layer order");
        await presenter.LoadAsync(Item(11, video, true));
        Task? endReplacement = null;
        presenter.Ended += generation => { if (generation == 11) endReplacement = presenter.LoadAsync(Item(12, image)); };
        await presenter.SetPausedAsync(false);
        var eofTimer = Stopwatch.StartNew();
        while (endReplacement == null)
        {
            if (eofTimer.Elapsed.TotalSeconds > 12) throw new TimeoutException("end-of-video handoff");
            await Task.Delay(25);
        }
        await endReplacement;
        Require(presenter.ActiveGeneration == 12 && failed != 12, "EOF handoff does not forward stale decoder errors to replacement");
        await presenter.LoadAsync(Item(13, video, true));
        var finalActive = presenter.ActiveProcessId; var activeWindow = presenter.ActiveHandle;
        var unfinished = presenter.LoadAsync(Item(14, video, true));
        var finalPending = presenter.PendingProcessId; var pendingWindow = presenter.PendingHandle;
        presenter.Dispose(); await unfinished; await Task.Delay(150);
        Require(!NativeDesktop.IsWindow(activeWindow) && !NativeDesktop.IsWindow(pendingWindow) && !Alive(finalActive) && !Alive(finalPending), "dispose closes active/pending layers and players");
        Require(presenter.RendererReuseCount > 0, "video replacements reuse the owned renderer");
        Require(presenter.CleanupEvidence.TrueForAll(e => e.OldExitTicks < e.NewStartTicks && !Alive(e.OldPid)), "non-reused replacements close their old processes");
        foreach (var sample in presenter.CleanupEvidence) Console.WriteLine($"Cleanup generation={sample.Generation} oldPID={sample.OldPid} exitTicks={sample.OldExitTicks} newPID={sample.NewPid} startTicks={sample.NewStartTicks}");
        Console.WriteLine("PASS wallpaper transition continuity suite");
    }
    private static bool Alive(int id)
    {
        if (id == 0) return false;
        try { using var process = Process.GetProcessById(id); return !process.HasExited; } catch (ArgumentException) { return false; }
    }
}
