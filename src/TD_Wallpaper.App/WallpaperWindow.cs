using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TD_Wallpaper.App;

internal sealed class WallpaperWindow : Window
{
    private readonly WallpaperPresenter _presenter;
    private readonly NamedPipeClientStream _pipe;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _displayId;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(2) };
    private nint _parent;
    private readonly nint _checkParent;
    private int _generation;
    private bool _closing;
    private DesktopRenderLease? _desktopLease;

    public WallpaperWindow(string pipeName, string displayId, nint checkParent = 0)
    {
        _displayId = displayId; _checkParent = checkParent;
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        Title = "TD_Wallpaper Player";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Focusable = false;
        Background = Brushes.Black; Content = new Grid { Background = Brushes.Black };
        _presenter = new(this);
        _presenter.Loaded += generation =>
        {
            _ = Report(new() { Command = "loaded", Generation = generation });
            _ = ReportDiagnostics(null);
        };
        _presenter.Ended += generation => { if (generation == _generation) _ = Report(new() { Command = "ended", Generation = generation }); };
        _presenter.Failed += (generation, error) => _ = Report(new() { Command = "failed", Error = error, Generation = generation });
        _presenter.Crashed += (generation, error) => _ = Report(new() { Command = "decoder-crashed", Error = error, Generation = generation });
        _presenter.TransitionFailed += (generation, error) => _ = Report(new() { Command = "transition-failed", Error = error, Generation = generation });
        Loaded += async (_, _) => await Connect();
        SizeChanged += (_, _) => { if (!_closing && _parent != 0) _presenter.Reposition(); };
        _watch.Tick += async (_, _) =>
        {
            if (_closing) return;
            try
            {
                var display = NativeDesktop.Displays().Find(d => d.Id == _displayId);
                if (display == null) { Close(); return; }
                var hwnd = new WindowInteropHelper(this).Handle;
                if (!NativeDesktop.IsWindow(_parent) || NativeDesktop.GetParent(hwnd) != _parent)
                    _parent = Attach(display);
                else if (_checkParent != 0) NativeDesktop.PositionLayer(hwnd, _parent, 0);
                else NativeDesktop.Position(hwnd, _parent, display);
                _presenter.Reposition();
            }
            catch (Exception ex) { await Report(new() { Command = "fatal", Error = ex.Message }); Close(); }
        };
        Closed += (_, _) =>
        {
            _closing = true; _watch.Stop(); _presenter.Dispose(); _pipe.Dispose();
            _desktopLease?.Dispose(); _desktopLease = null;
            Application.Current.Shutdown();
        };
    }
    private nint Attach(DisplayInfo display)
    {
        if (_checkParent == 0) return NativeDesktop.Attach(this, display);
        // The native test worker renders inside an owned test window, not Explorer.
        NativeDesktop.AttachLayer(this, _checkParent, 0); return _checkParent;
    }

    private async Task Connect()
    {
        try
        {
            await _pipe.ConnectAsync(10000);
            _writer = new StreamWriter(_pipe) { AutoFlush = true };
            var display = NativeDesktop.Displays().Find(d => d.Id == _displayId) ?? throw new InvalidOperationException(L.Text("Màn hình đã ngắt kết nối."));
            if (_checkParent == 0) _desktopLease = DesktopRenderLease.Acquire(_displayId);
            _parent = Attach(display);
            await Report(new() { Command = "ready" });
            _watch.Start();
            var reader = new StreamReader(_pipe);
            while (!_closing)
            {
                var line = await reader.ReadLineAsync();
                if (line is null) break;
                var message = JsonSerializer.Deserialize<PlayerMessage>(line);
                if (message != null) await Handle(message);
            }
        }
        catch (DesktopBusyException ex) { await Report(new() { Command = "desktop-busy", Error = ex.Message }); }
        catch (Exception ex) { await Report(new() { Command = "fatal", Error = ex.Message }); }
        finally { if (!_closing) Close(); }
    }
    private async Task Handle(PlayerMessage message)
    {
        try
        {
            L.SetLanguage(message.Language);
            switch (message.Command)
            {
                case "load":
                    _generation = message.Generation;
                    _ = _presenter.LoadAsync(message);
                    break;
                case "options": await _presenter.SetOptionsAsync(message); break;
                case "pause": await _presenter.SetPausedAsync(true); break;
                case "play": await _presenter.SetPausedAsync(false); break;
                case "diagnostics": _ = ReportDiagnostics(message.RequestId); return;
                case "cancel": _presenter.CancelPending(); break;
                default: throw new InvalidOperationException("Unknown player command.");
                case "close":
                    if (message.RequestId != null) await Report(new() { Command = "ack", RequestId = message.RequestId });
                    Close(); return;
            }
            if (message.RequestId != null) await Report(new() { Command = "ack", RequestId = message.RequestId });
        }
        catch (Exception ex) { await Report(new() { Command = "ack", RequestId = message.RequestId, Error = ex.Message, Generation = _generation }); }
    }
    private async Task ReportDiagnostics(string? requestId)
    {
        try
        {
            var snapshot = await _presenter.CaptureDiagnosticsAsync();
            if (!_closing) await Report(new() { Command = "diagnostics", RequestId = requestId, Generation = snapshot.ActiveGeneration, Diagnostics = snapshot });
        }
        catch (Exception ex)
        {
            if (!_closing) await Report(new() { Command = "diagnostics", RequestId = requestId, Error = ex.Message });
        }
    }
    private async Task Report(PlayerMessage message)
    {
        if (_writer == null || !_pipe.IsConnected) return;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await _writeLock.WaitAsync(deadline.Token); } catch (OperationCanceledException) { _pipe.Dispose(); return; }
        try { await _writer.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), deadline.Token); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) { _pipe.Dispose(); }
        finally { _writeLock.Release(); }
    }
}
