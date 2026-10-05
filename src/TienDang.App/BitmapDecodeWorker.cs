using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TienDang.App;

// WPF imaging creates a MediaContext/dispatcher even on a background thread.
// Reuse one STA dispatcher instead of leaving a fresh context on ThreadPool work.
internal sealed class BitmapDecodeWorker : IDisposable
{
    private readonly TaskCompletionSource<Dispatcher> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _disposed;
    internal BitmapDecodeWorker()
    {
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ready.TrySetResult(dispatcher);
            Dispatcher.Run();
        }) { IsBackground = true, Name = "TienDang bitmap decode" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
    internal async Task<BitmapSource> RunAsync(Func<BitmapSource> decode, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var dispatcher = await _ready.Task.WaitAsync(token);
        return await dispatcher.InvokeAsync(decode, DispatcherPriority.Background, token).Task.WaitAsync(token);
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = _ready.Task.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully && !t.Result.HasShutdownStarted) t.Result.BeginInvokeShutdown(DispatcherPriority.Background);
        }, TaskScheduler.Default);
    }
}