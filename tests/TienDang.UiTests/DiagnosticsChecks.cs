using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TienDang.App;
using TienDang.Core;

internal static class DiagnosticsChecks
{
    internal static int Run(string root)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TienDang-diagnostics-" + Guid.NewGuid().ToString("N"));
        var state = new LibraryState { Items = [new() { Id = "video", Name = "Diagnostics video", Path = Path.Combine(root, "artifacts", "TienDang-fps-60.mp4"), IsVideo = true }] };
        var window = new MainWindow(state, new StateStore(directory), true);
        var result = 1;
        window.Loaded += async (_, _) =>
        {
            try { await Check(window, state, root, directory); result = 0; }
            catch (Exception ex) { Console.WriteLine("FAIL diagnostics " + ex); }
            finally { window.Exit(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run(); return result;
    }
    private static async Task Check(MainWindow window, LibraryState state, string root, string directory)
    {
        void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); Console.WriteLine("PASS diagnostics " + name); }
        async Task Until(Func<bool> condition) { var clock = Stopwatch.StartNew(); while (!condition()) { if (clock.Elapsed.TotalSeconds > 20) throw new TimeoutException("Video readiness"); await Task.Delay(15); } }
        Require(Startup.LaunchDelay([]) == TimeSpan.Zero && Startup.LaunchDelay(["--minimized"]) == TimeSpan.Zero, "manual and legacy minimized launch has no delay");
        Require(Startup.LaunchDelay(["--startup", "--minimized"]) == TimeSpan.FromSeconds(3), "logon launch has 3 second delay");
        Require(Startup.LaunchDelay(["--wallpaper", "pipe", "monitor", "--startup"]) == TimeSpan.Zero, "wallpaper worker never delayed");
        Require(Startup.CommandLine(@"C:\App Folder\wallpaper.exe", @"D:\My Library") == @"""C:\App Folder\wallpaper.exe"" --startup --minimized --data-dir ""D:\My Library""", "startup command preserves quoted exe/data paths and marker");
        Require(new PlayerDiagnostics().DecodeMode == "unknown" && !new PlayerDiagnostics().SoftwareFallback, "unknown hardware state is not GPU proof");
        Require(new PlayerDiagnostics { RequestedHwdec = "auto", HwdecCurrent = "no" }.SoftwareFallback, "software fallback from auto is detected");
        Require(new PlayerDiagnostics { RequestedHwdec = "no", HwdecCurrent = "no" }.SoftwareFallback == false, "explicit software mode is not fallback");
        Require(new PlayerDiagnostics { HwdecCurrent = "d3d11va-copy" }.DecodeMode == "hardware", "copy backend still means hardware decode");
        var preview = (WallpaperPreview)window.FindName("InlinePreview");
        await Until(() => preview.IsVideoReady && preview.HasVideoFrame);
        var first = (await preview.CaptureDiagnosticsAsync())!;
        Require(first.ProcessId == preview.PreviewProcessId && first.ProcessId > 0, "snapshot measures owned real decoder PID");
        Require(first.Codec != null && first.Width == 320 && first.Height == 180, "codec/dimensions from real MPV");
        Require(Math.Abs(first.SourceFps!.Value - 60) < .5, "source FPS read correctly");
        Require(first.FirstFrameMs > 0 && first.FileLoadedMs > 0, "monotonic startup milestones observed");
        Require(first.PrivateMiB > 0 && first.WorkingSetMiB > 0 && first.HandleCount > 0 && first.CpuSeconds >= 0, "native process memory/handle/CPU counters available");
        Require(first.CpuPercentAllCores == null, "first resource snapshot does not invent CPU percentage");
        Require(first.HwdecCurrent != null && first.VideoOutput == "gpu" && first.RequestedHwdec == "auto", "actual hardware backend and output are recorded");
        await Task.Delay(1100);
        var second = (await preview.CaptureDiagnosticsAsync())!;
        Require(second.CpuSampleSeconds >= 1 && second.CpuPercentAllCores >= 0, "CPU delta sampled with explicit interval");
        await preview.SeekAsync(2); await Task.Delay(100);
        Require((await preview.CaptureDiagnosticsAsync())!.FirstFrameMs == first.FirstFrameMs, "seek/playback-restart does not reset first-frame milestone");
        L.SetLanguage("en");
        var english = DiagnosticsWindow.FormatVideo(second);
        Require(english.Contains("Active decoder backend") && !english.Contains("Chưa có số đo"), "diagnostics English labels translated");
        Require(DiagnosticsWindow.FormatVideo(new()).Contains("Not available"), "unavailable metrics remain unavailable, not zero");
        L.SetLanguage("vi"); Require(DiagnosticsWindow.FormatVideo(second).Contains("Giải mã đang dùng"), "Vietnamese labels are readable");
        var fallback = new PlayerDiagnostics { RequestedHwdec="auto", HwdecCurrent="no" };
        Require(DiagnosticsWindow.FormatVideo(fallback).Contains("H.264 8-bit"), "fallback guidance offers lighter media without stopping playback");
        Require(DiagnosticsWindow.FormatVideo(new PlayerDiagnostics { HwdecCurrent="d3d11va-copy" }).Contains("chép về RAM"), "copy backend guidance explains CPU processing cost");
        var engine = (WallpaperEngine)typeof(MainWindow).GetField("_engine", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        var notices=0; string lastNotice=""; engine.Problem += message => {notices++;lastNotice=message;};
        engine.ReportSoftwareFallback("item","Custom name",new PlayerDiagnostics {RequestedHwdec="auto",HwdecCurrent="d3d11va"});
        Require(notices==0,"hardware decode does not show software warning");
        engine.ReportSoftwareFallback("item","Custom name",fallback); engine.ReportSoftwareFallback("item","Custom name",fallback);
        Require(notices==1 && lastNotice.EndsWith("Custom name"), "fallback notice is once per item and preserves user name");
        engine.LibraryChanged(); L.SetLanguage("en"); engine.ReportSoftwareFallback("item","Custom name",fallback);
        Require(notices==2 && lastNotice.StartsWith("Software decoding is active."), "relink/library refresh permits a fresh English fallback notice");
        L.SetLanguage("vi");
        var text = await window.CaptureDiagnosticTextAsync(); Require(text.Contains("Video xem trước") && !state.Settings.WasRunning, "diagnostics does not apply desktop or save settings");
        var dialog = new DiagnosticsWindow(window.CaptureDiagnosticTextAsync) { Owner = window };
        dialog.Show(); await Task.Delay(200); await dialog.RefreshAsync();
        await Until(() => dialog.Details.Length > 100);
        window.SaveRender(Path.Combine(directory, "main-with-diagnostics.png"));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(dialog);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(directory, "diagnostics-vi.png"))) encoder.Save(file);
        dialog.Close();
        File.WriteAllText(Path.Combine(directory, "gpu-snapshots.json"), JsonSerializer.Serialize(new[] {first,second}, new JsonSerializerOptions {WriteIndented=true}));
        using (var empty = new MpvPlayer(0, true))
        {
            var sample = await empty.CaptureDiagnosticsAsync();
            Require(sample.Codec == null && sample.DecodeMode == "unknown" && sample.UnavailableProperties.Length > 0, "before connection missing properties handled safely");
        }
        using (var player = new MpvPlayer(0, true))
        {
            var frame = new TaskCompletionSource(); player.FrameReady += () => frame.TrySetResult();
            await player.StartAsync(state.Items[0].Path, true, 0, "Fit", true);
            await frame.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var jobs = Enumerable.Range(0, 30).Select(_ => player.CaptureDiagnosticsAsync()).ToArray();
            Require(jobs.All(j => ReferenceEquals(j,jobs[0])), "concurrent refreshes share one snapshot job");
            var sample = await jobs[0];
            Require(sample.Headless && sample.DecodeMode == "software" && !sample.SoftwareFallback, "headless software does not falsely prove GPU/fallback");
            Require(player.PendingCommandCount == 0, "diagnostic requests are removed after completion");
            var pending = player.CaptureDiagnosticsAsync(); player.Dispose();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Require(player.PendingCommandCount == 0, "close during snapshot cancels owned pending IPC without hanging");
        }
        var presenterWindow = new Window { Title="Diagnostics presenter fixture", Width=600, Height=400, Content=new Grid() }; presenterWindow.Show();
        using (var presenter = new WallpaperPresenter(presenterWindow))
        {
            await presenter.LoadAsync(new() { Generation=41, Path=Program.FixtureImage(root) });
            var image = await presenter.CaptureDiagnosticsAsync();
            Require(image.ActiveGeneration==41 && image.ActiveKind=="image" && image.ActiveVideo==null, "image layer does not invent video diagnostics");
            await presenter.LoadAsync(new() { Generation=42, IsVideo=true, Path=state.Items[0].Path, FrameRateLimit=30 });
            var video = await presenter.CaptureDiagnosticsAsync();
            Require(video.ActiveGeneration==42 && video.ActiveVideo?.FrameRateLimit==30 && video.ActiveVideo.RequestedHwdec=="auto-copy" && video.ActiveVideo.ProcessId==presenter.ActiveProcessId, "presenter generation/FPS/PID reflect current layer");
            var dto = new PlayerMessage { Command="diagnostics",RequestId="correlation-42",Generation=42,Diagnostics=video };
            var roundTrip=JsonSerializer.Deserialize<PlayerMessage>(JsonSerializer.Serialize(dto))!;
            Require(roundTrip.RequestId==dto.RequestId && roundTrip.Diagnostics!.ActiveVideo!.HwdecCurrent==video.ActiveVideo!.HwdecCurrent, "worker IPC DTO preserves correlation and observed backend");
            Console.WriteLine("GPU diagnostics: "+JsonSerializer.Serialize(video.ActiveVideo));
        }
        presenterWindow.Close();
        Require(!state.Settings.WasRunning, "all test windows are isolated from personal desktop");
        Console.WriteLine("PASS diagnostics artifacts: "+directory);
    }
}