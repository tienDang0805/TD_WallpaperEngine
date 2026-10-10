using System.Text.Json;
using TD_Wallpaper.Core;
internal static class SnapshotChecks
{
    internal static void Run()
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var directory = Path.Combine(Path.GetTempPath(), "TD_Wallpaper-snapshots-" + Guid.NewGuid().ToString("N"));
        var state = new LibraryState { Items = [new WallpaperItem { Id = "item", Name = "Tên riêng ✓", Path = "a.jpg", OriginalPath = "old.jpg", SourceUrl = "https://example.com/a.jpg", Favorite = true, DurationSeconds = 15 }] };
        state.Playlists.Add(new Playlist { Id = "custom", Name = "Own collection", ItemIds = ["item"] });
        state.Settings = new AppSettings { TransparentTaskbar = true, Language = "en", FrameRateLimit = 30, IntervalSeconds = 60, Shuffle = false, AdvanceAtVideoEnd = true,
            PauseFullscreen = false, PauseMaximized = true, PauseOnBattery = true, ResumeOnLaunch = false, Muted = false, Volume = 80, Fit = "Fit", CopyOnImport = true, WasRunning = true,
            Monitors = [new MonitorProfile { MonitorId = "screen", PlaylistId = "custom", LastItemId = "item", RemainingSeconds = 12.5 }],
            Schedules = [new ScheduleRule { Id = "rule", Name = "Evening", PlaylistId = "custom", MonitorId = "screen", Days = [DayOfWeek.Friday], Start = TimeSpan.FromHours(17), End = TimeSpan.FromHours(21), Enabled = false }] };
        var snapshot = state.CreateSnapshot(); var snapshotJson = JsonSerializer.Serialize(snapshot);
        Check(snapshotJson == JsonSerializer.Serialize(state), "Snapshot omitted serialized fields");
        state.Items[0].Name = "Changed"; state.Items.Add(new WallpaperItem { Path = "b.jpg" });
        state.Playlists[1].ItemIds.Clear(); state.Settings.Monitors[0].RemainingSeconds = 2;
        state.Settings.Schedules[0].Days.Add(DayOfWeek.Monday); state.Settings.Language = "vi";
        Check(JsonSerializer.Serialize(snapshot) == snapshotJson, "Snapshot aliases mutable live state");
        var store = new StateStore(directory); store.Save(snapshot); var revision = store.Revision;
        var stale = snapshot.CreateSnapshot(); stale.Items[0].Name = "Stale checkpoint";
        var current = snapshot.CreateSnapshot(); current.Items[0].Name = "Explicit newest commit"; store.Save(current);
        Check(!store.TrySaveSnapshot(stale, revision), "Stale checkpoint replaced explicit mutation");
        Check(store.Load().Items[0].Name == "Explicit newest commit", "Latest commit lost");
        using (var locked = new FileStream(store.StatePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var before = store.Revision;
            try { store.TrySaveSnapshot(snapshot, before); throw new Exception("Expected locked write failure"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Check(store.Revision == before, "Failed write advanced revision");
        }
        Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Failed checkpoint left temporary files");
        var next = store.Revision; Check(store.TrySaveSnapshot(snapshot, next), "Checkpoint retry failed");
        Check(store.Revision == next + 1 && store.Load().Items[0].Name == snapshot.Items[0].Name, "Checkpoint not committed");
        Parallel.For(0, 24, index =>
        {
            var own = snapshot.CreateSnapshot(); own.Items[0].Name = "Concurrent " + index; store.Save(own);
        });
        Check(store.Load().Items[0].Name.StartsWith("Concurrent "), "Serialized concurrent saves corrupted library");
        Check(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Concurrent saves left temporary files");
    }
}
