using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TienDang.App;
using TienDang.Core;
internal static class UrlChecks
{
    internal static async Task Run(MainWindow main, LibraryState state, string directory, string server)
    {
        void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS URL " + name); }
        async Task Wait(Func<bool> value) { var watch=Stopwatch.StartNew(); while (!value()) { if(watch.Elapsed.TotalSeconds>25) throw new Exception("timeout in URL check"); await Task.Delay(60); } }
        var store = new StateStore(directory);
        UrlImportWindow Open(string route) { var w=new UrlImportWindow(store,server+route,r=>main.RegisterDownloadedAsync(r,Playlist.AllId),state.Items) { Owner=main }; w.Show(); return w; }
        T C<T>(UrlImportWindow w,string name) where T:FrameworkElement => (T)w.FindName(name);
        void Render(UrlImportWindow w,string name) { w.UpdateLayout(); var v=(FrameworkElement)w.Content; var r=new RenderTargetBitmap((int)v.ActualWidth,(int)v.ActualHeight,96,96,PixelFormats.Pbgra32);r.Render(v);var e=new PngBitmapEncoder();e.Frames.Add(BitmapFrame.Create(r));using var f=File.Create(Path.Combine(directory,name));e.Save(f); }
        // IPC/file metadata readiness precedes the first rendered frame.
        async Task CaptureVideo(UrlImportWindow window, string name)
        {
            await Wait(() => C<WallpaperPreview>(window, "VideoPreview").HasVideoFrame);
            await C<WallpaperPreview>(window, "VideoPreview").RenderSnapshotAsync(() => Render(window, name));
        }
        var before=state.Items.Count;
        if (L.State.Language == "en") { var english = Open("/video.mp4"); Check(english.Title == "Add wallpaper from URL", "English URL dialog title"); english.Close(); }
        var video=Open("/video.mp4");await video.AnalyzeAsync();await Wait(()=>C<WallpaperPreview>(video,"VideoPreview").IsVideoReady && C<WallpaperPreview>(video,"VideoPreview").MediaWidth>0);
        Check(C<WallpaperPreview>(video,"VideoPreview").MediaWidth==960,"remote MP4 preview and actual dimensions");
        await CaptureVideo(video, "native-url-ready.png");
        await video.DownloadAsync();Check(video.Phase=="complete"&&state.Items.Count==before+1,"download MP4 registers exactly once");
        var item=state.Items.Single(i=>i.SourceUrl==server+"/video.mp4");
        Check(File.Exists(item.Path)&&store.Load().Items.Any(i=>i.Id==item.Id),"download persists on disk and in library");
        await Wait(()=>C<WallpaperPreview>(video,"VideoPreview").IsVideoReady);
        await CaptureVideo(video, "native-url-complete.png");video.Close();
        var duplicate=Open("/video.mp4");await duplicate.AnalyzeAsync();Check(duplicate.Phase=="complete"&&state.Items.Count==before+1,"duplicate source reuses existing wallpaper");duplicate.Close();
        var webm=Open("/unknown.webm");await webm.AnalyzeAsync();await Wait(()=>C<WallpaperPreview>(webm,"VideoPreview").IsVideoReady && C<WallpaperPreview>(webm,"VideoPreview").MediaWidth>0);
        Check(C<WallpaperPreview>(webm,"VideoPreview").MediaWidth==640,"remote WebM preview");
        var webmJob=webm.DownloadAsync();await Wait(()=>webm.Phase=="downloading"&&C<TextBlock>(webm,"ProgressStats").Text.Contains(L.State.Language == "en" ? "total size unknown" : "chưa biết"));
        Check(C<ProgressBar>(webm,"DownloadProgressBar").IsIndeterminate&&C<TextBlock>(webm,"ProgressPercent").Text=="","unknown total has no invented percentage");
        await webmJob;Check(webm.Phase=="complete"&&state.Items.Any(i=>i.SourceUrl==server+"/unknown.webm"&&i.IsVideo),"WebM downloads and imports");
        await Wait(()=>C<WallpaperPreview>(webm,"VideoPreview").IsVideoReady && C<WallpaperPreview>(webm,"VideoPreview").MediaWidth>0);await C<WallpaperPreview>(webm,"VideoPreview").ToggleAsync();Check(!C<WallpaperPreview>(webm,"VideoPreview").IsPaused,"downloaded WebM plays");webm.Close();
        var baseline=state.Items.Count;var cancel=Open("/slow.webm");await cancel.AnalyzeAsync();var cancelJob=cancel.DownloadAsync();
        await Wait(()=>cancel.Phase=="downloading"&&C<ProgressBar>(cancel,"DownloadProgressBar").Value>1);
        C<Button>(cancel,"CancelButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await cancelJob;
        Check(state.Items.Count==baseline&&!Directory.GetFiles(Path.Combine(directory,"media")).Any(f=>f.Contains(".part")),"cancel leaves library and owned files clean");cancel.Close();
        var broken=Open("/corrupt.mp4");await broken.AnalyzeAsync();await broken.DownloadAsync();
        Check(state.Items.Count==baseline&&C<Border>(broken,"ErrorPanel").Visibility==Visibility.Visible,"corrupt media rejected before registration");broken.Close();
        var html=Open("/page.mp4");await html.AnalyzeAsync();Check(C<Border>(html,"ErrorPanel").Visibility==Visibility.Visible&&state.Items.Count==baseline,"web page rejected"); if (L.State.Language == "en") Check(C<TextBlock>(html, "ErrorText").Text.StartsWith("This link is a web page or document."), "Core Content-Type errors are translated to English"); html.Close();
        var image=Open("/image.jpg");await image.AnalyzeAsync();await Wait(()=>C<Image>(image,"ImagePreview").Source!=null);await image.DownloadAsync();Check(image.Phase=="complete"&&state.Items.Count==baseline+1,"URL image preview and import");image.Close();
        Check(!state.Settings.WasRunning,"URL inspection never changes desktop wallpaper");
        Console.WriteLine("PASS URL renders: "+directory);
    }
}
