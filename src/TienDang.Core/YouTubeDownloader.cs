using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TienDang.Core;

public sealed record YouTubeMediaInfo(UrlMediaInfo Media, string Title, Uri? Thumbnail, int? Width, int? Height, double? Duration);
public sealed record VideoDownloadProgress(DownloadProgress? Download, bool Processing);
public sealed record DownloadToolPaths(string YtDlp, string Ffmpeg, string Deno);
public interface IDownloadToolRunner
{
    Task RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        Action<string> output, CancellationToken token);
}

/// <summary>Starts executable directly: user data is never interpreted by a command shell.</summary>
public sealed class DownloadToolRunner : IDownloadToolRunner
{
    public async Task RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        Action<string> output, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true, WorkingDirectory = workingDirectory,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start(); process.StandardInput.Close();
        using var job = AttachJob(process);
        using var cancellation = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var errors = new StringBuilder();
        async Task ReadAsync(StreamReader reader, bool stderr)
        {
            try
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.Length > 2_097_152) throw new IOException("Thông tin nguồn quá lớn, thử video khác.");
                    if (stderr) { errors.AppendLine(line); if (errors.Length > 6000) errors.Remove(0, errors.Length - 6000); }
                    else output(line);
                }
            }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        }
        try
        {
            await Task.WhenAll(ReadAsync(process.StandardOutput, false), ReadAsync(process.StandardError, true),
                process.WaitForExitAsync());
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new InvalidOperationException(FriendlyError(errors.ToString()));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }
    private static DownloadProcessJob? AttachJob(Process process)
    {
        if (!OperatingSystem.IsWindows() || process.HasExited) return null;
        try { return DownloadProcessJob.Attach(process); }
        catch
        {
            if (process.HasExited) return null;
            process.Kill(entireProcessTree: true); process.WaitForExit(); throw;
        }
    }
    public static string FriendlyError(string text)
    {
        if (text.Contains("Sign in", StringComparison.OrdinalIgnoreCase) || text.Contains("bot", StringComparison.OrdinalIgnoreCase))
            return "YouTube yêu cầu xác minh hoặc đăng nhập trên mạng này. Thử lại sau hoặc tải file bằng trình duyệt rồi thêm từ máy.";
        if (text.Contains("private", StringComparison.OrdinalIgnoreCase) || text.Contains("unavailable", StringComparison.OrdinalIgnoreCase) || text.Contains("removed", StringComparison.OrdinalIgnoreCase))
            return "Video không công khai, đã bị xóa hoặc không khả dụng. Thử link YouTube khác.";
        if (text.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase))
            return "Không ghép/remux được video. Giữ nguyên thư mục DownloadTools đi kèm app và thử lại.";
        if (text.Contains("HTTP Error 403", StringComparison.OrdinalIgnoreCase) || text.Contains("challenge", StringComparison.OrdinalIgnoreCase))
            return "YouTube từ chối luồng tải. Thử lại; bộ yt-dlp/Deno có thể cần cập nhật khi YouTube thay đổi.";
        var last = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
        if (last.Length > 450) last = last[..450];
        return "Không tải được video YouTube." + (last.Length > 0 ? " " + last : " Kiểm tra mạng và thử lại.");
    }
}

public sealed class YouTubeDownloader(DownloadToolPaths tools, IDownloadToolRunner? runner = null, int maxHeight = 0)
{
    private readonly IDownloadToolRunner _runner = runner ?? new DownloadToolRunner();
    public static bool IsYouTube(Uri url) => url.Host.ToLowerInvariant() is
        "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "youtu.be" or "www.youtu.be";

    public static Uri CanonicalUrl(Uri url)
    {
        if (!IsYouTube(url) || url.Scheme is not ("http" or "https") || url.UserInfo.Length > 0)
            throw new InvalidOperationException("Dùng link video YouTube hợp lệ.");
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id = null;
        if (url.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase)) id = segments.FirstOrDefault();
        else if (segments.Length == 1 && segments[0] == "watch")
            id = url.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2))
                .Where(p => p[0] == "v" && p.Length == 2).Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault();
        else if (segments.Length == 2 && segments[0] is "shorts" or "embed" or "live") id = segments[1];
        if (id == null || !Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{11}\z"))
            throw new InvalidOperationException("Dán link một video YouTube (watch, youtu.be hoặc Shorts), không phải trang kênh hay playlist.");
        return new Uri("https://www.youtube.com/watch?v=" + id);
    }
    public static string FormatSelector(int height) => height is 1080 or 1440 or 2160
        ? $"bv*[height<={height}][ext=mp4]+ba[ext=m4a]/b[height<={height}][ext=mp4]/bv*[height<={height}]+ba/b[height<={height}]"
        : "bv*[ext=mp4]+ba[ext=m4a]/b[ext=mp4]/bv*+ba/b";
    private List<string> CommonArguments()
    {
        foreach (var path in new[] { tools.YtDlp, tools.Ffmpeg, tools.Deno })
            if (!File.Exists(path)) throw new FileNotFoundException("Thiếu bộ tải YouTube. Giữ nguyên thư mục DownloadTools đi kèm app.");
        return ["--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-playlist", "--no-colors",
            "--encoding", "utf-8", "--socket-timeout", "30", "--retries", "3", "--fragment-retries", "3",
            "--js-runtimes", "deno:" + Path.GetFullPath(tools.Deno), "--ffmpeg-location", Path.GetFullPath(tools.Ffmpeg),
            "--format", FormatSelector(maxHeight),
            "--merge-output-format", "mp4", "--remux-video", "mp4",
            "--match-filter", "!is_live & !is_upcoming"];
    }
    public async Task<YouTubeMediaInfo> InspectAsync(Uri url, CancellationToken token)
    {
        var canonical = CanonicalUrl(url);
        var arguments = CommonArguments(); arguments.AddRange(["--skip-download", "--dump-single-json", "--", canonical.AbsoluteUri]);
        string? json = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        await _runner.RunAsync(tools.YtDlp, arguments, Path.GetDirectoryName(Path.GetFullPath(tools.YtDlp))!,
            line => { if (line.StartsWith('{')) json = line; }, timeout.Token);
        token.ThrowIfCancellationRequested();
        if (json == null) throw new InvalidOperationException("Không đọc được video; video trực tiếp hoặc sắp phát chưa được hỗ trợ.");
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (root.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True ||
            root.TryGetProperty("live_status", out var status) && status.GetString() is "is_live" or "is_upcoming")
            throw new InvalidOperationException("Chỉ tải video đã phát xong, không tải livestream đang chạy.");
        var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "YouTube wallpaper" : "YouTube wallpaper";
        if (title.Length > 150) title = title[..150];
        var name = new string(title.Select(c => char.IsControl(c) || "<>:\"/\\|?*%".Contains(c) ? '-' : c).ToArray()).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(name)) name = "YouTube-wallpaper";
        var fileName = name + " [" + canonical.Query[3..] + "].mp4";
        long? length = Number(root, "filesize") ?? Number(root, "filesize_approx");
        Uri? thumbnail = null;
        if (root.TryGetProperty("thumbnail", out var thumb) && Uri.TryCreate(thumb.GetString(), UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http")
            thumbnail = parsed;
        double? duration = root.TryGetProperty("duration", out var d) && d.TryGetDouble(out var seconds) ? seconds : null;
        return new(new(canonical, fileName, "video/mp4", length), title, thumbnail,
            (int?)Number(root, "width"), (int?)Number(root, "height"), duration);
    }
    private static long? Number(JsonElement element, string key) =>
        element.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && n >= 0 && n <= long.MaxValue ? (long)n : null;

    public async Task<DownloadedMedia> DownloadAsync(YouTubeMediaInfo info, string directory,
        Func<string, CancellationToken, Task> validate, IProgress<VideoDownloadProgress>? progress, CancellationToken token)
    {
        var canonical = CanonicalUrl(info.Media.Url);
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, ".tiendang-youtube-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string? outputPath = null;
        try
        {
            var arguments = CommonArguments();
            arguments.AddRange(["--newline", "--progress", "--progress-delta", "0.2",
                "--progress-template", "download:TD_PROGRESS:%(progress)j",
                "--progress-template", "postprocess:TD_PROCESS:%(progress.status)s",
                "--print", "after_move:TD_FILE:%(filepath)j", "--no-simulate",
                "--output", Path.Combine(staging, "video.%(ext)s"), "--", canonical.AbsoluteUri]);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromHours(3));
            await _runner.RunAsync(tools.YtDlp, arguments, staging, line =>
            {
                if (line.StartsWith("TD_FILE:")) outputPath = JsonSerializer.Deserialize<string>(line[8..]);
                else if (line.StartsWith("TD_PROCESS:")) progress?.Report(new(null, true));
                else if (line.StartsWith("TD_PROGRESS:"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(line[12..]); var value = doc.RootElement;
                        var received = Number(value, "downloaded_bytes") ?? 0;
                        var total = Number(value, "total_bytes") ?? Number(value, "total_bytes_estimate");
                        if (total is <= 0) total = null;
                        progress?.Report(new(new(received, total, Number(value, "speed") ?? 0), false));
                    }
                    catch (JsonException) { /* A non-progress diagnostic must not break a download. */ }
                }
            }, timeout.Token);
            token.ThrowIfCancellationRequested();
            var final = Path.GetFullPath(outputPath ?? "");
            if (!final.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(final), ".mp4", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(final) || new FileInfo(final).Length == 0)
                throw new IOException("Bộ tải chưa tạo được file MP4 đầy đủ. Chưa thêm vào thư viện.");
            progress?.Report(new(null, true));
            await validate(final, token); token.ThrowIfCancellationRequested();
            var safeName = Path.GetFileName(info.Media.FileName);
            if (!string.Equals(safeName, info.Media.FileName, StringComparison.Ordinal) || Path.GetExtension(safeName) != ".mp4")
                throw new IOException("Tên file tải không hợp lệ.");
            // Prefix avoids reserved Windows device names, even when the title starts with CON.
            var stem = Path.GetFileNameWithoutExtension(safeName);
            var candidate = Path.Combine(directory, "YouTube - " + safeName);
            for (var suffix = 2; ; suffix++)
            {
                token.ThrowIfCancellationRequested();
                try { File.Move(final, candidate, false); break; }
                catch (IOException) when (File.Exists(candidate)) { candidate = Path.Combine(directory, $"YouTube - {stem} ({suffix}).mp4"); }
            }
            return new(candidate, canonical, safeName);
        }
        finally
        {
            // This unique directory was created by this operation; never touch the caller's folder.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }
}