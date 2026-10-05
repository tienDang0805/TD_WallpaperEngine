namespace TienDang.App;

public sealed class PlayerMessage
{
    public string Command { get; set; } = "";
    public string? Path { get; set; }
    public bool IsVideo { get; set; }
    public bool Muted { get; set; } = true;
    public int Volume { get; set; }
    public string Fit { get; set; } = "Fill";
    public int FrameRateLimit { get; set; }
    public string Language { get; set; } = "vi";
    public string? Error { get; set; }
    public string? RequestId { get; set; }
    public PlaybackDiagnostics? Diagnostics { get; set; }
    public int Generation { get; set; }
}