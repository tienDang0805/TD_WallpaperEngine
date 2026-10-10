using System.IO.Compression;
using TD_Wallpaper.Core;
internal static class PhaseFourChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "TD-phase4-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        void Require(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS Phase4 " + name); }
        try
        {
            var media = Path.Combine(root, "media"); Directory.CreateDirectory(media);
            var existing = Path.Combine(media, "existing.png"); File.WriteAllText(existing, "owned fixture");
            var state = new LibraryState { Items = [new() { Id = "a", Name = "User title", Path = existing, Favorite = true, DurationSeconds = 45 }, new() { Id = "b", Path = Path.Combine(root, "missing.png") }] };
            state.Playlists.Add(new() { Id = "collection", ItemIds = ["a", "b"] });
            var health = LibraryUtilities.Scan(state); Require(health.Count == 2 && health.Count(r => r.Missing) == 1, "missing scan preserves item records");
            var moved = Path.Combine(root, "moved"); Directory.CreateDirectory(moved); File.WriteAllText(Path.Combine(moved, "missing.png"), "replacement");
            var links = LibraryUtilities.FindRelinks(state, moved); Require(links.Count == 1 && links[0].Id == "b", "folder relink uniquely matches filenames");
            Directory.CreateDirectory(Path.Combine(moved, "nested")); File.WriteAllText(Path.Combine(moved, "nested", "missing.png"), "ambiguous");
            Require(LibraryUtilities.FindRelinks(state, moved).Count == 0, "ambiguous filename skipped");
            var unused = Path.Combine(media, "unused.mp4"); File.WriteAllText(unused, "owned orphan");
            Require(LibraryUtilities.UnreferencedMedia(state, root).SequenceEqual([unused]), "cleanup excludes referenced and external files");
            var quarantine = LibraryUtilities.Quarantine(root, [unused]); Require(!File.Exists(unused) && File.Exists(existing), "cleanup moves only reviewed unused file");
            Require(LibraryUtilities.RestoreQuarantine(root, quarantine) == 1 && File.Exists(unused), "cleanup is reversible");
            Require(!LibraryUtilities.IsOwnedFile(media, Path.Combine(media, "..", "outside.mp4")), "path traversal is outside ownership boundary");
            var snapshot = state.CreateSnapshot(); PerformanceProfiles.Apply(snapshot.Settings, "Saver");
            Require(snapshot.Settings.ReleaseWhenBusy && snapshot.Settings.PauseMaximized && snapshot.Settings.FrameRateLimit == 0, "saver releases memory without copy-filter FPS penalty");
            PerformanceProfiles.Apply(snapshot.Settings, "Balanced"); Require(!snapshot.Settings.ReleaseWhenBusy && snapshot.Settings.PauseFullscreen, "balanced pauses rather than destroys player");
            snapshot.Settings.AppRules.Add(new() { ProcessName = "game.exe", Action = "Release" });
            var detached = snapshot.CreateSnapshot(); detached.Settings.AppRules[0].ProcessName = "other.exe";
            Require(snapshot.Settings.AppRules[0].ProcessName == "game.exe", "new settings are deep copied");
            Require(YouTubeDownloader.FormatSelector(1080).Contains("height<=1080") && YouTubeDownloader.FormatSelector(1440).Contains("height<=1440") && YouTubeDownloader.FormatSelector(2160).Contains("height<=2160"), "YouTube height applies to every fallback");
            var backup = Path.Combine(root, "portable.tdbackup"); LibraryArchive.ExportAsync(state.CreateSnapshot(), backup, true, default).GetAwaiter().GetResult();
            var restored = LibraryArchive.ReadAsync(backup, root, default).GetAwaiter().GetResult();
            Require(restored.Items[0].Exists && File.ReadAllText(restored.Items[0].Path) == "owned fixture", "streamed backup restores independent media");
            Require(restored.Items[0].Favorite && restored.Items[0].DurationSeconds == 45 && restored.Playlists[1].ItemIds.Contains("a"), "restore preserves IDs, collections and preferences");
            Require(state.Items[0].Path == existing, "export does not mutate live paths");
            var bad = Path.Combine(root, "bad.zip"); using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create)) { zip.CreateEntry("../escape.exe"); }
            try { LibraryArchive.ReadAsync(bad, root, default).GetAwaiter().GetResult(); throw new Exception("Traversal accepted"); } catch (IOException) { Console.WriteLine("PASS Phase4 backup traversal rejected"); }
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var canceledPath = Path.Combine(root, "canceled.tdbackup");
            try { LibraryArchive.ExportAsync(state.CreateSnapshot(), canceledPath, true, canceled.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation ignored"); } catch (OperationCanceledException) { Require(!File.Exists(canceledPath) && !Directory.EnumerateFiles(root, "*.part").Any(), "canceled backup removes partial output"); }
            Require(ToolsetManager.Resolve(root, media).YtDlp == Path.Combine(media, "yt-dlp.exe"), "missing override falls back to bundled toolset");
            var toolsets = Path.Combine(root, "toolsets"); Directory.CreateDirectory(toolsets);
            File.WriteAllText(Path.Combine(toolsets, "active.json"), "null");
            Require(ToolsetManager.Resolve(root, media).YtDlp == Path.Combine(media, "yt-dlp.exe"), "null toolset pointer safely falls back");
            File.WriteAllText(Path.Combine(toolsets, "active.json"), new string('x', 8192));
            Require(ToolsetManager.Resolve(root, media).YtDlp == Path.Combine(media, "yt-dlp.exe"), "oversized toolset pointer safely falls back");
            Console.WriteLine("PASS Phase4 core suite");
        }
        finally { Directory.Delete(root, true); }
    }
}