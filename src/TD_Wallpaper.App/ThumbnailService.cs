using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace TD_Wallpaper.App;

public sealed class WallpaperCard(WallpaperItem item) : INotifyPropertyChanged
{
    public WallpaperItem Item { get; } = item;
    public string Name => Item.Name;
    public string Path => Item.Path;
    public string Kind => Item.IsVideo ? "VIDEO" : L.Text("ẢNH");
    public string Detail => !FileAvailable ? L.Text("Không tìm thấy file") : Item.DurationSeconds is { } seconds ? L.Text($"Đổi sau {seconds} giây") : System.IO.Path.GetExtension(Item.Path).TrimStart('.').ToUpperInvariant();
    public string FavoriteLabel => Item.Favorite ? L.Text("★ Yêu thích") : "";
    public string FavoriteGlyph => Item.Favorite ? "★" : "☆";
    internal void RefreshFavorite() { PropertyChanged?.Invoke(this, new(nameof(FavoriteGlyph))); PropertyChanged?.Invoke(this, new(nameof(FavoriteLabel))); }
    internal bool ThumbnailAttempted { get; private set; }
    public bool FileAvailable { get; private set; } = true;
    internal void SetFileAvailable(bool value) { FileAvailable = value; PropertyChanged?.Invoke(this, new(nameof(Detail))); }
    public BitmapSource? Thumbnail { get; private set; }
    public string ThumbnailStatus { get; private set; } = L.Text("Đang lấy ảnh xem trước…");
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void RefreshLanguage()
    {
        ThumbnailStatus = Thumbnail != null ? "" : L.Text(ThumbnailStatus == "Chưa có ảnh xem trước" || ThumbnailStatus == "No thumbnail available" ? "Chưa có ảnh xem trước" : "Đang lấy ảnh xem trước…");
        foreach (var property in new[] { nameof(Kind), nameof(Detail), nameof(FavoriteLabel), nameof(ThumbnailStatus) })
            PropertyChanged?.Invoke(this, new(property));
    }
    internal void ReleaseThumbnail() { ThumbnailAttempted = false; Thumbnail = null; ThumbnailStatus = L.Text("Đang lấy ảnh xem trước…"); PropertyChanged?.Invoke(this, new(nameof(Thumbnail))); PropertyChanged?.Invoke(this, new(nameof(ThumbnailStatus))); }
    internal void SetThumbnail(BitmapSource? image)
    {
        ThumbnailAttempted = true; Thumbnail = image;
        ThumbnailStatus = image == null ? L.Text("Chưa có ảnh xem trước") : "";
        PropertyChanged?.Invoke(this, new(nameof(Thumbnail)));
        PropertyChanged?.Invoke(this, new(nameof(ThumbnailStatus)));
    }
}

internal sealed class ThumbnailService : IDisposable
{
    internal const long DefaultBudget = 64 * 1024 * 1024;
    private const long DiskBudget = 128 * 1024 * 1024;
    private readonly string _directory;
    private readonly long _budget;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _workers = new(2);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, Job> _jobs = [];
    private readonly HashSet<Job> _allJobs = [];
    private readonly Dictionary<string, LinkedListNode<Entry>> _cache = [];
    private readonly LinkedList<Entry> _lru = [];
    private long _bytes, _peakBytes;
    private int _active, _peakActive, _started;
    private bool _disposed;
    private sealed record Entry(string Key, BitmapSource Image, long Bytes);
    private sealed class Job(CancellationTokenSource cancellation)
    {
        internal CancellationTokenSource Cancellation = cancellation;
        internal Task<BitmapSource?> Task = null!;
        internal int Consumers;
        internal bool Finished;
    }
    internal ThumbnailService(string directory, long budget = DefaultBudget)
    { _directory = System.IO.Path.Combine(directory, "thumbnails", "v2"); _budget = Math.Max(1, budget); }
    internal long CacheBytes { get { lock (_gate) return _bytes; } }
    internal long PeakCacheBytes { get { lock (_gate) return _peakBytes; } }
    internal int CacheEntries { get { lock (_gate) return _cache.Count; } }
    internal int PendingJobs { get { lock (_gate) return _jobs.Count; } }
    internal int PeakActiveJobs { get { lock (_gate) return _peakActive; } }
    internal int StartedJobs { get { lock (_gate) return _started; } }
    public Task<BitmapSource?> GetAsync(WallpaperItem item, CancellationToken token = default)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled<BitmapSource?>(token);
        var path = item.Path; var video = item.IsVideo; var request = path.ToUpperInvariant() + "|" + video;
        Job job;
        lock (_gate)
        {
            if (_disposed) return Task.FromResult<BitmapSource?>(null);
            if (!_jobs.TryGetValue(request, out job!))
            {
                job = new(CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token)); _jobs.Add(request, job); _allJobs.Add(job); _started++;
                var owned = job; job.Task = Task.Run(() => ProduceAsync(request, path, video, owned));
            }
            job.Consumers++;
        }
        return AwaitJobAsync(request, job, token);
    }
    private async Task<BitmapSource?> AwaitJobAsync(string request, Job job, CancellationToken token)
    {
        try { return await job.Task.WaitAsync(token); }
        finally
        {
            lock (_gate)
            {
                if (--job.Consumers == 0)
                {
                    if (_jobs.TryGetValue(request, out var current) && current == job) _jobs.Remove(request);
                    if (job.Finished) job.Cancellation.Dispose(); else job.Cancellation.Cancel();
                }
            }
        }
    }
    private async Task<BitmapSource?> ProduceAsync(string request, string path, bool video, Job job)
    {
        var token = job.Cancellation.Token; var acquired = false;
        try
        {
            await _workers.WaitAsync(token); acquired = true;
            lock (_gate) { _active++; _peakActive = Math.Max(_peakActive, _active); }
            token.ThrowIfCancellationRequested(); var info = new FileInfo(path); if (!info.Exists) return null;
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("v2|480|" + info.FullName.ToUpperInvariant() + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks)));
            lock (_gate)
            {
                if (_cache.TryGetValue(key, out var hit)) { _lru.Remove(hit); _lru.AddFirst(hit); return hit.Value.Image; }
            }
            Directory.CreateDirectory(_directory);
            var target = System.IO.Path.Combine(_directory, key + (video ? ".jpg" : ".png")); BitmapSource? image = null;
            if (File.Exists(target))
            {
                try { image = LoadImage(target, 480); }
                catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) { File.Delete(target); }
            }
            if (image == null)
            {
                if (video) image = await DecodeVideoAsync(path, target, token);
                else
                {
                    image = LoadImage(path, 480); token.ThrowIfCancellationRequested();
                    var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                        using (var output = File.Create(temporary)) encoder.Save(output);
                        File.Move(temporary, target, true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                TrimDiskCache(target);
            }
            token.ThrowIfCancellationRequested();
            if (image != null)
            {
                var bytes = ((long)image.PixelWidth * image.Format.BitsPerPixel + 7) / 8 * image.PixelHeight;
                lock (_gate)
                {
                    if (!_disposed && !token.IsCancellationRequested && bytes <= _budget)
                    {
                        if (_cache.Remove(key, out var prior)) { _lru.Remove(prior); _bytes -= prior.Value.Bytes; }
                        while (_bytes + bytes > _budget && _lru.Last is { } oldest)
                        { _cache.Remove(oldest.Value.Key); _bytes -= oldest.Value.Bytes; _lru.RemoveLast(); }
                        _cache[key] = _lru.AddFirst(new Entry(key, image, bytes)); _bytes += bytes; _peakBytes = Math.Max(_peakBytes, _bytes);
                    }
                }
            }
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException or FormatException) { return null; }
        finally
        {
            lock (_gate)
            {
                if (acquired) _active--;
                if (_jobs.TryGetValue(request, out var current) && current == job) _jobs.Remove(request);
                _allJobs.Remove(job); job.Finished = true; if (job.Consumers == 0) job.Cancellation.Dispose();
            }
            if (acquired) _workers.Release();
        }
    }
    private async Task<BitmapSource?> DecodeVideoAsync(string path, string target, CancellationToken token)
    {
        var work = System.IO.Path.Combine(_directory, "work-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        try
        {
            var start = new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory, "Player", "mpv.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var option in new[] { "--no-config", "--load-scripts=no", "--input-default-bindings=no", "--no-audio", "--audio-display=no", "--hwdec=no", "--vo=image", "--vo-image-format=jpg", "--vo-image-outdir=" + work, "--vf=lavfi=[scale=w=480:h=480:force_original_aspect_ratio=decrease]", "--frames=1", "--start=0", "--idle=no", "--keep-open=no", "--terminal=no", "--", System.IO.Path.GetFullPath(path) }) start.ArgumentList.Add(option);
            using var process = Process.Start(start) ?? throw new IOException(L.Text("Không mở được bộ lấy thumbnail."));
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(); await process.WaitForExitAsync(); await Task.WhenAll(output, error); }
            var frame = Directory.EnumerateFiles(work, "*.jpg").FirstOrDefault(); if (process.ExitCode != 0 || frame == null) return null;
            File.Move(frame, target, true); return LoadImage(target, 480);
        }
        finally { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    private readonly object _diskGate = new(); private long _nextDiskSweep;
    private void TrimDiskCache(string current)
    {
        lock (_diskGate)
        {
            if (Environment.TickCount64 < _nextDiskSweep) return; _nextDiskSweep = Environment.TickCount64 + 30_000;
            try
            {
                var files = new DirectoryInfo(_directory).EnumerateFiles().Where(f => f.Extension is ".png" or ".jpg").OrderBy(f => f.LastWriteTimeUtc).ToArray(); var size = files.Sum(f => f.Length);
                foreach (var file in files) { if (size <= DiskBudget) break; if (file.FullName == current) continue; size -= file.Length; file.Delete(); }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    internal static BitmapSource LoadImage(string path, int width)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None); var frame = decoder.Frames[0];
        var landscape = frame.PixelWidth >= frame.PixelHeight; input.Position = 0;
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if (landscape) image.DecodePixelWidth = Math.Min(width, frame.PixelWidth); else image.DecodePixelHeight = Math.Min(width, frame.PixelHeight);
        image.StreamSource = input; image.EndInit(); image.Freeze(); return image;
    }
    internal void ClearMemoryCache()
    { lock (_gate) { _cache.Clear(); _lru.Clear(); _bytes = 0; } }
    public void Dispose()
    {
        Task[] jobs;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _lifetime.Cancel(); _cache.Clear(); _lru.Clear(); _bytes = 0;
            jobs = _allJobs.Select(j => j.Task).ToArray();
        }
        try { Task.WaitAll(jobs, TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
    }
}