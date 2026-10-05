namespace TienDang.App;

// Missing properties stay null: unavailable is not zero or proof of GPU decoding.
public sealed record PlayerDiagnostics
{
    public int ProcessId { get; init; }
    public bool? Paused { get; init; }
    public DateTime SampledAtUtc { get; init; }
    public bool Headless { get; init; }
    public string? Codec { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? SourceFps { get; init; }
    public double? OutputFps { get; init; }
    public string? RequestedHwdec { get; init; }
    public string? HwdecCurrent { get; init; }
    public string? VideoOutput { get; init; }
    public int FrameRateLimit { get; init; }
    public double? FileLoadedMs { get; init; }
    public double? FirstFrameMs { get; init; }
    public long? DecoderDroppedFrames { get; init; }
    public long? OutputDroppedFrames { get; init; }
    public long? MistimedFrames { get; init; }
    public double? PrivateMiB { get; init; }
    public double? WorkingSetMiB { get; init; }
    public int? HandleCount { get; init; }
    public double? CpuSeconds { get; init; }
    public double? CpuPercentAllCores { get; init; }
    public double? CpuSampleSeconds { get; init; }
    public string[] UnavailableProperties { get; init; } = [];
    public string DecodeMode => HwdecCurrent == "no" ? "software" : string.IsNullOrWhiteSpace(HwdecCurrent) ? "unknown" : "hardware";
    public bool SoftwareFallback => !Headless && RequestedHwdec is not (null or "no") && DecodeMode == "software";
}

public sealed record PlaybackDiagnostics
{
    public int ActiveGeneration { get; init; }
    public int PendingGeneration { get; init; }
    public int WorkerProcessId { get; init; }
    public string ActiveKind { get; init; } = "none";
    public PlayerDiagnostics? ActiveVideo { get; init; }
    public PlayerDiagnostics? PendingVideo { get; init; }
    public string? Error { get; init; }
}