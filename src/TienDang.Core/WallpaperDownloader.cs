using System.Net;
using System.Net.Http;

namespace TienDang.Core;

public sealed record UrlMediaInfo(Uri Url, string FileName, string ContentType, long? Length)
{
    public bool IsVideo => MediaTypes.IsVideo(FileName);
}
public sealed record DownloadProgress(long Received, long? Total, double BytesPerSecond);
public sealed record DownloadedMedia(string Path, Uri Source, string FileName);

/// <summary>Download into an owned temporary file, validate, then publish without overwriting.</summary>
public sealed class WallpaperDownloader : IDisposable
{
    private readonly HttpClient _client;
    public WallpaperDownloader(HttpMessageHandler? handler = null)
    {
        _client = handler == null ? new HttpClient() : new HttpClient(handler);
        _client.Timeout = Timeout.InfiniteTimeSpan;
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("TienDangWallpaper/0.4.0");
    }
    public static Uri ParseUrl(string text)
    {
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Dán link HTTP/HTTPS trực tiếp tới ảnh hoặc video, không kèm thông tin đăng nhập.");
        var builder = new UriBuilder(uri) { Fragment = "" };
        return builder.Uri;
    }
    public async Task<UrlMediaInfo> InspectAsync(string text, CancellationToken cancellationToken)
    {
        var uri = ParseUrl(text);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Head, uri);
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
            return Describe(uri, null, null, null);
        EnsureSuccess(response);
        return Describe(uri, response.Content.Headers.ContentType?.MediaType,
            response.Content.Headers.ContentLength,
            response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
    }
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Nguồn trả lỗi HTTP {(int)response.StatusCode}. Kiểm tra lại link hoặc quyền truy cập.");
    }
    private static UrlMediaInfo Describe(Uri uri, string? type, long? length, string? suppliedName)
    {
        var raw = string.IsNullOrWhiteSpace(suppliedName) ? Uri.UnescapeDataString(uri.AbsolutePath.Split('/').Last()) : suppliedName.Trim('"');
        var invalid = "<>:\"/\\|?*";
        var name = new string(raw.Select(c => char.IsControl(c) || invalid.Contains(c) ? '-' : c).ToArray()).Trim(' ', '.');
        if (name.Length > 160) name = name[..160];
        if (string.IsNullOrWhiteSpace(name)) name = "wallpaper";
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            name = "wallpaper-" + name;
        var mime = (type ?? "").Split(';')[0].ToLowerInvariant().Trim();
        var extension = mime switch
        {
            "image/jpeg" => ".jpg", "image/png" => ".png", "image/bmp" => ".bmp", "image/tiff" => ".tiff",
            "video/mp4" => ".mp4", "video/webm" => ".webm", "video/quicktime" => ".mov",
            "video/x-msvideo" => ".avi", "video/x-ms-wmv" => ".wmv", _ => null
        };
        if (mime is "text/html" or "application/json" or "application/pdf")
            throw new InvalidOperationException("Link này là trang web hoặc tài liệu. Hãy lấy link trực tiếp tới file ảnh/video.");
        if (extension != null && !MediaTypes.Supported(name)) name += extension;
        if (!MediaTypes.Supported(name) || (!string.IsNullOrEmpty(mime) && mime != "application/octet-stream" && !mime.StartsWith("image/") && !mime.StartsWith("video/")))
            throw new InvalidOperationException("Không nhận ra định dạng wallpaper. Dùng ảnh JPG/PNG/BMP/TIFF hoặc video MP4/WebM/MOV/AVI/WMV.");
        if (mime.StartsWith("image/") && MediaTypes.IsVideo(name) || mime.StartsWith("video/") && !MediaTypes.IsVideo(name))
            throw new InvalidOperationException("Định dạng nguồn và tên file không khớp. Kiểm tra lại link media.");
        return new(uri, name, mime, length is > 0 ? length : null);
    }
    public async Task<DownloadedMedia> DownloadAsync(UrlMediaInfo info, string directory,
        Func<string, CancellationToken, Task> validate, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, ".tiendang-" + Guid.NewGuid().ToString("N") + ".part" + Path.GetExtension(info.FileName));
        try
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            idle.CancelAfter(TimeSpan.FromSeconds(40));
            using var request = new HttpRequestMessage(HttpMethod.Get, info.Url);
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, idle.Token);
            EnsureSuccess(response);
            var actual = Describe(info.Url, response.Content.Headers.ContentType?.MediaType,
                response.Content.Headers.ContentLength, response.Content.Headers.ContentDisposition?.FileNameStar ??
                response.Content.Headers.ContentDisposition?.FileName ?? info.FileName);
            await using (var source = await response.Content.ReadAsStreamAsync(idle.Token))
            await using (var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true))
            {
                var buffer = new byte[131072]; long received = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    idle.CancelAfter(TimeSpan.FromSeconds(40));
                    var count = await source.ReadAsync(buffer, idle.Token);
                    if (count == 0) break;
                    await destination.WriteAsync(buffer.AsMemory(0, count), idle.Token);
                    received += count;
                    progress?.Report(new(received, actual.Length, received / Math.Max(.1, watch.Elapsed.TotalSeconds)));
                }
                if (received == 0 || actual.Length is { } expected && received != expected)
                    throw new IOException("File tải chưa đầy đủ. Thử lại; chưa thêm vào thư viện.");
                await destination.FlushAsync(cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            await validate(temp, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = Path.Combine(directory, actual.FileName);
            for (var suffix = 2; ; suffix++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { File.Move(temp, candidate, false); break; }
                catch (IOException) when (File.Exists(candidate))
                {
                    candidate = Path.Combine(directory, Path.GetFileNameWithoutExtension(actual.FileName) + $" ({suffix})" + Path.GetExtension(actual.FileName));
                }
            }
            return new(candidate, info.Url, actual.FileName);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Dispose() => _client.Dispose();
}
