using System.IO.Compression;
using System.Text.Json;
namespace TienDang.Core;

public sealed record HealthEntry(string Id, string Name, string Path, bool Missing, long Bytes);
public sealed record RelinkProposal(string Id, string OldPath, string NewPath);
public static class LibraryUtilities
{
    public static IReadOnlyList<HealthEntry> Scan(LibraryState state) => state.Items.Select(item =>
    {
        try { var file = new FileInfo(item.Path); return new HealthEntry(item.Id, item.Name, item.Path, !file.Exists, file.Exists ? file.Length : 0); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return new(item.Id, item.Name, item.Path, true, 0); }
    }).ToArray();
    public static IReadOnlyList<RelinkProposal> FindRelinks(LibraryState state, string folder)
    {
        var files = Directory.EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(MediaTypes.Supported).GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key!, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        var occupied = state.Items.Where(i => i.Exists).Select(i => Path.GetFullPath(i.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<RelinkProposal>();
        foreach (var item in state.Items.Where(i => !i.Exists))
        {
            if (!files.TryGetValue(Path.GetFileName(item.Path), out var matches) || matches.Length != 1) continue;
            var path = Path.GetFullPath(matches[0]);
            if (MediaTypes.IsVideo(path) != item.IsVideo || !occupied.Add(path)) continue;
            result.Add(new(item.Id, item.Path, path));
        }
        return result;
    }
    public static bool IsOwnedFile(string root, string path)
    {
        var absolute = Path.GetFullPath(path); var directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!absolute.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        for (var cursor = absolute; cursor.Length >= directory.Length; cursor = Path.GetDirectoryName(cursor) ?? "")
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return false;
        return true;
    }
    public static string[] UnreferencedMedia(LibraryState state, string dataDirectory)
    {
        var root = Path.Combine(dataDirectory, "media"); if (!Directory.Exists(root)) return [];
        var used = state.Items.Select(i => Path.GetFullPath(i.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(p => IsOwnedFile(root, p) && !used.Contains(Path.GetFullPath(p))).ToArray();
    }
    public static string Quarantine(string dataDirectory, IReadOnlyList<string> paths)
    {
        var media = Path.Combine(dataDirectory, "media");
        if (paths.Any(p => !IsOwnedFile(media, p))) throw new IOException("Cleanup can only move owned media files.");
        var directory = Path.Combine(dataDirectory, "quarantine", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var moved = new Dictionary<string, string>();
        try
        {
            foreach (var path in paths)
            {
                var destination = Path.Combine(directory, Guid.NewGuid().ToString("N") + Path.GetExtension(path));
                File.Move(path, destination); moved.Add(destination, path);
                File.WriteAllText(Path.Combine(directory, "restore.json"), JsonSerializer.Serialize(moved));
            }
        }
        catch { foreach (var pair in moved) if (!File.Exists(pair.Value)) File.Move(pair.Key, pair.Value); throw; }
        return directory;
    }
    public static int RestoreQuarantine(string dataDirectory, string directory)
    {
        if (!IsOwnedFile(Path.Combine(dataDirectory, "quarantine"), Path.Combine(directory, "restore.json"))) throw new IOException("Invalid quarantine.");
        var records = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(directory, "restore.json")))!; var count = 0;
        foreach (var pair in records)
        {
            if (!IsOwnedFile(directory, pair.Key) || !IsOwnedFile(Path.Combine(dataDirectory, "media"), pair.Value)) throw new IOException("Invalid restore path.");
            if (!File.Exists(pair.Key) || File.Exists(pair.Value)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(pair.Value)!); File.Move(pair.Key, pair.Value); count++;
        }
        return count;
    }
}

public static class LibraryArchive
{
    public static async Task ExportAsync(LibraryState snapshot, string target, bool includeMedia, CancellationToken token)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            await using (var disk = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            using (var zip = new ZipArchive(disk, ZipArchiveMode.Create, true))
            {
                if (includeMedia) foreach (var item in snapshot.Items)
                {
                    token.ThrowIfCancellationRequested();
                    if (!item.Exists) continue;
                    var name = "media/" + Guid.NewGuid().ToString("N") + Path.GetExtension(item.Path);
                    var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
                    await using var output = entry.Open();
                    await using var input = new FileStream(item.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
                    await input.CopyToAsync(output, token);
                    item.Path = name; item.OriginalPath = "";
                }
                await using var json = zip.CreateEntry("library.json", CompressionLevel.Optimal).Open();
                await JsonSerializer.SerializeAsync(json, snapshot, cancellationToken: token);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static async Task<LibraryState> ReadAsync(string archive, string dataDirectory, CancellationToken token)
    {
        var destination = Path.Combine(dataDirectory, "media", "restores", Guid.NewGuid().ToString("N"));
        if (!LibraryUtilities.IsOwnedFile(dataDirectory, destination)) throw new IOException("Unsafe restore directory.");
        Directory.CreateDirectory(destination);
        try
        {
            using var zip = ZipFile.OpenRead(archive);
            if (zip.Entries.Count > 30000 || zip.Entries.Sum(e => e.Length) > 20L * 1024 * 1024 * 1024) throw new IOException("Backup exceeds the 20 GiB restore limit.");
            if (zip.Entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new IOException("Duplicate backup entry.");
            foreach (var entry in zip.Entries)
                if (entry.FullName.Contains('\\') || entry.FullName.Split('/').Any(s => s is ".." or ".") || Path.IsPathRooted(entry.FullName)) throw new IOException("Unsafe backup path.");
            var library = zip.GetEntry("library.json") ?? throw new IOException("Backup has no library.json.");
            if (library.Length > 16 * 1024 * 1024) throw new IOException("Backup metadata too large.");
            await using var json = library.Open();
            var state = await JsonSerializer.DeserializeAsync<LibraryState>(json, cancellationToken: token) ?? throw new IOException("Invalid backup.");
            if (state.Version != 1) throw new IOException("Unsupported backup schema."); state.Normalize(); state.Settings.WasRunning = false;
            foreach (var item in state.Items.Where(i => i.Path.StartsWith("media/", StringComparison.Ordinal)))
            {
                var entry = zip.GetEntry(item.Path) ?? throw new IOException("Backup media missing.");
                if (!MediaTypes.Supported(entry.Name)) throw new IOException("Unsupported backup media.");
                var path = Path.Combine(destination, Guid.NewGuid().ToString("N") + Path.GetExtension(entry.Name));
                await using var input = entry.Open(); await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                await input.CopyToAsync(output, token); item.Path = path;
            }
            return state;
        }
        catch { Directory.Delete(destination, true); throw; }
    }
}