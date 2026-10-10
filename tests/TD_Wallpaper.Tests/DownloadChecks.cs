using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using TD_Wallpaper.Core;

internal static class DownloadChecks
{
    public static async Task Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS Download " + message); }
        async Task Reject(Func<Task> work, string message) { try { await work(); throw new Exception("accepted: " + message); } catch (Exception ex) when (ex.Message != "accepted: " + message) { Console.WriteLine("PASS Download " + message); } }
        var root = Path.Combine(Path.GetTempPath(), "TD_Wallpaper-download-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var payload = Enumerable.Range(0, 900000).Select(i => (byte)(i % 251)).ToArray();
        using var downloader = new WallpaperDownloader(new Handler(payload));
        var info = await downloader.InspectAsync("https://fixture.test/wallpaper.mp4#preview", default);
        Check(info.Url.Fragment == "" && info.IsVideo && info.Length == payload.Length, "HEAD metadata and canonical source");
        var validated = false;
        var finished = await downloader.DownloadAsync(info, root, async (file, ct) => { Check(File.Exists(file) && Directory.GetFiles(root).Length == 1, "uncommitted download only exists as temporary file"); Check((await File.ReadAllBytesAsync(file, ct)).SequenceEqual(payload), "streaming bytes match source"); validated = true; }, null, default);
        Check(validated && File.Exists(finished.Path) && !Directory.GetFiles(root).Any(f => f.Contains(".part")), "validate before publish and no leftover partial");
        var collision = await downloader.DownloadAsync(info, root, (_, _) => Task.CompletedTask, null, default);
        Check(collision.Path != finished.Path && (await File.ReadAllBytesAsync(finished.Path)).SequenceEqual(payload), "existing file never overwritten");
        var before = Directory.GetFiles(root).Order().ToArray();
        await Reject(() => downloader.DownloadAsync(info, root, (_, _) => throw new InvalidOperationException("bad media"), null, default), "validation failure does not publish");
        Check(Directory.GetFiles(root).Order().SequenceEqual(before), "failed media leaves no partial or extra file");
        using var cancellation = new CancellationTokenSource();
        await Reject(() => downloader.DownloadAsync(info, root, (_, _) => Task.CompletedTask, new InlineProgress<DownloadProgress>(p => { if (p.Received >= 131072) cancellation.Cancel(); }), cancellation.Token), "midstream cancellation");
        Check(Directory.GetFiles(root).Order().SequenceEqual(before), "cancel keeps originals and removes partial file");
        await Reject(() => downloader.InspectAsync("javascript:alert(1)", default), "non HTTP URL rejected");
        await Reject(() => downloader.InspectAsync("https://username:password@fixture.test/a.mp4", default), "URL credentials rejected");
        await Reject(() => downloader.InspectAsync("https://fixture.test/page.mp4", default), "HTML page cannot masquerade as video");
        await Reject(() => downloader.InspectAsync("https://fixture.test/missing.mp4", default), "HTTP error not imported");
        var changed = await downloader.InspectAsync("https://fixture.test/changed.mp4", default);
        await Reject(() => downloader.DownloadAsync(changed, root, (_, _) => Task.CompletedTask, null, default), "changed GET type rejected");
        var shortFile = await downloader.InspectAsync("https://fixture.test/short.mp4", default);
        await Reject(() => downloader.DownloadAsync(shortFile, root, (_, _) => Task.CompletedTask, null, default), "truncated content length rejected");
        var unknown = await downloader.InspectAsync("https://fixture.test/unknown.webm", default);
        var progress = new List<DownloadProgress>();
        await downloader.DownloadAsync(unknown, root, (_, _) => Task.CompletedTask, new InlineProgress<DownloadProgress>(p => progress.Add(p)), default);
        Check(progress.Count > 0 && progress.All(p => p.Total == null), "unknown length reported honestly");
        var named = await downloader.InspectAsync("https://fixture.test/named", default);
        Check(named.FileName == "CON-..-wallpaper.mp4" && named.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0, "untrusted filename sanitized inside target directory");
        var method = await downloader.InspectAsync("https://fixture.test/no-head.webm", default);
        Check(method.IsVideo && method.Length == null, "HEAD not supported still accepts explicit media extension");
        Check(!Directory.GetFiles(root).Any(f => f.Contains(".part")), "all errors cleaned only owned partial files");
    }
    private sealed class InlineProgress<T>(Action<T> update) : IProgress<T> { public void Report(T value) => update(value); }
    private sealed class Handler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; var head = request.Method == HttpMethod.Head;
            var response = new HttpResponseMessage(path.Contains("missing") ? HttpStatusCode.NotFound : head && path.Contains("no-head") ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.OK);
            response.Content = new ByteArrayContent(head ? [] : payload);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(path.Contains("page") || path.Contains("changed") && !head ? "text/html" : path.Contains("webm") ? "video/webm" : "video/mp4");
            if (!path.Contains("unknown")) response.Content.Headers.ContentLength = path.Contains("short") && !head ? payload.Length + 1 : payload.Length;
            else response.Content = new StreamContent(new UnknownLengthStream(head ? [] : payload)) { Headers = { ContentType = new MediaTypeHeaderValue("video/webm") } };
            if (path.Contains("named")) response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "\"CON/../wallpaper.mp4\"" };
            return Task.FromResult(response);
        }
    }
    private sealed class UnknownLengthStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b, int o, int c) => _inner.Read(b,o,c);
        public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default) => _inner.ReadAsync(b,ct);
        public override void Flush() => throw new NotSupportedException(); public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b,int o,int c) => throw new NotSupportedException();
    }
}
