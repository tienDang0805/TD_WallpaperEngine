using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace TienDang.Core;
public sealed record ToolsetPointer(string Directory, string YtDlpSha256, string DenoSha256);
public static class ToolsetManager
{
    private static string Root(string data) => Path.Combine(data, "toolsets");
    public static DownloadToolPaths Resolve(string data, string? bundled = null)
    {
        bundled ??= Path.Combine(AppContext.BaseDirectory, "DownloadTools");
        var pointer = Path.Combine(Root(data), "active.json");
        try
        {
            if (File.Exists(pointer))
            {
                if (new FileInfo(pointer).Length > 4096) throw new IOException("Toolset pointer too large.");
                var p = JsonSerializer.Deserialize<ToolsetPointer>(File.ReadAllText(pointer)) ?? throw new IOException("Invalid toolset pointer.");
                var directory = Path.Combine(Root(data), p.Directory);
                if (!Regex.IsMatch(p.Directory, "^[a-f0-9]{32}$")) throw new IOException("Invalid toolset pointer.");
                var yt = Path.Combine(directory, "yt-dlp.exe"); var deno = Path.Combine(directory, "deno.exe");
                if (Hash(yt) == p.YtDlpSha256 && Hash(deno) == p.DenoSha256)
                    return new(yt, Path.Combine(bundled, "ffmpeg.exe"), deno);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException) { }
        return new(Path.Combine(bundled, "yt-dlp.exe"), Path.Combine(bundled, "ffmpeg.exe"), Path.Combine(bundled, "deno.exe"));
    }
    private static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
    public static async Task UpdateAsync(string data, IProgress<string>? progress, CancellationToken token, HttpMessageHandler? handler = null)
    {
        using var client = handler == null ? new HttpClient() : new HttpClient(handler); client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TD-WallpaperEngine/1.0.0");
        var root = Root(data); Directory.CreateDirectory(root); var id = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(root, id); Directory.CreateDirectory(stage); var committed = false;
        try
        {
            foreach (var (repository, assetName, output) in new[] {
                ("yt-dlp/yt-dlp", "yt-dlp.exe", "yt-dlp.exe"),
                ("denoland/deno", "deno-x86_64-pc-windows-msvc.zip", "deno.exe") })
            {
                progress?.Report("Checking " + repository);
                using var response = await client.GetAsync("https://api.github.com/repos/" + repository + "/releases/latest", HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > 4 * 1024 * 1024) throw new IOException("Release metadata too large.");
                await using var metadata = await response.Content.ReadAsStreamAsync(token);
                using var metadataBuffer = new MemoryStream(); var block = new byte[65536];
                while (true) { var count = await metadata.ReadAsync(block, token); if (count == 0) break; if (metadataBuffer.Length + count > 4 * 1024 * 1024) throw new IOException("Release metadata too large."); metadataBuffer.Write(block, 0, count); }
                metadataBuffer.Position = 0; using var doc = await JsonDocument.ParseAsync(metadataBuffer, cancellationToken: token);
                var asset = doc.RootElement.GetProperty("assets").EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == assetName);
                if (asset.ValueKind != JsonValueKind.Object) throw new IOException("Official release asset missing.");
                var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
                if (digest == null || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw new IOException("Official release has no SHA256 digest; bundled tools were kept.");
                var source = new Uri(asset.GetProperty("browser_download_url").GetString()!);
                if (source.Scheme != "https" || source.Host != "github.com" || !source.AbsolutePath.StartsWith("/" + repository + "/releases/download/", StringComparison.Ordinal)) throw new IOException("Unexpected tool download source.");
                var downloaded = Path.Combine(stage, assetName); progress?.Report("Downloading " + assetName);
                using var media = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, token); media.EnsureSuccessStatusCode();
                await using (var input = await media.Content.ReadAsStreamAsync(token))
                await using (var disk = new FileStream(downloaded, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
                {
                    var buffer = new byte[131072]; long total = 0;
                    while (true) { var count = await input.ReadAsync(buffer, token); if (count == 0) break; total += count; if (total > 250 * 1024 * 1024) throw new IOException("Tool asset too large."); await disk.WriteAsync(buffer.AsMemory(0, count), token); }
                }
                if (!string.Equals(Hash(downloaded), digest[7..], StringComparison.OrdinalIgnoreCase)) throw new IOException("SHA256 verification failed; bundled tools were kept.");
                if (assetName.EndsWith(".zip", StringComparison.Ordinal))
                {
                    using var zip = ZipFile.OpenRead(downloaded); var entries = zip.Entries.Where(e => e.FullName == "deno.exe").ToArray();
                    if (entries.Length != 1 || entries[0].Length > 250 * 1024 * 1024) throw new IOException("Invalid Deno archive.");
                    await using var input = entries[0].Open(); await using var disk = new FileStream(Path.Combine(stage, output), FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                    await input.CopyToAsync(disk, token);
                }
                var version = ""; using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await new DownloadToolRunner().RunAsync(Path.Combine(stage, output), ["--version"], stage, text => version += text, timeout.Token);
                if (string.IsNullOrWhiteSpace(version)) throw new IOException("Updated tool did not report a version.");
                progress?.Report(output + " " + version);
            }
            var pointer = new ToolsetPointer(id, Hash(Path.Combine(stage, "yt-dlp.exe")), Hash(Path.Combine(stage, "deno.exe")));
            var active = Path.Combine(root, "active.json"); var pending = active + ".tmp";
            File.WriteAllText(pending, JsonSerializer.Serialize(pointer)); token.ThrowIfCancellationRequested();
            if (File.Exists(active)) File.Copy(active, Path.Combine(root, "previous.json"), true);
            File.Move(pending, active, true); committed = true;
        }
        finally { if (!committed) { var pending = Path.Combine(root, "active.json.tmp"); if (File.Exists(pending)) File.Delete(pending); if (Directory.Exists(stage)) Directory.Delete(stage, true); } }
    }
    public static void Rollback(string data)
    {
        var root = Root(data); var active = Path.Combine(root, "active.json"); var previous = Path.Combine(root, "previous.json");
        if (File.Exists(previous)) { var temp = active + ".tmp"; File.Copy(previous, temp, true); File.Move(temp, active, true); File.Delete(previous); }
        else if (File.Exists(active)) File.Delete(active);
    }
}