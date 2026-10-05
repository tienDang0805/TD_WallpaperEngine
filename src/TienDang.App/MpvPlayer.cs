using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;

namespace TienDang.App;

/// <summary>One isolated process per load prevents stale events reaching the next video.</summary>
internal sealed partial class MpvPlayer : IDisposable, IAsyncDisposable
{
    private readonly string _pipeName = "TienDang-mpv-" + Guid.NewGuid().ToString("N");
    private readonly NamedPipeClientStream _pipe;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _errorLock = new();
    private readonly bool _headless;
    private readonly nint _window;
    private Process? _process;
    private int _processId;
    private OwnedProcessJob? _job;
    private StreamWriter? _writer;
    private int _requestId;
    private volatile bool _disposed;
    private Task? _exitTask;
    internal Task ExitCompletion => _exitTask ?? Task.CompletedTask;
    private bool _failureReported;
    internal bool InfrastructureFailure { get; private set; }
    private int _frameRateLimit;
    private string _diagnostic = "";
    internal int ProcessId => _processId;
    public event Action? Loaded;
    public event Action? FrameReady;
    public event Action? Ended;
    public event Action<string>? Failed;

    public MpvPlayer(nint window, bool headless = false)
    {
        _window = window; _headless = headless;
        _pipe = new(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    }
    public Task StartAsync(string path, bool muted, int volume, string fit, bool paused, int frameRateLimit = 0) => StartCoreAsync(path, muted, volume, fit, paused, false, frameRateLimit);
    internal Task StartUrlAsync(Uri url, bool muted, int volume, string fit, bool paused) => StartCoreAsync(url.Scheme is "http" or "https" && string.IsNullOrEmpty(url.UserInfo) ? url.AbsoluteUri : throw new ArgumentException("HTTP/HTTPS URL required."), muted, volume, fit, paused, true, 0);
    private async Task StartCoreAsync(string path, bool muted, int volume, string fit, bool paused, bool remote, int frameRateLimit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var executable = Path.Combine(AppContext.BaseDirectory, "Player", "mpv.exe");
        if (!File.Exists(executable)) throw new FileNotFoundException(L.Text("Thiếu Player\\mpv.exe. Giải nén đầy đủ thư mục app."), executable);
        if (!remote) path = Path.GetFullPath(path);
        if (!remote && !File.Exists(path)) throw new FileNotFoundException(L.Text("File video không còn tồn tại."), path);
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true, RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var argument in new[]
        {
            "--no-config", "--load-scripts=no", "--input-default-bindings=no", "--input-vo-keyboard=no",
            "--input-cursor=no", "--osc=no", "--osd-level=0", "--terminal=yes", "--msg-level=all=warn",
            "--idle=yes", "--keep-open=yes", "--keep-open-pause=no", "--loop-file=no", "--stop-screensaver=no",
            "--audio-display=no", "--drag-and-drop=no", "--focus-on=never", "--taskbar-progress=no", "--input-ipc-server=" + _pipeName,
            "--mute=" + (muted ? "yes" : "no"), "--volume=" + Math.Clamp(volume, 0, 100),
            "--pause=" + (paused ? "yes" : "no"),
            "--keepaspect=" + (fit == "Stretch" ? "no" : "yes"), "--panscan=" + (fit == "Fill" ? "1" : "0")
        }) info.ArgumentList.Add(argument);
        if (_headless)
        {
            info.ArgumentList.Add("--vo=null"); info.ArgumentList.Add("--ao=null"); info.ArgumentList.Add("--hwdec=no");
        }
        else
        {
            info.ArgumentList.Add("--wid=" + _window.ToInt64()); info.ArgumentList.Add("--vo=gpu");
            info.ArgumentList.Add("--gpu-api=d3d11"); info.ArgumentList.Add("--gpu-context=d3d11"); info.ArgumentList.Add("--hwdec=auto");
        }
        _frameRateLimit = ValidFrameRate(frameRateLimit);
        if (_frameRateLimit != 0)
        {
            // CPU lavfi requires readable frames; copy-mode keeps supported hardware decoding.
            if (!_headless) info.ArgumentList.Add("--hwdec=auto-copy");
            info.ArgumentList.Add("--vf=" + FrameRateFilter(_frameRateLimit));
        }
        _startupClock.Start();
        _process = new Process { StartInfo = info, EnableRaisingEvents = true };
        _process.ErrorDataReceived += (_, e) => AddDiagnostic(e.Data);
        _process.OutputDataReceived += (_, e) => AddDiagnostic(e.Data);
        _process.Exited += (_, _) => Fail(L.Text("Bộ phát video đã thoát. ") + Diagnostic(), true);
        if (!_process.Start()) throw new InvalidOperationException(L.Text("Không khởi động được bộ phát video."));
        _processId = _process.Id;
        _job = OwnedProcessJob.Attach(_process);
        _process.BeginErrorReadLine(); _process.BeginOutputReadLine();
        try
        {
            await _pipe.ConnectAsync(15000, _lifetime.Token);
            _writer = new(_pipe) { AutoFlush = true };
            _ = ReadEvents();
            await CommandAsync("request_log_messages", "warn");
            await CommandAsync("observe_property", 1, "eof-reached");
            await CommandAsync("loadfile", path, "replace");
        }
        catch (Exception ex) when (!_disposed)
        { throw new InvalidOperationException(L.Text("Không mở được bộ phát video: ") + ex.Message + " " + Diagnostic(), ex); }
    }
    public Task SetPausedAsync(bool paused) => CommandAsync("set_property", "pause", paused);
    public async Task SetOptionsAsync(bool muted, int volume, string fit, int frameRateLimit = 0)
    {
        await CommandAsync("set_property", "mute", muted);
        await CommandAsync("set_property", "volume", Math.Clamp(volume, 0, 100));
        await CommandAsync("set_property", "keepaspect", fit != "Stretch");
        await CommandAsync("set_property", "panscan", fit == "Fill" ? 1 : 0);
        await SetFrameRateLimitAsync(frameRateLimit);
    }
    internal static int ValidFrameRate(int limit) => limit is 15 or 30 or 60 ? limit : 0;
    internal static string FrameRateFilter(int limit) => "@wallpaper-fps:lavfi=[fps=fps='if(gt(source_fps,0),min(source_fps," + ValidFrameRate(limit) + ")," + ValidFrameRate(limit) + ")']";
    internal async Task SetFrameRateLimitAsync(int limit)
    {
        limit = ValidFrameRate(limit);
        if (_frameRateLimit == limit) return;
        if (limit != 0 && !_headless) await CommandAsync("set_property", "hwdec", "auto-copy");
        await CommandAsync("vf", "set", limit == 0 ? "" : FrameRateFilter(limit));
        if (limit == 0 && !_headless) await CommandAsync("set_property", "hwdec", "auto");
        _frameRateLimit = limit;
    }
    public Task RestartAsync() => CommandAsync("seek", 0, "absolute+exact");
    internal Task<JsonElement> GetPropertyAsync(string name) => CommandAsync("get_property", name);
    internal Task ScreenshotAsync(string path, CancellationToken token = default) => CommandAsync(token, "screenshot-to-file", path, "video");
    internal Task SeekAsync(double seconds) => CommandAsync("seek", seconds, "absolute+exact");
    private Task<JsonElement> CommandAsync(params object[] command) => CommandAsync(CancellationToken.None, command);
    private async Task<JsonElement> CommandAsync(CancellationToken cancellationToken, params object[] command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var id = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        try
        {
            await _writeLock.WaitAsync(deadline.Token);
            try
            {
                if (_writer is null) throw new InvalidOperationException(L.Text("Bộ phát chưa kết nối."));
                await _writer.WriteLineAsync(JsonSerializer.Serialize(new { command, request_id = id }).AsMemory(), deadline.Token);
            }
            finally { _writeLock.Release(); }
            return await completion.Task.WaitAsync(deadline.Token);
        }
        finally { _pending.TryRemove(id, out _); }
    }
    private async Task ReadEvents()
    {
        try
        {
            using var reader = new StreamReader(_pipe, System.Text.Encoding.UTF8, true, 1024, true);
            while (!_disposed)
            {
                var line = await reader.ReadLineAsync(_lifetime.Token);
                if (line is null) break;
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("request_id", out var request) && _pending.TryRemove(request.GetInt32(), out var completion))
                {
                    var error = root.GetProperty("error").GetString();
                    if (error == "success") completion.TrySetResult(root.TryGetProperty("data", out var data) ? data.Clone() : default);
                    else completion.TrySetException(new InvalidOperationException("mpv: " + error + ". " + Diagnostic()));
                }
                if (!root.TryGetProperty("event", out var eventName)) continue;
                switch (eventName.GetString())
                {
                    case "log-message": AddDiagnostic(root.GetProperty("text").GetString()); break;
                    case "file-loaded": Interlocked.CompareExchange(ref _fileLoadedTicks, _startupClock.ElapsedTicks, -1); Loaded?.Invoke(); break;
                    case "playback-restart": Interlocked.CompareExchange(ref _firstFrameTicks, _startupClock.ElapsedTicks, -1); FrameReady?.Invoke(); break;
                    case "property-change":
                        if (root.TryGetProperty("name", out var name) && name.GetString() == "eof-reached" &&
                            root.TryGetProperty("data", out var value) && value.ValueKind == JsonValueKind.True) Ended?.Invoke();
                        break;
                    case "end-file":
                        if (root.TryGetProperty("reason", out var reason) && reason.GetString() == "error")
                            Fail(L.Text("Không giải mã được video: ") + (root.TryGetProperty("file_error", out var fileError) ? fileError.GetString() : L.Text("lỗi bộ phát")) + ". " + Diagnostic());
                        break;
                }
            }
            if (!_disposed) Fail(L.Text("Mất kết nối với bộ phát video. ") + Diagnostic(), true);
        }
        catch (Exception ex) when (!_disposed) { Fail(L.Text("Lỗi kết nối bộ phát: ") + ex.Message, true); }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }
    private void AddDiagnostic(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_errorLock)
        {
            _diagnostic += line + "\n";
            if (_diagnostic.Length > 3000) _diagnostic = _diagnostic[^3000..];
        }
    }
    private string Diagnostic() { lock (_errorLock) return _diagnostic.Trim(); }
    private void Fail(string message, bool infrastructure = false)
    {
        lock (_errorLock)
        {
            if (_disposed || _failureReported) return;
            _failureReported = true; InfrastructureFailure = infrastructure;
        }
        foreach (var item in _pending.Values) item.TrySetException(new IOException(message));
        Failed?.Invoke(message);
    }
    public void Dispose() => BeginDispose(false);
    private void BeginDispose(bool graceful)
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        foreach (var item in _pending.Values) item.TrySetCanceled();
        _pending.Clear();
        if (!graceful)
        {
            _pipe.Dispose();
            try { if (_process is { HasExited: false }) _process.Kill(); }
            catch (InvalidOperationException) { }
            _job?.Dispose(); _job = null;
        }
        _exitTask = StopCoreAsync(graceful);
    }
    private async Task StopCoreAsync(bool graceful)
    {
        var process = _process;
        try
        {
            if (process == null) return;
            if (graceful && !process.HasExited && _writer != null)
            {
                // Keep the native surface alive while MPV tears down D3D resources.
                // An unresponsive quit is bounded, then the private job is terminated.
                using var quit = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    await _writeLock.WaitAsync(quit.Token);
                    try { await _writer.WriteLineAsync(JsonSerializer.Serialize(new { command = new object[] { "quit" } }).AsMemory(), quit.Token); }
                    finally { _writeLock.Release(); }
                    await process.WaitForExitAsync(quit.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException or ObjectDisposedException) { }
            }
            if (!process.HasExited) process.Kill();
            _job?.Dispose(); _job = null;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (InvalidOperationException) { /* Already exited/not started. */ }
        finally
        {
            _job?.Dispose(); _job = null; _pipe.Dispose();
            process?.Dispose();
        }
    }
    public async ValueTask DisposeAsync()
    {
        BeginDispose(true);
        await ExitCompletion;
    }
}