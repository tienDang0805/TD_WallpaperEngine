using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Windows.Markup;
using TD_Wallpaper.App;
using TD_Wallpaper.Core;

internal static class Program
{
    internal static string FixtureImage(string root)
    {
        var demo = Path.Combine(root, "design-demos", "assets", "galaxy-poster.jpg");
        return File.Exists(demo) ? demo : Path.Combine(root, "src", "TD_Wallpaper.App", "StarterPack", "forest-valley.jpg");
    }
    private static MainWindow? _window;
    private static int _failures;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--naming")) return NamingChecks.Run();
        if (args[0] == "--desktop-lease-child") return DesktopLeaseChecks.Child(args[1], args[2]);
        if (args.Contains("--desktop-lease")) { DesktopLeaseChecks.Run().GetAwaiter().GetResult(); return 0; }
        if (args.Contains("--inspect-desktop-owner")) { foreach (var display in NativeDesktop.Displays()) Console.WriteLine(display.Id + " OtherRenderer=" + NativeDesktop.OtherRenderer(display.Id)); return 0; }
        if (args[0] == "--native-worker") return WorkerChecks.Child(args[1], args[2], (nint)long.Parse(args[3]));
        if (args[0] == "--activity-child" ) return ActivityChecks.Child(args[1]);
        if (args[0] == "--ipc-child" ) return IpcChecks.Child(args[1], args[2]).GetAwaiter().GetResult();
        if (args[0] == "--ipc-checks") { IpcChecks.Run().GetAwaiter().GetResult(); return 0; }
        if (args.Contains("--cleanup-stress") || args.Contains("--cleanup-soak") || args.Contains("--transitions")) StaticFramePresentation.ConfigureWorker();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var root = Path.GetFullPath(args[0]);
        var doc = XDocument.Load(Path.Combine(root, "src", "TD_Wallpaper.App", "App.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = new XElement(ns + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), doc.Root!.Element(ns + "Application.Resources")!.Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        if (args.Contains("--audit-persistence")) return PerformanceAudit.AuditPersistence(Path.GetFullPath(args[Array.IndexOf(args, "--audit-output") + 1]));
        if (args.Contains("--audit")) return PerformanceAudit.Run(app, root, args);
        if (args.Contains("--filmstrip")) return FilmstripChecks.Run(root, args);
        if (args.Contains("--cache-checks")) { ThumbnailChecks.Run(root).GetAwaiter().GetResult(); return 0; }
        if (args.Contains("--resource-isolation")) return ResourceIsolationChecks.Run(app, root);
        if (args.Contains("--cleanup-soak"))
        {
            var hours = int.Parse(args[Array.IndexOf(args, "--hours") + 1]);
            if (hours is not (8 or 24)) throw new ArgumentException("Soak hours must be 8 or 24.");
            return CleanupStressChecks.Run(app, root, Path.GetFullPath(args[Array.IndexOf(args, "--cleanup-soak") + 1]), hours * 6, 600_000,
                Path.Combine(root, "artifacts", "stage3-soak-" + hours + "h.json"));
        }
        if (args.Contains("--cleanup-stress")) return CleanupStressChecks.Run(app, root, Path.GetFullPath(args[Array.IndexOf(args, "--cleanup-stress") + 1]));
        if (args.Contains("--activity")) return ActivityChecks.Run(app, root);
        if (args.Contains("--phase4")) return PhaseFourUiChecks.Run(app, root);
        if (args.Contains("--controller")) return ControllerChecks.Run(app);
        if (args.Contains("--taskbar")) return TaskbarChecks.Run(app, args.Contains("--taskbar-live"));
        if (args.Contains("--loops")) return LoopChecks.Run(app, Path.GetFullPath(args[Array.IndexOf(args, "--loops") + 1]));
        if (args.Contains("--renderer-reuse")) return RendererReuseChecks.Run(app, Path.GetFullPath(args[Array.IndexOf(args, "--renderer-reuse") + 1]));
        if (args.Contains("--diagnostics")) return DiagnosticsChecks.Run(root);
        if (args.Contains("--stage2")) return StageTwoChecks.Run(root, args);
        var directory = Path.Combine(Path.GetTempPath(), "TD_Wallpaper-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var video = new WallpaperItem { Id = "video", Name = "Galaxy Eye Anime Girl", Path = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(root, "src", "TD_Wallpaper.App", "StarterPack", "galaxy-eye.mp4"), IsVideo = true };
        var image = new WallpaperItem { Id = "image", Name = "Galaxy Eye · ảnh thử", Path = Program.FixtureImage(root) };
        var brokenPath = Path.Combine(directory, "broken.mp4"); File.WriteAllText(brokenPath, "invalid video");
        var broken = new WallpaperItem { Id = "broken", Name = "Video lỗi thử", Path = brokenPath, IsVideo = true };
        var state = new LibraryState { Items = [video, image, broken] };
        if (args.Contains("--english")) state.Settings.Language = "en";
        if (args.Contains("--transitions")) return TransitionChecks.Run(app, video.Path, image.Path);
        _window = new MainWindow(state, new StateStore(directory), true);
        _window.Loaded += async (_, _) =>
        {
            try
            {
                if (!args.Contains("--url-only") && !args.Contains("--youtube-only") && !args.Contains("--save-failures-only") && !args.Contains("--ux-only")) await Check(_window, state, video, image, broken, directory);
                if (args.Contains("--save-failures")) await SaveFailureChecks.Run(_window, state, directory);
                if (args.Contains("--ux")) await UserExperienceChecks.Run(_window, state, video, directory);
                if (args.Contains("--qol")) await QolChecks.Run(_window, state, video, image, directory);
                if (args.Contains("--url")) await UrlChecks.Run(_window, state, directory, args[Array.IndexOf(args, "--url") + 1]);
                if (args.Contains("--youtube")) await YouTubeUiChecks.Run(_window, state, directory, args[Array.IndexOf(args, "--youtube") + 1]);
                Console.WriteLine("PASS Native UI checks; renders: " + directory);
            }
            catch (Exception ex) { _failures++; Console.WriteLine("FAIL " + ex); }
            finally { _window.Exit(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        _window.Show(); Dispatcher.Run();
        return _failures == 0 ? 0 : 1;
    }
    private static async Task Check(MainWindow window, LibraryState state, WallpaperItem video, WallpaperItem image, WallpaperItem broken, string directory)
    {
        var list = (ListBox)window.FindName("WallpaperList");
        var preview = (WallpaperPreview)window.FindName("InlinePreview");
        await Until(() => preview.IsVideoReady, "real video inline preview loads");
        Require(preview.IsPaused, "preview starts paused");
        var playerId = preview.PreviewProcessId; Require(playerId > 0, "preview owns a video process");
        var alternate = new WallpaperItem { Id = "reuse-video", Name = "Second video selection", Path = video.Path, IsVideo = true };
        await preview.ShowItemAsync(alternate);
        Require(preview.IsVideoReady && preview.HasVideoFrame && preview.PreviewProcessId == playerId, "video selection reuses renderer and waits for decoded frame");
        await preview.ShowItemAsync(video);
        Require(preview.PreviewProcessId == playerId && preview.SelectedId == video.Id, "reused preview returns to selected video");
        await preview.ToggleAsync(); Require(!preview.IsPaused, "play");
        await Task.Delay(700); await preview.ToggleAsync(); Require(preview.IsPaused, "pause");
        await preview.SeekAsync(3);
        await preview.RenderSnapshotAsync(() => window.SaveRender(Path.Combine(directory, "A-native-video-1250.png")));
        var cards = list.Items.Cast<WallpaperCard>().ToList();
        await Until(() => cards.First(c => c.Item.Id == video.Id).Thumbnail != null, "video has real thumbnail");
        await Until(() => cards.First(c => c.Item.Id == image.Id).Thumbnail != null, "image has real thumbnail");
        await Until(() => cards.First(c => c.Item.Id == broken.Id).ThumbnailStatus == L.Text("Chưa có ảnh xem trước"), "invalid video honest fallback");
        Require(state.Settings.WasRunning == false, "preview never applies desktop wallpaper");
        list.SelectedItem = cards.First(c => c.Item.Id == image.Id);
        await Until(() => preview.ImageSource != null && preview.SelectedId == image.Id, "inline image preview");
        Require(preview.PreviewProcessId == 0, "image closes prior player");
        await Task.Delay(300); Require(!Alive(playerId), "owned player exits");
        window.SaveRender(Path.Combine(directory, "A-native-1250.png"));
        window.Width = 1040; window.Height = 690; await Task.Delay(200);
        window.SaveRender(Path.Combine(directory, "A-native-1040.png"));
        var apply = (Button)window.FindName("ApplySelectedButton");
        var rotate = (Button)window.FindName("RotationButton");
        Require(apply.IsVisible && apply.ActualWidth > 90 && rotate.IsVisible && rotate.ActualWidth > 90, "main actions remain usable at minimum window");
        ((RadioButton)window.FindName("ImageFilter")).IsChecked = true; Require(list.Items.Count == 1, "image filter");
        ((RadioButton)window.FindName("VideoFilter")).IsChecked = true; Require(list.Items.Count == 2, "video filter");
        ((TextBox)window.FindName("SearchBox")).Text = "nothing-matches-123"; await Until(() => !window.SearchPending && list.Items.Count == 0, "search no results");
        ((TextBox)window.FindName("SearchBox")).Text = ""; ((RadioButton)window.FindName("AllFilter")).IsChecked = true;
        cards = list.Items.Cast<WallpaperCard>().ToList();
        for (var i = 0; i < 4; i++)
        {
            list.SelectedItem = cards.First(c => c.Item.Id == video.Id);
            await Task.Delay(35);
            list.SelectedItem = cards.First(c => c.Item.Id == image.Id);
        }
        await Until(() => preview.ImageSource != null && preview.SelectedId == image.Id && preview.PreviewProcessId == 0, "rapid selection retains latest image");
        list.SelectedItem = cards.First(c => c.Item.Id == video.Id);
        await Until(() => preview.IsVideoReady, "video can reopen");
        var hiddenPlayer = preview.PreviewProcessId;
        window.Hide(); await Task.Delay(250); Require(preview.PreviewProcessId == 0 && !Alive(hiddenPlayer), "hidden window closes preview");
        window.Show(); await Until(() => preview.IsVideoReady, "show restores paused preview");
        Require(preview.IsPaused, "restored preview stays paused");
        list.SelectedItem = cards.First(c => c.Item.Id == broken.Id);
        await Until(() => preview.PreviewProcessId == 0, "broken video fails without a leaked player");
        list.SelectedItem = cards.First(c => c.Item.Id == image.Id);
        await Until(() => preview.ImageSource != null, "image recovers after broken video");
        var settings = new SettingsWindow(state.Settings, false);
        Require(settings.Result != state.Settings && settings.Result.IntervalSeconds == state.Settings.IntervalSeconds, "settings draft preserves state until save");
        settings.Close();
        using var thumbnails = new ThumbnailService(directory);
        var cached = await thumbnails.GetAsync(video);
        Require(cached != null && cached.PixelWidth == 480, "persistent video cache reads real frame");
        Require(!Directory.EnumerateDirectories(Path.Combine(directory, "thumbnails"), "work-*").Any(), "thumbnail jobs clean temporary directories");
        window.Width = 1440; window.Height = 900; await Task.Delay(200);
        window.SaveRender(Path.Combine(directory, "A-native-1440.png"));
        Require(!state.Settings.WasRunning, "all UI inspection remains independent of desktop playback");
        if (Path.GetExtension(video.Path).Equals(".webm", StringComparison.OrdinalIgnoreCase))
        {
            var importFolder = Path.Combine(directory, "webm-import");
            Directory.CreateDirectory(Path.Combine(importFolder, "nested"));
            var importedPath = Path.Combine(importFolder, "Imported.WeBm");
            File.Copy(video.Path, importedPath);
            await window.Import([importedPath]);
            var imported = state.Items.Single(i => i.OriginalPath == importedPath);
            Require(imported.IsVideo && MediaTypes.Supported(imported.Path), "WebM file import is classified as video");
            list.SelectedItem = list.Items.Cast<WallpaperCard>().Single(c => c.Item.Id == imported.Id);
            await Until(() => preview.SelectedId == imported.Id && preview.IsVideoReady, "imported WebM inline preview");
            await Until(() => list.Items.Cast<WallpaperCard>().Single(c => c.Item.Id == imported.Id).Thumbnail != null, "imported WebM thumbnail");
            var nestedPath = Path.Combine(importFolder, "nested", "Folder.WebM");
            File.Copy(video.Path, nestedPath);
            state.Settings.CopyOnImport = true;
            var count = state.Items.Count;
            await window.Import([importFolder]);
            Require(state.Items.Count == count + 1, "folder import finds nested WebM and skips existing file");
            var copied = state.Items.Single(i => i.OriginalPath == nestedPath);
            Require(copied.IsVideo && File.Exists(copied.Path) && copied.Path != copied.OriginalPath &&
                Path.GetExtension(copied.Path).Equals(".webm", StringComparison.OrdinalIgnoreCase), "copy-on-import preserves WebM format");
            list.SelectedItem = list.Items.Cast<WallpaperCard>().Single(c => c.Item.Id == copied.Id);
            await Until(() => preview.SelectedId == copied.Id && preview.IsVideoReady, "copied WebM preview");
            state.Settings.CopyOnImport = false;
        }
        state.Items.Remove(broken); ((TextBox)window.FindName("SearchBox")).Text = " ";
        list.SelectedItem = list.Items.Cast<WallpaperCard>().First(c => c.Item.Id == video.Id);
        window.Width = 1250; window.Height = 820; await Until(() => preview.IsVideoReady, "final preview capture");
        await preview.SeekAsync(3); await Task.Delay(650);
        Require(Math.Abs(await preview.PositionAsync() - 3) < 0.15, "seek moves to requested time even before duration polling");
        await preview.RenderSnapshotAsync(() => window.SaveRender(Path.Combine(directory, "A-native-video-1250.png")));
    }
    private static void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
    private static async Task Until(Func<bool> condition, string name)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed.TotalSeconds > 25) throw new TimeoutException(name); await Task.Delay(100); }
        Console.WriteLine("PASS " + name);
    }
    private static bool Alive(int id)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
}
