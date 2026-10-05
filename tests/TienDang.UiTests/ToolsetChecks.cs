using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TienDang.Core;
internal static class ToolsetChecks
{
    private sealed class Handler(string root, bool badHash = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var yt = request.RequestUri!.AbsoluteUri.Contains("yt-dlp", StringComparison.Ordinal);
            var path = yt ? Path.Combine(root, "src", "TienDang.App", "DownloadTools", "yt-dlp.exe") : Path.Combine(root, "artifacts", "download-tools", "deno-2.9.7.zip");
            HttpContent content;
            if (request.RequestUri.Host == "api.github.com")
            {
                using var input = File.OpenRead(path); var hash = Convert.ToHexString(SHA256.HashData(input));
                var asset = yt ? "yt-dlp.exe" : "deno-x86_64-pc-windows-msvc.zip";
                var repo = yt ? "yt-dlp/yt-dlp" : "denoland/deno";
                content = new StringContent(JsonSerializer.Serialize(new { assets = new[] { new { name = asset, digest = "sha256:" + (badHash ? new string('0', 64) : hash), browser_download_url = "https://github.com/" + repo + "/releases/download/test/" + asset } } }), Encoding.UTF8, "application/json");
            }
            else content = new StreamContent(File.OpenRead(path));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
    internal static async Task Run(string root, string data)
    {
        void Require(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS toolset " + name); }
        var bundled = Path.Combine(root, "src", "TienDang.App", "DownloadTools");
        await ToolsetManager.UpdateAsync(data, null, default, new Handler(root));
        var first = ToolsetManager.Resolve(data, bundled);
        Require(first.YtDlp.StartsWith(Path.Combine(data, "toolsets"), StringComparison.OrdinalIgnoreCase) && first.Ffmpeg == Path.Combine(bundled, "ffmpeg.exe"), "verified streamed stage activates only yt-dlp and Deno; ffmpeg stays pinned");
        var pointer = Path.Combine(data, "toolsets", "active.json"); var original = File.ReadAllText(pointer);
        try { await ToolsetManager.UpdateAsync(data, null, default, new Handler(root, true)); throw new Exception("Bad digest accepted"); }
        catch (IOException) { Require(File.ReadAllText(pointer) == original, "hash rejection preserves active tools and pointer"); }
        await ToolsetManager.UpdateAsync(data, null, default, new Handler(root));
        Require(File.ReadAllText(pointer) != original, "a second verified stage is independently versioned");
        ToolsetManager.Rollback(data); Require(File.ReadAllText(pointer) == original, "rollback restores previous pointer");
        await File.AppendAllTextAsync(first.YtDlp, "tamper");
        Require(ToolsetManager.Resolve(data, bundled).YtDlp == Path.Combine(bundled, "yt-dlp.exe"), "tampered active tool falls back to bundled version");
        ToolsetManager.Rollback(data); Require(!File.Exists(pointer), "rollback without previous returns to bundled tools");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { await ToolsetManager.UpdateAsync(data, null, canceled.Token, new Handler(root)); throw new Exception("Update cancellation ignored"); } catch (OperationCanceledException) { Console.WriteLine("PASS toolset canceled stage never activates"); }
        Console.WriteLine("PASS toolset regression suite (streamed fixture release assets, actual --version executables, no live update)");
    }
}