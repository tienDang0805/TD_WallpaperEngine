using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TienDang.App;
using TienDang.Core;

internal static class YouTubeUiChecks
{
    internal static async Task Run(MainWindow main, LibraryState state, string directory, string url)
    {
        void Check(bool value, string name) { if (!value) throw new System.Exception(name); System.Console.WriteLine("PASS YouTube UI " + name); }
        async Task Wait(System.Func<bool> value) { var watch = Stopwatch.StartNew(); while (!value()) { if (watch.Elapsed.TotalSeconds > 30) throw new System.Exception("preview timeout"); await Task.Delay(60); } }
        T C<T>(UrlImportWindow window, string name) where T : FrameworkElement => (T)window.FindName(name);
        void Render(UrlImportWindow window, string name)
        {
            window.UpdateLayout(); var view = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
        }
        var store = new StateStore(directory); var count = state.Items.Count;
        var playlist = new Playlist { Name = "YouTube test" }; state.Playlists.Add(playlist);
        state.Settings.CopyOnImport = true;
        var window = new UrlImportWindow(store, url, result => main.RegisterDownloadedAsync(result, playlist.Id), state.Items) { Owner = main };
        window.Show(); await window.AnalyzeAsync();
        Check(window.Phase == "ready", "real YouTube extractor returns title and metadata: " + C<TextBlock>(window, "ErrorText").Text);
        Check(C<TextBlock>(window, "FileMetaText").Text.Contains("YouTube"), "YouTube flow selected before HTTP media inspection");
        await Task.Delay(500); Render(window, "youtube-ready.png");
        var external = Path.Combine(directory, "chosen-download-folder"); Directory.CreateDirectory(external);
        typeof(UrlImportWindow).GetField("_directory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(window, external);
        C<CheckBox>(window, "UseDefaultFolder").IsChecked = false;
        var job = window.DownloadAsync();
        var sawProgress = false;
        while (!job.IsCompleted)
        {
            if (C<TextBlock>(window, "ProgressStats").Text.Contains("/s") || C<TextBlock>(window, "ProgressLabel").Text.Contains("ghép")) sawProgress = true;
            await Task.Delay(50);
        }
        await job;
        Check(window.Phase == "complete", "real download/remux succeeds: " + C<TextBlock>(window, "ErrorText").Text);
        Check(sawProgress, "native download or merge progress displayed");
        var item = window.CompletedItem!;
        Check(state.Items.Count == count + 1 && item.IsVideo && item.SourceUrl == YouTubeDownloader.CanonicalUrl(new System.Uri(url)).AbsoluteUri, "automatically registers once using canonical source");
        Check(item.Path.StartsWith(Path.Combine(directory, "media")) && item.OriginalPath.StartsWith(external) &&
            File.Exists(item.Path) && File.Exists(item.OriginalPath), "shared local import respects copy-on-import for chosen folder");
        Check(playlist.ItemIds.Contains(item.Id) && store.Load().Items.Any(i => i.Id == item.Id), "playlist membership and library persisted");
        Check(!Directory.GetDirectories(external).Any(), "no staging folder after completion");
        await Wait(() => C<WallpaperPreview>(window, "VideoPreview").IsVideoReady && C<WallpaperPreview>(window, "VideoPreview").HasVideoFrame);
        await C<WallpaperPreview>(window, "VideoPreview").RenderSnapshotAsync(() => Render(window, "youtube-complete.png"));
        await C<WallpaperPreview>(window, "VideoPreview").ToggleAsync();
        Check(!C<WallpaperPreview>(window, "VideoPreview").IsPaused, "downloaded MP4 plays locally");
        window.Close();
        var duplicate = new UrlImportWindow(store, "https://youtu.be/" + item.SourceUrl.Split("v=")[1] + "?si=test",
            result => main.RegisterDownloadedAsync(result, playlist.Id), state.Items) { Owner = main };
        duplicate.Show(); await duplicate.AnalyzeAsync(); Check(duplicate.Phase == "complete" && state.Items.Count == count + 1, "short URL identifies existing wallpaper"); duplicate.Close();
        Check(!state.Settings.WasRunning, "YouTube import does not apply desktop wallpaper");
        System.Console.WriteLine("YOUTUBE_FILE:" + item.OriginalPath);
    }
}