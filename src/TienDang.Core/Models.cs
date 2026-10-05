using System.Text.Json;
using System.Text.Json.Serialization;

namespace TienDang.Core;

public sealed class WallpaperItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string OriginalPath { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public bool IsVideo { get; set; }
    public bool Favorite { get; set; }
    public int? DurationSeconds { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    [JsonIgnore] public bool Exists => File.Exists(Path);
    [JsonIgnore] public string Kind => IsVideo ? "VIDEO" : "ẢNH";
    [JsonIgnore] public string Detail => !Exists ? "Không tìm thấy file" : DurationSeconds is { } d ? $"{Kind} · {d}s" : Kind;
    public override string ToString() => Name;
}

public sealed class Playlist
{
    public const string AllId = "all";
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Bộ mới";
    public List<string> ItemIds { get; set; } = [];
    public override string ToString() => Name;
}

public sealed class ScheduleRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Lịch mới";
    public string PlaylistId { get; set; } = Playlist.AllId;
    public string MonitorId { get; set; } = "*";
    public TimeSpan Start { get; set; } = TimeSpan.FromHours(7);
    public TimeSpan End { get; set; } = TimeSpan.FromHours(18);
    public List<DayOfWeek> Days { get; set; } = Enum.GetValues<DayOfWeek>().ToList();
    public bool Enabled { get; set; } = true;

    // Days refer to the start day; equal times mean the entire selected day.
    public bool Matches(DateTime now, string monitorId)
    {
        if (!Enabled || (MonitorId != "*" && MonitorId != monitorId)) return false;
        var time = now.TimeOfDay;
        if (Start == End) return Days.Contains(now.DayOfWeek);
        if (Start < End) return Days.Contains(now.DayOfWeek) && time >= Start && time < End;
        return time >= Start ? Days.Contains(now.DayOfWeek)
            : time < End && Days.Contains(now.AddDays(-1).DayOfWeek);
    }
}

public sealed class MonitorProfile
{
    public string MonitorId { get; set; } = "";
    public string PlaylistId { get; set; } = Playlist.AllId;
    public string? LastItemId { get; set; }
    public double RemainingSeconds { get; set; }
}

public sealed class AppRule
{
    public string ProcessName { get; set; } = "";
    public string Action { get; set; } = "Pause";
}
public sealed class AppSettings
{
    public string Language { get; set; } = "vi";
    public string PerformanceProfile { get; set; } = "Custom";
    public bool ReleaseWhenBusy { get; set; }
    public List<AppRule> AppRules { get; set; } = [];
    public int FrameRateLimit { get; set; }
    public int IntervalSeconds { get; set; } = 600;
    public bool Shuffle { get; set; } = true;
    public bool AdvanceAtVideoEnd { get; set; }
    public bool PauseFullscreen { get; set; } = true;
    public bool PauseMaximized { get; set; }
    public bool PauseOnBattery { get; set; }
    public bool ResumeOnLaunch { get; set; } = true;
    public bool Muted { get; set; } = true;
    public int Volume { get; set; } = 30;
    public string Fit { get; set; } = "Fill";
    public bool CopyOnImport { get; set; }
    public bool WasRunning { get; set; }
    public List<MonitorProfile> Monitors { get; set; } = [];
    public List<ScheduleRule> Schedules { get; set; } = [];
}

public sealed class LibraryState
{
    public int Version { get; set; } = 1;
    public List<WallpaperItem> Items { get; set; } = [];
    public List<Playlist> Playlists { get; set; } = [new() { Id = Playlist.AllId, Name = "Tất cả wallpaper" }];
    public AppSettings Settings { get; set; } = new();

    // Capture on the state-owning thread; background writers only receive this detached graph.
    public LibraryState CreateSnapshot()
    {
        ValidateStructure(); var s = Settings;
        return new LibraryState
        {
            Version = Version,
            Items = Items.Select(i => new WallpaperItem { Id = i.Id, Name = i.Name, Path = i.Path, OriginalPath = i.OriginalPath, SourceUrl = i.SourceUrl,
                IsVideo = i.IsVideo, Favorite = i.Favorite, DurationSeconds = i.DurationSeconds, AddedAt = i.AddedAt }).ToList(),
            Playlists = Playlists.Select(p => new Playlist { Id = p.Id, Name = p.Name, ItemIds = [.. p.ItemIds] }).ToList(),
            Settings = new AppSettings { PerformanceProfile = s.PerformanceProfile, ReleaseWhenBusy = s.ReleaseWhenBusy, AppRules = s.AppRules.Select(r => new AppRule { ProcessName = r.ProcessName, Action = r.Action }).ToList(), Language = s.Language, FrameRateLimit = s.FrameRateLimit, IntervalSeconds = s.IntervalSeconds, Shuffle = s.Shuffle,
                AdvanceAtVideoEnd = s.AdvanceAtVideoEnd, PauseFullscreen = s.PauseFullscreen, PauseMaximized = s.PauseMaximized, PauseOnBattery = s.PauseOnBattery, ResumeOnLaunch = s.ResumeOnLaunch,
                Muted = s.Muted, Volume = s.Volume, Fit = s.Fit, CopyOnImport = s.CopyOnImport, WasRunning = s.WasRunning,
                Monitors = s.Monitors.Select(m => new MonitorProfile { MonitorId = m.MonitorId, PlaylistId = m.PlaylistId, LastItemId = m.LastItemId, RemainingSeconds = m.RemainingSeconds }).ToList(),
                Schedules = s.Schedules.Select(r => new ScheduleRule { Id = r.Id, Name = r.Name, PlaylistId = r.PlaylistId, MonitorId = r.MonitorId, Start = r.Start,
                    End = r.End, Days = [.. r.Days], Enabled = r.Enabled }).ToList() }
        };
    }
    public IReadOnlyList<WallpaperItem> GetItems(string playlistId)
    {
        if (playlistId == Playlist.AllId) return Items;
        var playlist = Playlists.Find(p => p.Id == playlistId);
        if (playlist is null) return [];
        var lookup = Items.ToDictionary(i => i.Id);
        return playlist.ItemIds.Where(lookup.ContainsKey).Select(id => lookup[id]).ToList();
    }

    public string ResolvePlaylist(DateTime now, MonitorProfile profile)
    {
        // Display-specific rules take priority. First rule wins for overlap.
        var rule = Settings.Schedules.FirstOrDefault(r => r.MonitorId == profile.MonitorId && r.Matches(now, profile.MonitorId))
            ?? Settings.Schedules.FirstOrDefault(r => r.MonitorId == "*" && r.Matches(now, profile.MonitorId));
        var id = rule?.PlaylistId ?? profile.PlaylistId;
        return Playlists.Any(p => p.Id == id) ? id : Playlist.AllId;
    }

    // A broken collection is a schema error, not an empty library. Let StateStore recover.
    internal void ValidateStructure()
    {
        if (Items == null || Playlists == null || Settings == null ||
            Items.Any(i => i == null) || Playlists.Any(p => p == null || p.ItemIds == null) ||
            Settings.Monitors == null || Settings.Schedules == null || Settings.AppRules == null || Settings.AppRules.Any(r => r == null) ||
            Settings.Monitors.Any(p => p == null) ||
            Settings.Schedules.Any(r => r == null || r.Days == null))
            throw new JsonException("Library contains a null collection or record.");
    }
    public void Normalize()
    {
        ValidateStructure();
        Items = Items.Where(i => !string.IsNullOrWhiteSpace(i.Id) && !string.IsNullOrWhiteSpace(i.Path)).DistinctBy(i => i.Id).ToList();
        Playlists = Playlists.DistinctBy(p => p.Id).ToList();
        if (!Playlists.Any(p => p.Id == Playlist.AllId)) Playlists.Insert(0, new() { Id = Playlist.AllId, Name = "Tất cả wallpaper" });
        var ids = Items.Select(i => i.Id).ToHashSet();
        foreach (var p in Playlists) p.ItemIds = p.ItemIds.Where(ids.Contains).Distinct().ToList();
        if (Settings.Language is not ("vi" or "en")) Settings.Language = "vi";
        if (Settings.FrameRateLimit is not (0 or 15 or 30 or 60)) Settings.FrameRateLimit = 0;
        if (Settings.PerformanceProfile is not ("Custom" or "Saver" or "Balanced" or "Quality")) Settings.PerformanceProfile = "Custom";
        Settings.AppRules = Settings.AppRules.Where(r => System.Text.RegularExpressions.Regex.IsMatch(r.ProcessName, @"^[\w .-]{1,80}(\.exe)?$")).Take(50).ToList();
        foreach (var rule in Settings.AppRules) if (rule.Action is not ("Pause" or "Release")) rule.Action = "Pause";
        Settings.IntervalSeconds = Math.Clamp(Settings.IntervalSeconds, 5, 604800);
        Settings.Volume = Math.Clamp(Settings.Volume, 0, 100);
        if (Settings.Fit is not ("Fill" or "Fit" or "Stretch")) Settings.Fit = "Fill";
        Settings.Monitors = Settings.Monitors.DistinctBy(p => p.MonitorId).ToList();
        foreach (var item in Items) if (item.DurationSeconds is { } duration && duration < 5) item.DurationSeconds = null;
    }
}

public static class MediaTypes
{
    public static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };
    public static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".wmv", ".mov", ".avi", ".webm" };
    public static string FileDialogFilter => "Wallpaper|" + string.Join(";", Images.Concat(Videos).Select(extension => "*" + extension)) + "|Tất cả file|*.*";
    public static bool Supported(string path) => Images.Contains(System.IO.Path.GetExtension(path)) || Videos.Contains(System.IO.Path.GetExtension(path));
    public static bool IsVideo(string path) => Videos.Contains(System.IO.Path.GetExtension(path));
}