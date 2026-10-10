using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TienDang.App;

// A layered held frame or committed image covers the shared video window
// while its owned renderer replaces media and prepares a decoded frame.
internal sealed class WallpaperPresenter(Window owner) : IDisposable
{
    private static Window NewLayerWindow() => new()
    {
        Title = "TD-WallpaperEngine Layer", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
        ShowActivated = false, ShowInTaskbar = false, Focusable = false, Background = Brushes.Black,
        Width = 100, Height = 100, WindowStartupLocation = WindowStartupLocation.Manual
    };
    private sealed class Layer(PlayerMessage message) : IDisposable
    {
        private Window? _window;
        internal Window Window { get => _window ??= NewLayerWindow(); set => _window = value; }
        internal nint NativeHandle;
        internal NativeFrameLayer? StaticSlot;
        internal readonly PlayerMessage Message = message;
        internal readonly CancellationTokenSource Lifetime = new();
        internal readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal MpvPlayer? Video;
        internal long MediaVersion;
        internal Image? Image;
        internal bool Disposed, Bridge;
        internal Task Exit = Task.CompletedTask;
        internal nint Handle => NativeHandle != 0 ? NativeHandle : _window == null ? 0 : new WindowInteropHelper(_window).Handle;
        internal void Use(NativeFrameLayer slot)
        {
            if (slot.Busy) throw new InvalidOperationException("Static bridge already in use.");
            StaticSlot = slot; NativeHandle = slot.Handle; slot.Busy = true;
        }
        private void ReleaseVisual()
        {
            if (StaticSlot != null) { StaticSlot.Release(); NativeHandle = 0; }
            else if (NativeHandle != 0) { NativeDesktop.ShowWindow(NativeHandle, 0); NativeHandle = 0; _window?.Close(); }
            else if (_window != null) { _window.Content = null; _window.Close(); }
            Image = null;
        }
        internal async Task RetireAsync()
        {
            if (Disposed) { await Exit; return; }
            Disposed = true; Lifetime.Cancel(); Ready.TrySetCanceled();
            var player = Video; Video = null;
            try
            {
                if (player != null) { var stop = player.DisposeAsync(); Exit = stop.AsTask(); await Exit; }
            }
            finally { ReleaseVisual(); }
        }
        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true; Lifetime.Cancel(); Ready.TrySetCanceled();
            var player = Video; Video = null; player?.Dispose();
            Exit = player?.ExitCompletion ?? Task.CompletedTask;
            ReleaseVisual();
        }
    }
    private Layer? _active, _pending;
    private Layer? _videoLayer;
    internal int RendererReuseCount { get; private set; }
    private readonly HashSet<Layer> _retiring = [];

    private NativeFrameLayer? _bridgeSlot;
    private nint _videoHandle;
    internal int VideoSlotCount => _videoHandle == 0 ? 0 : 1;
    private int _version;
    private bool _disposed, _paused;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly BitmapDecodeWorker _bitmaps = new();
    internal int BridgePixelCount => _active?.Bridge == true && _active.StaticSlot?.Source is BitmapSource b ? b.PixelWidth * b.PixelHeight : 0;
    internal int BridgePaintCount => _active?.Bridge == true ? _active.StaticSlot?.PaintCount ?? 0 : 0;
    internal readonly List<(int Generation, int OldPid, long OldExitTicks, int NewPid, long NewStartTicks)> CleanupEvidence = [];
    private readonly System.Diagnostics.Stopwatch _timeline = System.Diagnostics.Stopwatch.StartNew();
    private PlayerMessage _options = new() { Muted = true, Volume = 30, Fit = "Fill" };
    internal event Action<int>? Loaded;
    internal event Action<int>? Ended;
    internal event Action<int, string>? Failed;
    internal event Action<int, string>? Crashed;
    internal event Action<int, string>? TransitionFailed;
    internal int ActiveGeneration => _active?.Message.Generation ?? 0;
    internal int PendingGeneration => _pending?.Message.Generation ?? 0;
    internal nint ActiveHandle => _active?.Handle ?? 0;
    internal nint PendingHandle => _pending?.Handle ?? 0;
    internal int ActiveProcessId => _active?.Video?.ProcessId ?? 0;
    internal int PendingProcessId => _pending?.Video?.ProcessId ?? 0;

    internal async Task<PlaybackDiagnostics> CaptureDiagnosticsAsync()
    {
        var active = _active; var pending = _pending;
        async Task<PlayerDiagnostics?> Capture(MpvPlayer? video) => video == null ? null : await video.CaptureDiagnosticsAsync();
        var activeTask = Capture(active?.Video);
        var pendingTask = Capture(pending?.Video);
        await Task.WhenAll(activeTask, pendingTask);
        return new PlaybackDiagnostics
        {
            ActiveGeneration = active?.Message.Generation ?? 0, PendingGeneration = pending?.Message.Generation ?? 0,
            WorkerProcessId = Environment.ProcessId, ActiveKind = active == null ? "none" : active.Bridge ? "held-frame" : active.Message.IsVideo ? "video" : "image",
            ActiveVideo = await activeTask, PendingVideo = await pendingTask,
            Error = active != _active || pending != _pending ? "Playback changed while sampling; refresh to sample the current player." : null
        };
    }
    internal async Task LoadAsync(PlayerMessage message)
    {
        if (_disposed) return;
        var version = ++_version;
        _pending?.Lifetime.Cancel();
        var candidate = new Layer(message); _pending = candidate;
        _options = new() { Muted = message.Muted, Volume = message.Volume, Fit = message.Fit, FrameRateLimit = message.FrameRateLimit, LoopVideo = message.LoopVideo };
        var locked = false; var transitionFailure = false;
        try
        {
            await _transition.WaitAsync(candidate.Lifetime.Token); locked = true;
            if (message.Path == null || !File.Exists(message.Path)) throw new FileNotFoundException(L.Text("File wallpaper không còn tồn tại."), message.Path);
            var parent = new WindowInteropHelper(owner).Handle;
            if (!message.IsVideo)
            {
                NativeDesktop.AttachLayer(candidate.Window, parent, _active?.Handle ?? 0);
                candidate.Window.UpdateLayout();
            }
            transitionFailure = true;
            var oldPid = await FreezeOutgoingAsync(candidate, version);
            transitionFailure = false;
            var oldExit = _timeline.ElapsedTicks;
            if (!IsCurrent(candidate, version)) return;
            if (message.IsVideo)
            {
                // Keep the GPU renderer and native HWND; loadfile releases the old decoder.
                if (_videoHandle == 0) _videoHandle = NativeVideoLayer.Create(parent);
                candidate.NativeHandle = _videoHandle;
                NativeDesktop.PositionLayer(candidate.Handle, parent, _active?.Handle ?? 0);
                if (candidate.Handle == 0) throw new InvalidOperationException(L.Text("Không tạo được lớp video."));
                var reused = candidate.Video != null;
                var player = candidate.Video ?? new MpvPlayer(candidate.Handle); candidate.Video = player;
                if (!reused) BindPlayer(player);
                candidate.MediaVersion = player.MediaVersion + 1; _videoLayer = candidate;
                // Decode a first frame silently without advancing the new wallpaper.
                var newStart = _timeline.ElapsedTicks;
                if (reused)
                {
                    await player.ReloadAsync(message.Path, true, message.Volume, message.Fit, true, message.FrameRateLimit).WaitAsync(candidate.Lifetime.Token);
                    RendererReuseCount++;
                }
                else await player.StartAsync(message.Path, true, message.Volume, message.Fit, true, message.FrameRateLimit).WaitAsync(candidate.Lifetime.Token);
                if (oldPid != 0 && !reused) RecordCleanup(message.Generation, oldPid, oldExit, player.ProcessId, newStart);
                await candidate.Ready.Task.WaitAsync(TimeSpan.FromSeconds(25), candidate.Lifetime.Token);
            }
            else
            {
                var bitmap = await _bitmaps.RunAsync(() => LoadBitmap(message.Path, 8_294_400), candidate.Lifetime.Token);
                if (!IsCurrent(candidate, version)) return;
                candidate.Image = new Image { Source = bitmap, Stretch = StretchFor(_options.Fit) };
                candidate.Window.Content = candidate.Image;
                candidate.Window.UpdateLayout();
                await RenderBarrier(candidate.Lifetime.Token);
            }
            if (!IsCurrent(candidate, version)) return;
            if (candidate.Video != null)
            {
                await candidate.Video.SetOptionsAsync(true, _options.Volume, _options.Fit, _options.FrameRateLimit);
            }
            if (!IsCurrent(candidate, version)) return;
            if (_active?.Video != null)
            {
                // Retiring media must not overlap the new media's sound.
                try { await _active.Video.SetOptionsAsync(true, _options.Volume, _options.Fit, _options.FrameRateLimit); }
                catch (Exception) { } // A failed outgoing decoder must not block a valid replacement.
            }
            if (!IsCurrent(candidate, version)) return;
            var outgoing = _active;
            NativeDesktop.PositionLayer(candidate.Handle, parent, 0);
            _active = candidate; _pending = null;
            NativeDesktop.DwmFlush();
            if (outgoing != null) _retiring.Add(outgoing);
            try
            {
                if (candidate.Video != null)
                {
                    await candidate.Video.SetLoopAsync(_options.LoopVideo);
                    await candidate.Video.SetOptionsAsync(_options.Muted, _options.Volume, _options.Fit, _options.FrameRateLimit);
                    await candidate.Video.SetPausedAsync(_paused);
                }
                if (_active == candidate && !_disposed) Loaded?.Invoke(message.Generation);
            }
            finally
            {
                if (outgoing != null)
                {
                    try { await RenderBarrier(CancellationToken.None); }
                    finally { _retiring.Remove(outgoing); await outgoing.RetireAsync(); }
                }
            }
        }
        catch (Exception ex)
        {
            if (_active == candidate && !_disposed) { Failed?.Invoke(message.Generation, ex.Message); return; }
            if (!IsCurrent(candidate, version)) return;
            _pending = null;
            if (transitionFailure) TransitionFailed?.Invoke(message.Generation, ex.Message);
            else if (candidate.Video?.InfrastructureFailure == true || ex is OperationCanceledException or TimeoutException) Crashed?.Invoke(message.Generation, ex.Message);
            else Failed?.Invoke(message.Generation, ex.Message);
            candidate.Dispose();
        }
        finally
        {
            try
            {
                if (_active != candidate) { if (_pending == candidate) _pending = null; await candidate.RetireAsync(); }
            }
            catch (Exception ex) { if (!_disposed && version == _version) Crashed?.Invoke(message.Generation, ex.Message); }
            finally { if (locked) _transition.Release(); }
        }
    }
    private void BindPlayer(MpvPlayer player)
    {
        // Subscribe once per process. Never retain one closure for every wallpaper.
        Layer? Current(long media) => _videoLayer is { Disposed: false } layer && layer.Video == player && layer.MediaVersion == media ? layer : null;
        player.MediaFrameReady += media => Current(media)?.Ready.TrySetResult();
        player.MediaFailed += (media, error) =>
        {
            var layer = Current(media); if (layer == null) return;
            layer.Ready.TrySetException(new IOException(error));
            Dispatch(layer, () =>
            {
                if (_active == layer && !layer.Disposed)
                {
                    if (player.InfrastructureFailure) Crashed?.Invoke(layer.Message.Generation, error);
                    else Failed?.Invoke(layer.Message.Generation, error);
                }
                return Task.CompletedTask;
            });
        };
        player.MediaEnded += media =>
        {
            var layer = Current(media); if (layer == null) return;
            Dispatch(layer, async () =>
            {
                if (_active != layer || layer.Disposed || _disposed) return;
                Ended?.Invoke(layer.Message.Generation);
                // Rotation may synchronously begin the next load. Do not seek it.
                if (_pending != null || _active != layer || player.MediaVersion != media) return;
                await player.RestartAsync();
                if (_active == layer && _pending == null) await player.SetPausedAsync(_paused);
            });
        };
    }
    private void RecordCleanup(int generation, int oldPid, long oldExit, int newPid, long newStart)
    {
        CleanupEvidence.Add((generation, oldPid, oldExit, newPid, newStart));
        if (CleanupEvidence.Count > 64) CleanupEvidence.RemoveAt(0);
    }

    internal void CancelPending()
    {
        ++_version;
        var pending = _pending; _pending = null; pending?.Lifetime.Cancel();
    }
    private bool IsCurrent(Layer layer, int version) => !_disposed && version == _version && _pending == layer && !layer.Disposed;
    private async Task<int> FreezeOutgoingAsync(Layer candidate, int version)
    {
        var outgoing = _active;
        if (outgoing?.Video == null) return 0;
        var oldPlayer = outgoing.Video;
        var oldPid = oldPlayer.ProcessId;
        // Screenshot-to-file avoids transferring a decoded frame through JSON/RAM.
        var path = Path.Combine(Path.GetTempPath(), "TienDang-frame-" + Guid.NewGuid().ToString("N") + ".png");
        Layer? bridge = null;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(candidate.Lifetime.Token);
            budget.CancelAfter(TimeSpan.FromSeconds(4)); await oldPlayer.ScreenshotAsync(path, budget.Token);
            var bitmap = await _bitmaps.RunAsync(() => LoadBitmap(path, 2_073_600), budget.Token); if (!IsCurrent(candidate, version)) return 0;
            bridge = new Layer(outgoing.Message) { Bridge = true };
            var parent = new WindowInteropHelper(owner).Handle;
            bridge.Use(_bridgeSlot ??= new NativeFrameLayer(parent));
            bridge.StaticSlot!.SetFrame(bitmap, _options.Fit);
            NativeDesktop.PositionLayer(bridge.Handle, parent, outgoing.Handle);
            bridge.StaticSlot.Present();
            if (!IsCurrent(candidate, version)) { bridge.Dispose(); return 0; }
            NativeDesktop.PositionLayer(bridge.Handle, parent, 0); bridge.StaticSlot!.Present();
            if (bridge.StaticSlot.PaintCount == 0) throw new IOException("The held frame has not been painted.");
            _active = bridge; NativeDesktop.DwmFlush();
            if (candidate.Message.IsVideo)
            {
                candidate.Video = oldPlayer;
                outgoing.Video = null; outgoing.NativeHandle = 0;
            }
            // Keep the held frame above the renderer throughout media replacement/exit.
            await outgoing.RetireAsync();
            if (IsCurrent(candidate, version) && candidate.Handle != 0) NativeDesktop.PositionLayer(candidate.Handle, parent, bridge.Handle);
            return oldPid;
        }
        catch
        {
            if (bridge != null && _active != bridge) bridge.Dispose();
            // No media replacement may start if the held frame could not be presented.
            throw;
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    internal static BitmapSource LoadBitmap(string path, int maxPixels)
    {
        // Read dimensions before allocating pixels; WIC scales decode into a finite budget.
        using var input = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        var scale = Math.Min(1d, Math.Sqrt((double)maxPixels / ((long)frame.PixelWidth * frame.PixelHeight)));
        var width = Math.Max(1, (int)(frame.PixelWidth * scale));
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        input.Position = 0;
        bitmap.DecodePixelWidth = width; bitmap.StreamSource = input;
        bitmap.EndInit();
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        var stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        var detached = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        detached.Freeze(); return detached;
    }
    private static async Task RenderBarrier(CancellationToken token)
    {
        // Two rendering passes keep the old child alive through the composition
        // that introduces its replacement. A bounded fallback handles occlusion.
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frames = 0;
        EventHandler handler = (_, _) => { if (++frames >= 2) ready.TrySetResult(); };
        CompositionTarget.Rendering += handler;
        try { await Task.WhenAny(ready.Task, Task.Delay(100, token)).WaitAsync(token); }
        finally { CompositionTarget.Rendering -= handler; }
    }
    internal async Task SetOptionsAsync(PlayerMessage options)
    {
        _options = options;
        _active?.StaticSlot?.SetFit(options.Fit);
        if (_active?.Image != null) _active.Image.Stretch = StretchFor(options.Fit);
        if (_pending?.Image != null) _pending.Image.Stretch = StretchFor(options.Fit);
        var active = _active;
        if (active?.Video != null)
        {
            await active.Video.SetLoopAsync(options.LoopVideo);
            await active.Video.SetOptionsAsync(options.Muted, options.Volume, options.Fit, options.FrameRateLimit);
        }
        // A pending player may not have its pipe connected yet. The latest
        // options are applied when it commits; it stays muted while warming.
    }
    internal async Task SetPausedAsync(bool paused)
    {
        _paused = paused;
        var active = _active;
        if (active?.Video != null)
        {
            await active.Video.SetPausedAsync(paused);
        }
    }
    internal void Reposition()
    {
        var parent = new WindowInteropHelper(owner).Handle;
        if (_active != null) NativeDesktop.PositionLayer(_active.Handle, parent, 0, true);
        if (_pending != null && _pending.Handle != 0) NativeDesktop.PositionLayer(_pending.Handle, parent, _active?.Handle ?? 0);
    }
    private void Dispatch(Layer layer, Func<Task> action)
    {
        if (_disposed || owner.Dispatcher.HasShutdownStarted) return;
        owner.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try { if (!_disposed) await action(); }
            catch (Exception ex) { if (!_disposed && _active == layer && !layer.Disposed) Failed?.Invoke(layer.Message.Generation, ex.Message); }
        }));
    }
    private static Stretch StretchFor(string fit) => fit switch { "Fit" => Stretch.Uniform, "Stretch" => Stretch.Fill, _ => Stretch.UniformToFill };
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; ++_version; _bitmaps.Dispose();
        _pending?.Dispose(); _pending = null;
        _active?.Dispose(); _active = null;
        foreach (var layer in _retiring.ToArray()) layer.Dispose();
        _bridgeSlot?.Dispose(); _bridgeSlot = null;
        if (_videoHandle != 0) { NativeVideoLayer.Destroy(_videoHandle); _videoHandle = 0; }
        _retiring.Clear();
    }
}
