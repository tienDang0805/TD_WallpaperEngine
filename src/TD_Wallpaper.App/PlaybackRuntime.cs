namespace TD_Wallpaper.App;

internal interface IPlayerHost : IDisposable
{
    Task Send(PlayerMessage message, CancellationToken cancellationToken = default);
    Task<PlaybackDiagnostics> CaptureDiagnosticsAsync();
    Task ExitCompletion { get; }
}

// Tests replace OS/transport boundaries while exercising the real controller.
internal sealed class PlaybackRuntime
{
    internal Func<List<DisplayInfo>> Displays = NativeDesktop.Displays;
    internal Func<string, Action<PlayerMessage>, CancellationToken, Task<IPlayerHost>> StartHost =
        async (id, callback, token) => await PlayerHost.Start(id, callback, token);
    internal Func<DisplayInfo, bool> Fullscreen = NativeDesktop.Fullscreen;
    internal Func<DisplayInfo, bool> Maximized = NativeDesktop.Maximized;
    internal Func<bool> Battery = () => System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
    internal Func<bool> Remote = () => System.Windows.Forms.SystemInformation.TerminalServerSession;
    internal Func<double>? Seconds;
    internal Func<DisplayInfo, string> ForegroundProcess = NativeDesktop.ForegroundProcess;
    internal bool Automatic = true;
}

internal sealed record ControllerSnapshot(string DisplayId, string? ActiveId, string? RequestedId,
    int ActiveGeneration, int RequestedGeneration, bool Loaded, bool Pinned, bool Paused,
    bool Holding, bool Fatal, int RestartCount, double RetryAt, double RemainingSeconds);