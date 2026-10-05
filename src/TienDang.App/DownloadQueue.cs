using System.Text.Json;
namespace TienDang.App;
internal sealed class DownloadJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Directory { get; set; } = "";
    public string PlaylistId { get; set; } = Playlist.AllId;
    public int Height { get; set; }
    public string Name { get; set; } = "";
    public string Phase { get; set; } = "queued";
    public string Error { get; set; } = "";
    public double? Percent { get; set; }
    public DownloadedMedia? Finished { get; set; }
    public WallpaperItem? Imported { get; set; }
    internal CancellationTokenSource? Cancellation;
    public override string ToString() => Name + "\n" + L.Text(Phase switch {
        "queued" => "Đang chờ", "analyzing" => "Đang kiểm tra…", "downloading" => "Đang tải…", "processing" => "Đang ghép/remux và kiểm tra MP4…",
        "importing" => "Đang thêm vào thư viện…", "complete" => "Đã thêm vào thư viện!", "canceled" => "Đã hủy", _ => "Cần thử lại"
    }) + (Percent is { } p ? " · " + p.ToString("0") + "%" : "") + (Error.Length > 0 ? "\n" + Error : "");
}
internal sealed class DownloadQueue : IDisposable
{
    private readonly StateStore _store;
    private readonly Func<DownloadedMedia, string, Task<WallpaperItem>> _register;
    private bool _pumping, _disposed;
    private readonly CancellationTokenSource _lifetime = new();
    internal List<DownloadJob> Jobs { get; } = [];
    internal bool Busy => _pumping || Jobs.Any(j => j.Phase == "queued");
    internal event Action? Changed;
    private string StatePath => Path.Combine(_store.DirectoryPath, "download-queue.json");
    public DownloadQueue(StateStore store, Func<DownloadedMedia, string, Task<WallpaperItem>> register)
    {
        _store = store; _register = register;
        try
        {
            if (File.Exists(StatePath) && new FileInfo(StatePath).Length <= 1024 * 1024)
            {
                foreach (var job in JsonSerializer.Deserialize<List<DownloadJob>>(File.ReadAllText(StatePath)) ?? [])
                {
                    if (job.Phase == "complete") continue;
                    WallpaperDownloader.ParseUrl(job.Url);
                    job.Phase = "failed"; job.Error = L.Text("Phiên tải trước bị gián đoạn. Bấm Thử lại để tiếp tục."); Jobs.Add(job);
                }
            }
        }
        catch (Exception ex) { _store.Log("Queue recovery: " + ex.GetType().Name); }
    }
    internal DownloadJob Enqueue(Uri uri, string directory, string playlist, int height)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DownloadQueue));
        var canonical = YouTubeDownloader.IsYouTube(uri) ? YouTubeDownloader.CanonicalUrl(uri) : uri;
        var existing = Jobs.Find(j => j.Url == canonical.AbsoluteUri && j.Phase is not ("failed" or "canceled"));
        if (existing != null) return existing;
        if (Jobs.Count >= 100) Jobs.RemoveAll(j => j.Phase is "complete" or "canceled");
        if (Jobs.Count >= 100) throw new InvalidOperationException(L.Text("Hàng đợi tối đa 100 mục."));
        var job = new DownloadJob { Url = canonical.AbsoluteUri, Directory = Path.GetFullPath(directory), PlaylistId = playlist, Height = height, Name = canonical.Host };
        Jobs.Add(job); Publish(); _ = Pump(); return job;
    }
    internal void Cancel(DownloadJob job)
    {
        if (job.Phase == "complete") return;
        job.Cancellation?.Cancel(); job.Phase = "canceled"; Publish();
    }
    internal void Retry(DownloadJob job)
    {
        if (job.Cancellation != null || job.Phase is not ("failed" or "canceled")) return;
        job.Error = ""; job.Percent = null; job.Phase = "queued"; Publish(); _ = Pump();
    }
    private void Publish(bool persist = true)
    {
        Changed?.Invoke(); if (!persist) return;
        try
        {
            Directory.CreateDirectory(_store.DirectoryPath); var temp = StatePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Jobs)); File.Move(temp, StatePath, true);
        }
        catch (Exception ex) { _store.Log("Queue checkpoint failed: " + ex.GetType().Name); }
    }
    private async Task Pump()
    {
        if (_pumping || _disposed) return; _pumping = true;
        try
        {
            while (!_disposed && Jobs.FirstOrDefault(j => j.Phase == "queued") is { } job)
            {
                job.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); var token = job.Cancellation.Token;
                try
                {
                    job.Phase = "analyzing"; Publish(); var url = WallpaperDownloader.ParseUrl(job.Url);
                    using var direct = new WallpaperDownloader();
                    var toolPaths = await Task.Run(() => ToolsetManager.Resolve(_store.DirectoryPath), token);
                    var youtube = new YouTubeDownloader(toolPaths, maxHeight: job.Height);
                    var yt = YouTubeDownloader.IsYouTube(url) ? await youtube.InspectAsync(url, token) : null;
                    var info = yt?.Media ?? await direct.InspectAsync(url.AbsoluteUri, token); job.Name = yt?.Title ?? info.FileName;
                    var last = DateTime.MinValue;
                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        if (token.IsCancellationRequested || _disposed || job.Phase is not ("downloading" or "processing") || DateTime.UtcNow - last < TimeSpan.FromMilliseconds(200)) return;
                        last = DateTime.UtcNow; job.Phase = "downloading"; job.Percent = p.Total is > 0 ? Math.Min(99, p.Received * 100d / p.Total.Value) : null; Publish(false);
                    });
                    if (job.Finished == null || !File.Exists(job.Finished.Path))
                    {
                        job.Phase = "downloading"; Publish();
                        if (yt == null) job.Finished = await direct.DownloadAsync(info, job.Directory, UrlImportWindow.ValidateMediaAsync, progress, token);
                        else job.Finished = await youtube.DownloadAsync(yt, job.Directory, UrlImportWindow.ValidateMediaAsync,
                            new Progress<VideoDownloadProgress>(p => { if (token.IsCancellationRequested || _disposed || job.Phase is not ("downloading" or "processing")) return; if (p.Processing) { job.Phase = "processing"; job.Percent = null; Publish(false); } else if (p.Download != null) ((IProgress<DownloadProgress>)progress).Report(p.Download); }), token);
                    }
                    token.ThrowIfCancellationRequested(); job.Phase = "importing"; Publish();
                    job.Imported = await _register(job.Finished, job.PlaylistId);
                    job.Phase = "complete"; job.Percent = 100; job.Error = ""; Publish();
                }
                catch (OperationCanceledException) { job.Phase = "canceled"; Publish(); }
                catch (Exception ex) { job.Phase = "failed"; job.Error = L.Text(ex.Message); Publish(); }
                finally { job.Cancellation.Dispose(); job.Cancellation = null; }
            }
        }
        finally { _pumping = false; Changed?.Invoke(); }
    }
    public void Dispose() { _disposed = true; _lifetime.Cancel(); foreach (var job in Jobs) job.Cancellation?.Cancel(); Publish(); }
}