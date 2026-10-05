using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using System.Collections.Concurrent;

namespace TienDang.App;

internal sealed class PlayerHost : IPlayerHost
{
    private readonly NamedPipeServerStream _pipe;
    private readonly Process _process;
    private readonly OwnedProcessJob _job;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PlaybackDiagnostics>> _diagnostics = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _commands = new();
    internal int PendingCommands => _commands.Count;
    internal int ProcessId => _process.Id;
    public Task ExitCompletion { get; private set; } = Task.CompletedTask;
    public event Action<PlayerMessage>? Message;

    private PlayerHost(string displayId, Func<string, ProcessStartInfo>? processStart)
    {
        var name = "TienDang-" + Guid.NewGuid().ToString("N");
        _pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException(L.Text("Không tìm thấy chương trình."));
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--wallpaper"); start.ArgumentList.Add(name); start.ArgumentList.Add(displayId);
        if (processStart != null) start = processStart(name);
        try { _process = Process.Start(start) ?? throw new InvalidOperationException(L.Text("Không mở được player.")); }
        catch { _pipe.Dispose(); throw; }
        try { _job = OwnedProcessJob.Attach(_process); }
        catch { try { _process.Kill(); } catch (InvalidOperationException) { } _process.Dispose(); _pipe.Dispose(); throw; }
    }
    public static async Task<PlayerHost> Start(string displayId, Action<PlayerMessage> callback,
        CancellationToken cancellationToken = default, Func<string, ProcessStartInfo>? processStart = null)
    {
        var host = new PlayerHost(displayId, processStart);
        host.Message += callback;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            await host._pipe.WaitForConnectionAsync(deadline.Token);
            host._writer = new StreamWriter(host._pipe) { AutoFlush = true };
            _ = host.Read();
            await host._ready.Task.WaitAsync(deadline.Token);
            return host;
        }
        catch { host.Dispose(); await host.ExitCompletion; throw; }
    }
    private async Task Read()
    {
        try
        {
            using var reader = new StreamReader(_pipe);
            while (!_disposed)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                var message = JsonSerializer.Deserialize<PlayerMessage>(line);
                if (message == null) continue;
                if (message.Command == "diagnostics" && message.RequestId != null && _diagnostics.TryRemove(message.RequestId, out var request))
                {
                    if (message.Diagnostics != null) request.TrySetResult(message.Diagnostics);
                    else request.TrySetException(new IOException(message.Error ?? "Diagnostics unavailable."));
                    continue;
                }
                if (message.Command == "ack")
                {
                    if (message.RequestId != null && _commands.TryRemove(message.RequestId, out var command))
                    {
                        if (message.Error == null) command.TrySetResult();
                        else command.TrySetException(new IOException(message.Error));
                    }
                    continue; // Late/wrong correlation never becomes a controller event.
                }
                if (message.Command == "ready") _ready.TrySetResult();
                else if (message.Command == "desktop-busy") _ready.TrySetException(new DesktopBusyException());
                else if (message.Command == "fatal") _ready.TrySetException(new InvalidOperationException(message.Error));
                Message?.Invoke(message);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or JsonException)
        { _ready.TrySetException(ex); }
        finally
        {
            foreach (var request in _diagnostics.Values) request.TrySetException(new IOException("Player disconnected."));
            _diagnostics.Clear();
            foreach (var command in _commands.Values) command.TrySetException(new IOException("Player disconnected."));
            _commands.Clear();
            if (!_disposed)
            {
                _ready.TrySetException(new IOException(L.Text("Player đã đóng.")));
                Message?.Invoke(new() { Command = "disconnected", Error = L.Text("Player đã ngắt kết nối.") });
            }
        }
    }
    public async Task<PlaybackDiagnostics> CaptureDiagnosticsAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<PlaybackDiagnostics>(TaskCreationOptions.RunContinuationsAsynchronously);
        _diagnostics[id] = completion;
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            await Send(new() { Command = "diagnostics", RequestId = id }, budget.Token);
            return await completion.Task.WaitAsync(budget.Token);
        }
        finally { _diagnostics.TryRemove(id, out _); }
    }
    public async Task Send(PlayerMessage message, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var id = message.Command == "diagnostics" ? null : Guid.NewGuid().ToString("N");
        TaskCompletionSource? completion = null;
        if (id != null)
        {
            message.RequestId = id;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _commands[id] = completion;
        }
        try
        {
            await _writeLock.WaitAsync(deadline.Token);
            try
            {
                if (_disposed || _writer == null) throw new IOException(L.Text("Player chưa sẵn sàng."));
                await _writer.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), deadline.Token);
            }
            finally { _writeLock.Release(); }
            if (completion != null) await completion.Task.WaitAsync(deadline.Token);
        }
        finally { if (id != null) _commands.TryRemove(id, out _); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _pipe.Dispose(); _job.Dispose();
        foreach (var request in _diagnostics.Values) request.TrySetCanceled();
        _diagnostics.Clear();
        foreach (var command in _commands.Values) command.TrySetCanceled();
        _commands.Clear();
        ExitCompletion = Task.Run(async () =>
        {
            try
            {
                if (!_process.WaitForExit(2000)) _process.Kill();
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _process.WaitForExitAsync(deadline.Token);
            }
            catch (InvalidOperationException) { }
            finally { _process.Dispose(); }
        });
    }
}
