using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace TD_Wallpaper.App;

internal sealed partial class MpvPlayer
{
    private readonly Stopwatch _startupClock = new();
    private long _fileLoadedTicks = -1, _firstFrameTicks = -1;
    private readonly object _snapshotGate = new();
    private Task<PlayerDiagnostics>? _snapshotTask;
    private long _lastResourceTimestamp;
    private double _lastCpuSeconds;
    internal int PendingCommandCount => _pending.Count;
    internal Task<PlayerDiagnostics> CaptureDiagnosticsAsync()
    {
        lock (_snapshotGate)
            return _snapshotTask is { IsCompleted: false } ? _snapshotTask : _snapshotTask = CaptureDiagnosticsCoreAsync();
    }
    private async Task<PlayerDiagnostics> CaptureDiagnosticsCoreAsync()
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        budget.CancelAfter(TimeSpan.FromSeconds(4));
        var names = new[] { "video-codec", "width", "height", "container-fps", "estimated-vf-fps", "hwdec", "hwdec-current", "current-vo",
            "decoder-frame-drop-count", "frame-drop-count", "mistimed-frame-count", "pause" };
        var values = new ConcurrentDictionary<string, JsonElement>();
        var unavailable = new ConcurrentBag<string>();
        await Task.WhenAll(names.Select(async name =>
        {
            try { values[name] = await CommandAsync(budget.Token, "get_property", name); }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or IOException or ObjectDisposedException)
            { unavailable.Add(name); }
        }));
        string? Text(string name)
        {
            if (!values.TryGetValue(name, out var v)) return null;
            if (v.ValueKind == JsonValueKind.String) return v.GetString();
            // New mpv builds expose multi-choice options (hwdec) as a node array.
            if (v.ValueKind == JsonValueKind.Array) return string.Join(",", v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()));
            return null;
        }
        double? Number(string name) => values.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
        var resource = CaptureResources();
        var loaded = Interlocked.Read(ref _fileLoadedTicks); var first = Interlocked.Read(ref _firstFrameTicks);
        return new PlayerDiagnostics
        {
            Paused = values.TryGetValue("pause", out var pause) && pause.ValueKind is JsonValueKind.True or JsonValueKind.False ? pause.GetBoolean() : null,
            ProcessId = resource.Pid, SampledAtUtc = DateTime.UtcNow, Headless = _headless,
            Codec = Text("video-codec"), Width = (int?)Number("width"), Height = (int?)Number("height"), SourceFps = Number("container-fps"), OutputFps = Number("estimated-vf-fps"),
            RequestedHwdec = Text("hwdec"), HwdecCurrent = Text("hwdec-current"), VideoOutput = Text("current-vo"), FrameRateLimit = _frameRateLimit,
            FileLoadedMs = loaded < 0 ? null : loaded * 1000d / Stopwatch.Frequency,
            FirstFrameMs = first < 0 ? null : first * 1000d / Stopwatch.Frequency,
            DecoderDroppedFrames = (long?)Number("decoder-frame-drop-count"), OutputDroppedFrames = (long?)Number("frame-drop-count"), MistimedFrames = (long?)Number("mistimed-frame-count"),
            PrivateMiB = resource.Private, WorkingSetMiB = resource.Working, HandleCount = resource.Handles, CpuSeconds = resource.Cpu,
            CpuPercentAllCores = resource.Percent, CpuSampleSeconds = resource.Window, UnavailableProperties = unavailable.Order().ToArray()
        };
    }
    private (int Pid, double? Private, double? Working, int? Handles, double? Cpu, double? Percent, double? Window) CaptureResources()
    {
        try
        {
            var process = _process;
            if (process == null) return default;
            process.Refresh();
            var cpu = process.TotalProcessorTime.TotalSeconds; var now = Stopwatch.GetTimestamp();
            var seconds = _lastResourceTimestamp == 0 ? (double?)null : (now - _lastResourceTimestamp) / (double)Stopwatch.Frequency;
            var percent = seconds >= .1 ? Math.Max(0, (cpu - _lastCpuSeconds) / seconds.Value / Environment.ProcessorCount * 100) : (double?)null;
            _lastResourceTimestamp = now; _lastCpuSeconds = cpu;
            return (process.Id, process.PrivateMemorySize64 / 1048576d, process.WorkingSet64 / 1048576d, process.HandleCount, cpu, percent, seconds);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ObjectDisposedException)
        { return default; }
    }
}