namespace TienDang.Core;
public static class PerformanceProfiles
{
    public static void Apply(AppSettings settings, string profile)
    {
        settings.PerformanceProfile = profile;
        if (profile == "Custom") return;
        // Original FPS avoids the measured auto-copy/filter CPU penalty on this pipeline.
        settings.FrameRateLimit = 0; settings.PauseFullscreen = profile != "Quality";
        settings.PauseMaximized = profile == "Saver"; settings.PauseOnBattery = profile != "Quality";
        settings.ReleaseWhenBusy = profile == "Saver";
    }
}