using System.Diagnostics;
using System.Text.Json;
using TienDang.Core;

internal static class YouTubeChecks
{
    public static async Task<int> ProcessFixture(string[] args)
    {
        if (args[1] == "echo") { Console.WriteLine(JsonSerializer.Serialize(args.Skip(2).ToArray())); return 0; }
        if (args[1] == "sleep") { await Task.Delay(TimeSpan.FromMinutes(3)); return 0; }
        if (args[1] == "spawn")
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(typeof(YouTubeChecks).Assembly.Location);
            start.ArgumentList.Add("--process-fixture"); start.ArgumentList.Add("sleep");
            using var child = Process.Start(start)!;
            Console.WriteLine($"TREE:{Environment.ProcessId}:{child.Id}");
            await Task.Delay(TimeSpan.FromMinutes(3)); return 0;
        }
        return 2;
    }
    public static async Task Run()
    {
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS YouTube " + name); }
        async Task Reject(Func<Task> work, string name) { var rejected = false; try { await work(); } catch { rejected = true; } Check(rejected, name); }
        var root = Path.Combine(Path.GetTempPath(), "TienDang-youtube-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var id = "jNQXAC9IVRw";
        var canonical = "https://www.youtube.com/watch?v=" + id;
        foreach (var source in new[] { "https://youtu.be/" + id + "?si=abc", canonical + "&list=x", "https://m.youtube.com/shorts/" + id, "https://youtube.com/embed/" + id, "https://youtube.com/live/" + id })
            Check(YouTubeDownloader.CanonicalUrl(new Uri(source)).AbsoluteUri == canonical, "video URL normalized: " + source);
        Check(!YouTubeDownloader.IsYouTube(new Uri("https://youtube.com.evil.test/watch?v=" + id)), "host suffix spoof rejected");
        await Reject(() => Task.FromResult(YouTubeDownloader.CanonicalUrl(new Uri("https://youtube.com/playlist?list=x"))), "playlist-only link rejected");
        await Reject(() => Task.FromResult(YouTubeDownloader.CanonicalUrl(new Uri("https://youtube.com/watch?v=" + id + "%26--exec%3Dcalc"))), "option injection in ID rejected");
        await Reject(() => Task.FromResult(YouTubeDownloader.CanonicalUrl(new Uri("https://user:pass@youtube.com/watch?v=" + id))), "credentials rejected");

        var executable = Path.Combine(root, "tool.exe"); await File.WriteAllTextAsync(executable, "fixture");
        var tools = new DownloadToolPaths(executable, executable, executable);
        var fake = new FixtureRunner(); var downloader = new YouTubeDownloader(tools, fake);
        var info = await downloader.InspectAsync(new Uri(canonical + "&extra=%22%20%26%20calc"), default);
        Check(fake.Arguments.Last() == canonical && fake.Arguments[^2] == "--" && fake.Arguments.Contains("--ignore-config") &&
            fake.Arguments.Contains("--no-plugin-dirs") && fake.Arguments.Contains("--no-playlist"), "config/plugin isolation and one canonical URL argument");
        Check(info.Title == "A / wallpaper" && info.Media.FileName.EndsWith(".mp4") && info.Media.Url.AbsoluteUri == canonical, "metadata creates safe MP4 filename");
        var events = new List<VideoDownloadProgress>();
        var validated = false;
        var done = await downloader.DownloadAsync(info, root, (path, _) =>
        {
            validated = File.Exists(path) && path.Contains(".tiendang-youtube-") && !File.Exists(Path.Combine(root, "YouTube - " + info.Media.FileName));
            return Task.CompletedTask;
        }, new InlineProgress(events.Add), default);
        Check(validated && File.Exists(done.Path) && !Directory.GetDirectories(root).Any(), "staged file validated before publishing and staging cleaned");
        Check(events.Any(e => e.Download?.Received == 4096 && e.Download.Total == 8192) && events.Any(e => e.Processing), "byte progress and merge phase parsed");
        var again = await downloader.DownloadAsync(info, root, (_, _) => Task.CompletedTask, null, default);
        Check(again.Path != done.Path && File.ReadAllText(done.Path) == "fixture-video", "collision preserves existing file");
        var baseline = Directory.GetFiles(root).Order().ToArray();
        await Reject(() => downloader.DownloadAsync(info, root, (_, _) => throw new IOException("corrupt"), null, default), "invalid media rejected");
        Check(Directory.GetFiles(root).Order().SequenceEqual(baseline) && !Directory.GetDirectories(root).Any(), "failed validation leaves no file or staging");
        fake.Fail = true; await Reject(() => downloader.DownloadAsync(info, root, (_, _) => Task.CompletedTask, null, default), "process failure rejected"); fake.Fail = false;
        Check(!Directory.GetDirectories(root).Any(), "failed process partial cleanup");
        using var cancel = new CancellationTokenSource(); fake.Cancel = cancel;
        await Reject(() => downloader.DownloadAsync(info, root, (_, _) => Task.CompletedTask, null, cancel.Token), "cancel while downloading");
        fake.Cancel = null; Check(!Directory.GetDirectories(root).Any(), "cancel cleans owned partials");
        fake.EscapedPath = done.Path;
        await Reject(() => downloader.DownloadAsync(info, root, (_, _) => Task.CompletedTask, null, default), "output outside owned directory rejected");
        Check(File.Exists(done.Path), "output rejection preserves unrelated file");
        Check(DownloadToolRunner.FriendlyError("Sign in to confirm you're not a bot").Contains("xác minh"), "YouTube verification error explained");

        var runner = new DownloadToolRunner(); string? echoed = null;
        var hostile = "https://example.test/a?x=\" & calc.exe $(Get-Process) " + (char)96 + "literal" + (char)96;
        await runner.RunAsync(Environment.ProcessPath!, [typeof(YouTubeChecks).Assembly.Location, "--process-fixture", "echo", hostile],
            root, s => echoed = s, default);
        Check(JsonSerializer.Deserialize<string[]>(echoed!)!.Single() == hostile, "real process receives shell metacharacters as one literal argument");
        using var treeCancellation = new CancellationTokenSource(); var parentId = 0; var childId = 0;
        await Reject(() => runner.RunAsync(Environment.ProcessPath!,
            [typeof(YouTubeChecks).Assembly.Location, "--process-fixture", "spawn"], root, line =>
            {
                if (line.StartsWith("TREE:")) { var parts = line.Split(':'); parentId = int.Parse(parts[1]); childId = int.Parse(parts[2]); treeCancellation.Cancel(); }
            }, treeCancellation.Token), "real process cancellation");
        bool Alive(int pid) { try { return !Process.GetProcessById(pid).HasExited; } catch { return false; } }
        for (var i = 0; i < 20 && (Alive(parentId) || Alive(childId)); i++) await Task.Delay(50);
        Check(parentId > 0 && childId > 0 && !Alive(parentId) && !Alive(childId), "cancellation kills parent and merge child");
    }
    private sealed class InlineProgress(Action<VideoDownloadProgress> action) : IProgress<VideoDownloadProgress> { public void Report(VideoDownloadProgress value) => action(value); }
    private sealed class FixtureRunner : IDownloadToolRunner
    {
        public IReadOnlyList<string> Arguments = [];
        public bool Fail; public CancellationTokenSource? Cancel; public string? EscapedPath;
        public async Task RunAsync(string executable, IReadOnlyList<string> args, string cwd, Action<string> output, CancellationToken token)
        {
            Arguments = args;
            if (args.Contains("--dump-single-json")) { output("""{"title":"A / wallpaper","width":1920,"height":1080,"duration":19,"is_live":false}"""); return; }
            await File.WriteAllTextAsync(Path.Combine(cwd, "video.part"), "partial", token);
            if (Fail) throw new IOException("download error");
            if (Cancel != null) { Cancel.Cancel(); token.ThrowIfCancellationRequested(); }
            output("""TD_PROGRESS:{"downloaded_bytes":4096,"total_bytes":8192,"speed":512}""");
            output("TD_PROCESS:started");
            var path = EscapedPath ?? Path.Combine(cwd, "video.mp4");
            if (EscapedPath == null) await File.WriteAllTextAsync(path, "fixture-video", token);
            output("TD_FILE:" + JsonSerializer.Serialize(path));
        }
    }
}