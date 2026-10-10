using System.Text.Json;
using TD_Wallpaper.Core;

internal static class PersistenceChecks
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static (StateStore Store, LibraryState State) Fixture(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TD_Wallpaper-persistence-" + name + "-" + Guid.NewGuid().ToString("N"));
        var store = new StateStore(directory);
        var state = new LibraryState { Items = [new() { Id = "good", Name = "Known good", Path = "good.jpg", SourceUrl = "https://fixture.test/good.jpg" }] };
        store.Save(state); store.Save(state);
        return (store, state);
    }
    public static void NullSchemaRecovery()
    {
        foreach (var broken in new[]
        {
            """{"Version":1,"Items":null}""",
            """{"Version":1,"Playlists":null}""",
            """{"Version":1,"Settings":null}""",
            """{"Version":1,"Settings":{"Monitors":null}}""",
            """{"Version":1,"Settings":{"Schedules":null}}""",
            """{"Version":1,"Items":[null]}""",
            """{"Version":1,"Playlists":[null]}""",
            """{"Version":1,"Playlists":[{"Id":"all","ItemIds":null}]}""",
            """{"Version":1,"Settings":{"Monitors":[null]}}""",
            """{"Version":1,"Settings":{"Schedules":[null]}}""",
            """{"Version":1,"Settings":{"Schedules":[{"Days":null}]}}"""
        })
        {
            var (store, _) = Fixture("null");
            File.WriteAllText(store.StatePath, broken);
            var recovered = store.Load();
            Check(recovered.Items.Single().Id == "good", "Null schema must use valid backup: " + broken);
            Check(recovered.Items[0].SourceUrl == "https://fixture.test/good.jpg", "Recovery preserves additive SourceUrl");
            Check(store.RecoveryNotice != null && Directory.GetFiles(store.DirectoryPath, "library-damaged-*").Length == 1, "Honest notice and damaged file preservation");
        }
    }
    public static void BackupAfterRecovery()
    {
        var (store, state) = Fixture("backup");
        File.WriteAllText(store.StatePath, "{broken");
        var recovered = store.Load();
        recovered.Settings.IntervalSeconds = 42;
        store.Save(recovered);
        using (var backup = JsonDocument.Parse(File.ReadAllText(store.StatePath + ".bak"))) Check(backup.RootElement.GetProperty("Items")[0].GetProperty("Id").GetString() == "good", "Recovery save must preserve good backup");
        File.WriteAllText(store.StatePath, "{broken-again");
        Check(new StateStore(store.DirectoryPath).Load().Items.Single().Id == "good", "Second primary corruption remains recoverable after restart");
        File.Delete(store.StatePath);
        var missingPrimary = new StateStore(store.DirectoryPath);
        Check(missingPrimary.Load().Items.Single().Id == "good", "Missing primary must recover valid backup");
        Check(missingPrimary.RecoveryNotice != null, "Missing primary recovery is reported");
        missingPrimary.Save(state);
        Check(missingPrimary.Load().Items.Single().Id == "good", "Recovery from missing primary can save");
        // Also protect a good backup if the primary was changed externally after Load.
        store.Save(state);
        File.WriteAllText(store.StatePath, "{external-corruption");
        store.Save(state);
        using var stillGood = JsonDocument.Parse(File.ReadAllText(store.StatePath + ".bak"));
        Check(stillGood.RootElement.GetProperty("Items").GetArrayLength() == 1, "External primary corruption cannot poison good backup");
    }
    public static void FailureCleanup()
    {
        var (store, state) = Fixture("failure");
        var primary = File.ReadAllText(store.StatePath); var backup = File.ReadAllText(store.StatePath + ".bak");
        state.Settings.Monitors.Add(new() { MonitorId = "invalid", RemainingSeconds = double.NaN });
        var rejected = false;
        try { store.Save(state); } catch (Exception ex) when (ex is ArgumentException or JsonException) { rejected = true; }
        Check(rejected, "Non-serializable state rejected");
        Check(Directory.GetFiles(store.DirectoryPath, "*.tmp").Length == 0, "Serialize failure must clean its temporary file");
        Check(File.ReadAllText(store.StatePath) == primary && File.ReadAllText(store.StatePath + ".bak") == backup, "Failed write preserves previous files");
        state.Settings.Monitors.Clear();
        state.Settings.IntervalSeconds = 77;
        if (OperatingSystem.IsWindows())
        {
            using (var locked = new FileStream(store.StatePath + ".bak", FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                rejected = false; try { store.Save(state); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rejected = true; }
                Check(rejected, "Locked backup rejects save");
                Check(File.ReadAllText(store.StatePath) == primary, "Locked backup cannot publish partial mutation");
            }
            Check(Directory.GetFiles(store.DirectoryPath, "*.tmp").Length == 0, "Backup failure cleans owned temporary files");
            using (var locked = new FileStream(store.StatePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                rejected = false; try { store.Save(state); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { rejected = true; }
                Check(rejected, "Locked primary rejects publish");
            }
            Check(new StateStore(store.DirectoryPath).Load().Settings.IntervalSeconds == 600, "Failed publish preserves primary");
            using var goodBackup = JsonDocument.Parse(File.ReadAllText(store.StatePath + ".bak"));
            Check(goodBackup.RootElement.GetProperty("Settings").GetProperty("IntervalSeconds").GetInt32() == 600, "Failed publish retains valid backup");
            Check(Directory.GetFiles(store.DirectoryPath, "*.tmp").Length == 0, "Publish failure cleans owned temporary files");
        }
        File.WriteAllText(store.StatePath + ".owned-partial.tmp", "{partial");
        Check(new StateStore(store.DirectoryPath).Load().Items.Single().Id == "good", "Interrupted temporary file is never treated as committed data");
        File.Delete(store.StatePath + ".owned-partial.tmp");
        store.Save(state); Check(store.Load().Settings.IntervalSeconds == 77, "Save recovers after file locks released");
    }
}