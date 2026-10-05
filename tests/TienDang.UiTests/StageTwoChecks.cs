using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TienDang.App;
using TienDang.Core;
internal static class StageTwoChecks
{
    internal static int Run(string root, string[] args)
    {
        var count = int.Parse(args[Array.IndexOf(args, "--stage2") + 1]);
        var output = Path.GetFullPath(args[Array.IndexOf(args, "--stage2-output") + 1]);
        var fixtureIndex = Array.IndexOf(args, "--fixture");
        var directory = fixtureIndex >= 0 ? Path.GetFullPath(args[fixtureIndex + 1]) : Path.Combine(Path.GetTempPath(), "TienDang-stage2-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var state = new LibraryState();
        for (var i = 0; i < count; i++)
        {
            var path = Path.Combine(directory, $"image-{i:D4}.jpg");
            if (!File.Exists(path)) File.Copy(Program.FixtureImage(root), path);
            state.Items.Add(new WallpaperItem { Id = i.ToString(), Name = $"Wallpaper {i:D4}", Path = path, Favorite = i % 2 == 0 });
        }
        var store = new StateStore(directory); var failures = new List<string>();
        void Check(bool condition, string message)
        { Console.WriteLine((condition ? "PASS " : "FAIL ") + "Stage2 " + message); if (!condition) failures.Add(message); }
        var diskDirectory = Path.Combine(directory, "thumbnails", "v2");
        var diskWarm = Directory.Exists(diskDirectory) && Directory.EnumerateFiles(diskDirectory).Any();
        var startup = Stopwatch.StartNew(); var window = new MainWindow(state, store, true) { Width = 1280, Height = 850 };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var loadedMs = startup.Elapsed.TotalMilliseconds;
                var list = (ListBox)window.FindName("WallpaperList"); var strip = (ListBox)window.FindName("Filmstrip");
                var search = (TextBox)window.FindName("SearchBox"); var preview = (WallpaperPreview)window.FindName("InlinePreview");
                var engine = (WallpaperEngine)typeof(MainWindow).GetField("_engine", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                async Task Wait(Func<bool> condition, string reason, int seconds = 25)
                {
                    var clock = Stopwatch.StartNew();
                    while (!condition()) { if (clock.Elapsed.TotalSeconds > seconds) throw new TimeoutException(reason); await Task.Delay(10); }
                }
                async Task Settle()
                { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle); await Task.Delay(30); window.UpdateLayout(); }
                async Task VisibleReady()
                {
                    await Settle();
                    await Wait(() => window.ThumbnailWantedCount > 0 && window.ThumbnailPendingRequests == 0 && window.ThumbnailPendingJobs == 0 &&
                        list.Items.Cast<WallpaperCard>().Count(c => c.Thumbnail != null) >= window.ThumbnailWantedCount, "visible thumbnails");
                }
                int Containers(ListBox control) => Enumerable.Range(0, control.Items.Count).Count(i => control.ItemContainerGenerator.ContainerFromIndex(i) != null);
                await VisibleReady(); await Wait(() => preview.ImageSource != null, "real image preview");
                var usableMs = startup.Elapsed.TotalMilliseconds; await Task.Delay(700);
                using var process = Process.GetCurrentProcess(); process.Refresh();
                var initialPrivateMiB = process.PrivateMemorySize64 / 1048576d;
                var initialResident = list.Items.Cast<WallpaperCard>().Count(c => c.Thumbnail != null);
                Check(usableMs <= 3000, "first viewport usable <=3s (" + usableMs.ToString("0") + "ms)");
                Check(initialResident <= 40 && window.ThumbnailStartedJobs <= 40, "startup only decodes viewport/selection");
                Check(initialPrivateMiB <= 250 || count > 1000, "initial private memory <=250MiB at 100/1000");
                var searchApply = new List<double>(); var searchEndToEnd = new List<double>();
                var inputClock = new Stopwatch(); var measuring = false;
                window.ViewRefreshed += applyMs =>
                {
                    if (!measuring) return;
                    var layout = Stopwatch.StartNew(); window.UpdateLayout();
                    searchApply.Add(applyMs + layout.Elapsed.TotalMilliseconds); searchEndToEnd.Add(inputClock.Elapsed.TotalMilliseconds);
                };
                for (var i = 0; i < 32; i++)
                {
                    search.Clear(); await Settle();
                    measuring = true; inputClock.Restart();
                    search.Text = new[] { "Wallpaper 000", "Wallpaper 00", "no-results-stage2", "Wallpaper" }[i % 4];
                    await Wait(() => !window.SearchPending, "debounced search"); measuring = false;
                }
                Check(searchApply.Count == 32, "32 measured query updates");
                double P95(List<double> values) => values.Order().ElementAt((int)Math.Ceiling(values.Count * .95) - 1);
                var applyP95 = P95(searchApply); var endP95 = P95(searchEndToEnd);
                Check(applyP95 <= 100, "search+layout p95 <=100ms (" + applyP95.ToString("0.0") + "ms)");
                Check(endP95 <= 250 || count > 1000, "search input-to-layout p95 <=250ms at 100/1000 (" + endP95.ToString("0.0") + "ms)");
                search.Clear(); await VisibleReady(); var beforeBurst = window.ViewRefreshCount;
                foreach (var text in new[] { "W", "Wa", "Wall", "Wallp", "Wallpaper" }) { search.Text = text; await Task.Delay(25); }
                Check(window.ViewRefreshCount == beforeBurst, "typing burst does not rebuild per key");
                await Wait(() => !window.SearchPending, "final burst query");
                Check(window.ViewRefreshCount == beforeBurst + 1, "one final query update per typing burst");
                var source = list.ItemsSource; var selected = list.SelectedItem;
                search.Text = "Wall"; await Wait(() => !window.SearchPending, "same sequence query");
                Check(ReferenceEquals(source, list.ItemsSource) && ReferenceEquals(selected, list.SelectedItem), "same result sequence keeps ItemsSource/selection");
                search.Clear(); await VisibleReady();
                var memorySamples = new List<double>(); var residentSamples = new List<int>();
                for (var round = 0; round < 3; round++)
                {
                    for (var i = 0; i < 24; i++)
                    {
                        var index = (int)((long)i * (count - 1) / 23);
                        list.SelectedIndex = index; list.ScrollIntoView(list.SelectedItem); await VisibleReady();
                    }
                    await Task.Delay(700); process.Refresh(); memorySamples.Add(process.PrivateMemorySize64 / 1048576d);
                    residentSamples.Add(list.Items.Cast<WallpaperCard>().Count(c => c.Thumbnail != null));
                    Check(residentSamples.Last() <= window.ThumbnailWantedCount && window.ThumbnailWantedCount <= 40, "offscreen card references released round " + round);
                    Check(window.ThumbnailCacheBytes <= ThumbnailService.DefaultBudget, "cache budget round " + round);
                }
                Check(window.ThumbnailPeakCacheBytes <= ThumbnailService.DefaultBudget && window.ThumbnailPeakActiveJobs <= 2, "peak cache <=64MiB / decode concurrency <=2");
                Check(memorySamples.Last() - memorySamples[1] <= 64, "no steady private-memory growth after warm-up (short run)");
                window.Hide(); await Task.Delay(200);
                Check(list.Items.Cast<WallpaperCard>().All(c => c.Thumbnail == null) && preview.PreviewProcessId == 0, "tray releases card thumbnail references and preview");
                window.Show(); await VisibleReady();
                Check(preview.ImageSource != null, "show restores viewport and preview");
                var revision = store.Revision; await engine.SaveCheckpointAsync();
                Check(store.Revision == revision, "clean idle checkpoint performs no disk save");
                var heartbeat = 0; var beat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) }; beat.Tick += (_, _) => heartbeat++; beat.Start();
                var checkpoint = Stopwatch.StartNew(); var checkpointTask = engine.SaveCheckpointAsync(true); var snapshotMs = checkpoint.Elapsed.TotalMilliseconds;
                Check(await checkpointTask, "background checkpoint committed"); beat.Stop();
                var checkpointMs = checkpoint.Elapsed.TotalMilliseconds;
                Check(store.Load().Items.Count == count, "checkpoint preserves every library record");
                if (checkpointMs >= 20) Check(heartbeat > 0, "UI dispatches during checkpoint serialization/I-O");
                await Task.Delay(1500); process.Refresh(); var cpuStart = process.TotalProcessorTime; var idleClock = Stopwatch.StartNew();
                await Task.Delay(4000); process.Refresh();
                var idleCpu = (process.TotalProcessorTime - cpuStart).TotalSeconds / idleClock.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
                Check(idleCpu <= .5, "idle CPU <=0.5% total cores (" + idleCpu.ToString("0.000") + "%)");
                Check(!state.Settings.WasRunning, "benchmark does not apply desktop wallpaper");
                if (count == 1000) window.SaveRender(Path.ChangeExtension(output, ".png"));
                var result = new { Items = count, WindowLoadedMs = loadedMs, FirstViewportUsableMs = usableMs,
                    MainPrivateMiB = initialPrivateMiB, InitialResidentThumbnails = initialResident, LibraryContainers = Containers(list), FilmstripContainers = Containers(strip),
                    SearchApplyAndLayoutMs = searchApply, SearchInputToLayoutMs = searchEndToEnd, SearchApplyP95Ms = applyP95, SearchEndToEndP95Ms = endP95,
                    CacheBytes = window.ThumbnailCacheBytes, PeakCacheBytes = window.ThumbnailPeakCacheBytes, PeakActiveJobs = window.ThumbnailPeakActiveJobs,
                    ResidentSamples = residentSamples, PrivateMiBSamples = memorySamples, SnapshotCaptureMs = snapshotMs, BackgroundCheckpointMs = checkpointMs,
                    CheckpointUIHeartbeats = heartbeat, IdleCpuPercentAllCores = idleCpu, TestDirectory = directory, Failures = failures,
                    ThumbnailDiskCacheWarmAtStart = diskWarm,
                    Note = "WPF Release 1280x850; distinct local JPEG copies, OS file cache warm, thumbnail RAM cache cold; disk warmth recorded separately; 32 queries; 3 rounds x24 selections; short memory plateau only, not 8/24h soak; no personal wallpaper." };
                File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("STAGE2 RESULT " + output);
            }
            catch (Exception ex) { failures.Add(ex.ToString()); Console.WriteLine("FAIL Stage2 " + ex); }
            finally { window.Exit(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        };
        window.Show(); Dispatcher.Run(); return failures.Count == 0 ? 0 : 1;
    }
}