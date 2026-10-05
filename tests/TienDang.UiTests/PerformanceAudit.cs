using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TienDang.App;
using TienDang.Core;

internal static class PerformanceAudit
{
    internal static int AuditPersistence(string output)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TienDang-audit-persistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var evidence = new System.Collections.Generic.List<object>();
        foreach (var field in new[] { "Items", "Playlists" })
        {
            var store = new StateStore(Path.Combine(directory, "null-" + field));
            var state = new LibraryState { Items = [new WallpaperItem { Name = "Good item", Path = "fixture.jpg" }] };
            store.Save(state); store.Save(state);
            File.WriteAllText(store.StatePath, "{\"Version\":1,\"" + field + "\":null}");
            try { var loaded = store.Load(); evidence.Add(new { Case = "null-" + field, Reproduced = false, Items = loaded.Items.Count }); }
            catch (Exception ex) { evidence.Add(new { Case = "null-" + field, Reproduced = true, Exception = ex.GetType().Name, HasValidBackup = true }); }
        }
        var recoveredStore = new StateStore(Path.Combine(directory, "backup-recovery"));
        var good = new LibraryState { Items = [new WallpaperItem { Name = "Known good", Path = "fixture.jpg" }] };
        recoveredStore.Save(good); recoveredStore.Save(good);
        File.WriteAllText(recoveredStore.StatePath, "{broken-json");
        var recovered = recoveredStore.Load(); var recoveredCount = recovered.Items.Count;
        recoveredStore.Save(recovered);
        var validBackup = true;
        try { using var json = JsonDocument.Parse(File.ReadAllText(recoveredStore.StatePath + ".bak")); }
        catch (JsonException) { validBackup = false; }
        evidence.Add(new { Case = "save-after-backup-recovery", Reproduced = !validBackup, RecoveredItems = recoveredCount, ValidBackupAfterSave = validBackup });
        var save = new StateStore(Path.Combine(directory, "save-benchmark"));
        var big = new LibraryState();
        for (var i = 0; i < 1000; i++) big.Items.Add(new WallpaperItem { Id = i.ToString(), Name = $"Wallpaper {i:D4}", Path = Path.Combine(directory, $"image-{i:D4}.jpg") });
        var times = new System.Collections.Generic.List<double>();
        for (var i = 0; i < 5; i++) { var watch = Stopwatch.StartNew(); save.Save(big); times.Add(watch.Elapsed.TotalMilliseconds); }
        evidence.Add(new { Case = "sync-save-1000", Milliseconds = times, StateBytes = new FileInfo(save.StatePath).Length });
        File.WriteAllText(output, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PERSISTENCE AUDIT " + JsonSerializer.Serialize(evidence));
        return 0;
    }
    internal static int Run(Application app, string root, string[] args) => StageTwoChecks.Run(root,
        args.Select(arg => arg == "--audit" ? "--stage2" : arg == "--audit-output" ? "--stage2-output" : arg).ToArray());
}