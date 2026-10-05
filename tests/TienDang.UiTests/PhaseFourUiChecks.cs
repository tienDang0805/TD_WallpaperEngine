using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using TienDang.App;
using TienDang.Core;
internal static class PhaseFourUiChecks
{
    private static void Require(bool value, string text) { if (!value) throw new Exception(text); Console.WriteLine("PASS Phase4UI " + text); }
    private static async Task Until(Func<bool> condition) { var timer = Stopwatch.StartNew(); while (!condition()) { if (timer.Elapsed > TimeSpan.FromSeconds(25)) throw new TimeoutException("Phase4 wait"); await Task.Delay(35); } }
    internal static int Run(Application app, string root)
    {
        var code = 1; app.Dispatcher.BeginInvoke(async () => { try { await Check(root); code = 0; } catch (Exception error) { Console.WriteLine("FAIL Phase4UI " + error); } finally { app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); } }); Dispatcher.Run(); return code;
    }
    private static async Task Check(string root)
    {
        var data = Path.Combine(Path.GetTempPath(), "TD-phase4-ui-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(data);
        var state = new LibraryState(); state.Settings.Language = "en"; var window = new MainWindow(state, new StateStore(data), true); window.Show();
        try
        {
            L.SetLanguage("en"); await window.AddStarterAsync();
            Require(state.Items.Count == 4 && state.Items.Count(i => i.IsVideo) == 2 && state.Items.All(i => i.Exists && LibraryUtilities.IsOwnedFile(Path.Combine(data, "media"), i.Path)), "starter seeds four owned playable assets without external paths");
            await window.AddStarterAsync(); Require(state.Items.Count == 4, "starter add deduplicates");
            var starterList = (ListBox)window.FindName("WallpaperList");
            await Until(() => starterList.Items.Cast<WallpaperCard>().All(card => card.Thumbnail != null));
            window.SaveRender(Path.Combine(data, "starter-en.png"));
            var preferences = new SettingsWindow(state.Settings, false) { Owner = window };
            preferences.Show(); await Task.Delay(150);
            Capture(preferences, Path.Combine(data, "settings-en.png"));
            preferences.Close();
            var brand = new System.Windows.Media.Imaging.FormatConvertedBitmap((System.Windows.Media.Imaging.BitmapSource)((Image)window.FindName("StandingMascot")).Source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var corner = new byte[4]; var center = new byte[4];
            brand.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
            brand.CopyPixels(new Int32Rect(brand.PixelWidth / 2, brand.PixelHeight / 2, 1, 1), center, 4, 0);
            Require(corner[3] == 0 && center[3] > 0, "library mascot has transparent exterior and retained interior artwork");
            Require(!ReferenceEquals(((Image)window.FindName("EmptyMascot")).Source, ((Image)window.FindName("StandingMascot")).Source) && !ReferenceEquals(((Image)window.FindName("DeskMascot")).Source, ((Image)window.FindName("StandingMascot")).Source), "empty-box, laptop and library mascots have distinct supplied assets");
            var tools = new LibraryToolsWindow(window) { Owner = window }; tools.Show(); await Task.Delay(100);
            Capture(tools, Path.Combine(data, "library-tools-en.png"));
            Require(Elements<Button>(tools).Where(b => b.IsVisible).All(b => b.Focusable && !string.IsNullOrWhiteSpace(b.Content?.ToString())), "library tools buttons have readable labels and keyboard focus");
            Require(new System.Windows.Automation.Peers.ButtonAutomationPeer((Button)window.FindName("ExpandPreviewButton")).GetName().Contains("preview", StringComparison.OrdinalIgnoreCase), "icon preview button has an English accessible name");
            tools.Close();
            L.SetLanguage("vi");
            var viet = new LibraryToolsWindow(window) { Owner = window }; viet.Show(); await Task.Delay(100);
            Capture(viet, Path.Combine(data, "library-tools-vi.png")); Require(viet.Title.StartsWith("Tiện ích thư viện"), "library tools supports natural Vietnamese"); viet.Close(); L.SetLanguage("en");
            Require(window.Title.Contains("TD-WallpaperEngine"), "public product name has no personal name");
            var tcp = new TcpListener(IPAddress.Loopback, 0); tcp.Start(); var port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
            using var listener = new HttpListener(); listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
            var sample = Program.FixtureImage(root); var requests = 0; var peak = 0; var active = 0;
            async Task Serve()
            {
                while (listener.IsListening)
                {
                    HttpListenerContext context;
                    try { context = await listener.GetContextAsync(); } catch (HttpListenerException) { break; }
                    try
                    {
                        active++; peak = Math.Max(peak, active); requests++;
                        if (context.Request.Url!.AbsolutePath == "/bad") { context.Response.StatusCode = 404; continue; }
                        context.Response.ContentType = "image/jpeg"; context.Response.ContentLength64 = new FileInfo(sample).Length;
                        if (context.Request.HttpMethod != "HEAD") { await using var input = File.OpenRead(sample); await input.CopyToAsync(context.Response.OutputStream); }
                    }
                    finally { active--; context.Response.Close(); }
                }
            }
            var server = Serve(); var queue = window.Downloads;
            var first = queue.Enqueue(new Uri($"http://127.0.0.1:{port}/one.jpg"), Path.Combine(data, "media"), Playlist.AllId, 0);
            var same = queue.Enqueue(new Uri($"http://127.0.0.1:{port}/one.jpg"), Path.Combine(data, "media"), Playlist.AllId, 0); Require(ReferenceEquals(first, same), "queue deduplicates source");
            var second = queue.Enqueue(new Uri($"http://127.0.0.1:{port}/two.jpg"), Path.Combine(data, "media"), Playlist.AllId, 0);
            var canceled = queue.Enqueue(new Uri($"http://127.0.0.1:{port}/cancel.jpg"), Path.Combine(data, "media"), Playlist.AllId, 0); queue.Cancel(canceled);
            var failed = queue.Enqueue(new Uri($"http://127.0.0.1:{port}/bad"), Path.Combine(data, "media"), Playlist.AllId, 0);
            await Until(() => !queue.Busy);
            Require(first.Phase == "complete" && second.Phase == "complete" && state.Items.Count == 6, "serial direct URL queue auto-imports through existing registration");
            Require(canceled.Phase == "canceled" && failed.Phase == "failed" && !Directory.EnumerateFiles(Path.Combine(data, "media"), "*.part*").Any(), "queued cancel and HTTP failure leave no partial import");
            failed.Url = $"http://127.0.0.1:{port}/retry.jpg"; queue.Retry(failed); await Until(() => !queue.Busy); Require(failed.Phase == "complete" && state.Items.Count == 7, "failed job can retry and import");
            Require(peak == 1 && requests > 2, "queue keeps one transfer at a time"); listener.Stop(); await server;
            var detached = state.CreateSnapshot(); var item = detached.Items[0]; var retainedId = item.Id; item.Path = Path.Combine(data, "missing.mp4");
            state.Items[0].Path = item.Path;
            var proposal = new RelinkProposal(retainedId, item.Path, Path.Combine(data, "media", Path.GetFileName(state.Items[1].Path)));
            // A stale mapping must not apply over a newer library path.
            state.Items[0].Path = "changed.mp4";
            try { window.CommitRelinks([proposal]); throw new Exception("Stale relink accepted"); } catch (IOException) { Console.WriteLine("PASS Phase4UI stale relink rejected"); }
            state.Items[0].Path = detached.Items[0].Path;
            await ToolsetChecks.Run(root, data);
            await NativeProfile(root, data);
            Console.WriteLine("PASS Phase4UI artifacts: " + data);
        }
        finally { window.Exit(); }
        Console.WriteLine("PASS Phase4UI suite");
    }
    private static System.Collections.Generic.IEnumerable<T> Elements<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var value in Elements<T>(child)) yield return value;
        }
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
    private static async Task NativeProfile(string root, string data)
    {
        var parent = new Window { Width = 720, Height = 450, ShowActivated = false, Title = "TD owned resource test" }; parent.Show();
        var handle = new WindowInteropHelper(parent).Handle; var display = NativeDesktop.Displays()[0]; var busy = false;
        var item = new WallpaperItem { Id = "native", Path = Path.Combine(root, "src", "TienDang.App", "StarterPack", "galaxy-eye.mp4"), IsVideo = true };
        var state = new LibraryState { Items = [item] }; state.Settings.ReleaseWhenBusy = true;
        PlayerHost? host = null;
        var runtime = new PlaybackRuntime { Automatic = false, Displays = () => [display], Fullscreen = _ => busy, Maximized = _ => false, ForegroundProcess = _ => "", Battery = () => false, Remote = () => false,
            StartHost = async (id, callback, token) => host = await PlayerHost.Start(id, callback, token, pipe => WorkerChecks.Start(pipe, id, handle)) };
        using var engine = new WallpaperEngine(state, new StateStore(Path.Combine(data, "native")), runtime);
        try
        {
            await engine.Apply("*", Playlist.AllId, item.Id); await Until(() => engine.ControllerState.Single().Loaded);
            var before = (await host!.CaptureDiagnosticsAsync()).ActiveVideo!; var worker = host.ProcessId; var decoder = before.ProcessId;
            Require(before.DecodeMode == "hardware", "saver starts real H.264 GPU decoder");
            busy = true; engine.RequestPolicyRefresh(0); await Until(() => !engine.ControllerState.Single().Loaded);
            await host.ExitCompletion;
            bool Alive(int id) { try { using var p = Process.GetProcessById(id); return !p.HasExited; } catch (ArgumentException) { return false; } }
            Require(!Alive(worker) && !Alive(decoder), "saver releases actual owned worker and decoder memory");
            Console.WriteLine($"Profile benchmark decoder private before={before.PrivateMiB:F2}MiB after=0 owned process; GPU={before.HwdecCurrent}; worker {worker} and decoder {decoder} exited.");
            busy = false; engine.RequestPolicyRefresh(0); await Until(() => engine.ControllerState.Single().Loaded);
            Require(engine.ControllerState.Single().ActiveId == item.Id, "native saver resume restores same wallpaper");
            engine.Dispose(); await host!.ExitCompletion;
        }
        finally { parent.Close(); }
    }
}
