using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TienDang.App;
using TienDang.Core;
internal static class ThumbnailChecks
{
    internal static async Task Run(string root)
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS Thumbnail " + message); }
        var directory = Path.Combine(Path.GetTempPath(), "TienDang-thumbnail-cache-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var items = Enumerable.Range(0, 20).Select(i => new WallpaperItem { Path = Path.Combine(directory, i + ".jpg") }).ToArray();
        foreach (var item in items) File.Copy(Program.FixtureImage(root), item.Path);
        const long budget = 1024 * 1024;
        using var service = new ThumbnailService(directory, budget);
        foreach (var item in items) Check(await service.GetAsync(item) != null, "real image decode " + Path.GetFileName(item.Path));
        Check(service.CacheEntries <= 2 && service.CacheBytes > 0 && service.PeakCacheBytes <= budget, "LRU evicts under byte budget");
        Check(service.PeakActiveJobs <= 2 && service.PendingJobs == 0, "workers/jobs remain bounded and completed tasks released");
        var newest = await service.GetAsync(items[^1]); Check(ReferenceEquals(newest, await service.GetAsync(items[^1])), "cache hit reuses frozen bitmap");
        var before = await service.GetAsync(items[0]); File.SetLastWriteTimeUtc(items[0].Path, DateTime.UtcNow.AddMinutes(1));
        var after = await service.GetAsync(items[0]); Check(!ReferenceEquals(before, after), "source timestamp invalidates cached thumbnail");
        using var cancel = new CancellationTokenSource();
        var first = service.GetAsync(items[3], cancel.Token); var second = service.GetAsync(items[3]); cancel.Cancel();
        try { await first; } catch (OperationCanceledException) { }
        Check(await second != null, "cancelled consumer does not abort another consumer sharing the job");
        using var lifetime = new CancellationTokenSource();
        var tasks = items.Select(item => service.GetAsync(item, lifetime.Token)).ToArray(); lifetime.Cancel();
        foreach (var task in tasks) { try { await task; } catch (OperationCanceledException) { } }
        for (var i = 0; i < 100 && service.PendingJobs != 0; i++) await Task.Delay(10);
        Check(service.PendingJobs == 0 && service.PeakActiveJobs <= 2, "cancelled queued jobs drain without accumulating");
        // A tall image must remain inside the square decode bound without distortion.
        var portraitPath = Path.Combine(directory, "portrait.png");
        var pixels = new byte[40 * 2000 * 4]; Array.Fill(pixels, (byte)160);
        var bitmap = BitmapSource.Create(40, 2000, 96, 96, PixelFormats.Bgra32, null, pixels, 40 * 4); bitmap.Freeze();
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = File.Create(portraitPath)) encoder.Save(output);
        var portrait = await service.GetAsync(new WallpaperItem { Path = portraitPath });
        Check(portrait != null && portrait.PixelHeight == 480 && portrait.PixelWidth <= 10, "portrait decode caps longest side and preserves aspect ratio");
        using var reopened = new ThumbnailService(directory, budget);
        Check(await reopened.GetAsync(items[10]) != null, "persistent image cache is reusable by another service");
        Check(!Directory.EnumerateFiles(Path.Combine(directory, "thumbnails"), "*.tmp", SearchOption.AllDirectories).Any(), "owned image temporary files cleaned");
        Check(await service.GetAsync(new WallpaperItem { Path = Path.Combine(directory, "missing.jpg") }) == null, "missing source returns honest empty thumbnail");
        var bad = Path.Combine(directory, "bad.jpg"); File.WriteAllText(bad, "invalid image");
        Check(await service.GetAsync(new WallpaperItem { Path = bad }) == null, "corrupt source handled without exception to UI");
        Console.WriteLine("PASS Thumbnail cache checks; fixture " + directory);
    }
}