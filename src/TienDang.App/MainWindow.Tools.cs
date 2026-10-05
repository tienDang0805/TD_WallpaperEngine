using System.Text.RegularExpressions;
namespace TienDang.App;
public partial class MainWindow
{
    private DownloadQueue _downloads = null!;
    private DownloadQueueWindow? _queueWindow;
    internal LibraryState ToolsState => _state;
    internal StateStore ToolsStore => _store;
    internal DownloadQueue Downloads => _downloads;
    private void OpenLibraryTools(object sender, RoutedEventArgs e) => new LibraryToolsWindow(this) { Owner = this }.ShowDialog();
    private void OpenDownloads(object sender, RoutedEventArgs e) => ShowDownloads();
    private void ShowDownloads()
    {
        if (_queueWindow != null) { _queueWindow.Show(); _queueWindow.Activate(); return; }
        _queueWindow = new(_downloads) { Owner = this }; _queueWindow.Closed += (_, _) => _queueWindow = null; _queueWindow.Show();
    }
    internal bool CommitRelinks(IReadOnlyList<RelinkProposal> proposals)
    {
        var changes = proposals.Select(p => (Proposal: p, Item: _state.Items.Find(i => i.Id == p.Id))).ToArray();
        var changedIds = proposals.Select(p => p.Id).ToHashSet();
        var occupied = _state.Items.Where(i => !changedIds.Contains(i.Id)).Select(i => Path.GetFullPath(i.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (changedIds.Count != proposals.Count || changes.Any(p => p.Item == null || p.Item.Path != p.Proposal.OldPath ||
            !File.Exists(p.Proposal.NewPath) || !MediaTypes.Supported(p.Proposal.NewPath) || MediaTypes.IsVideo(p.Proposal.NewPath) != p.Item.IsVideo ||
            !occupied.Add(Path.GetFullPath(p.Proposal.NewPath))))
            throw new IOException(L.Text("Thư viện đã thay đổi. Quét lại trước khi áp dụng."));
        if (!CommitMutation(() => { foreach (var entry in changes) entry.Item!.Path = entry.Proposal.NewPath; },
            () => { foreach (var entry in changes) entry.Item!.Path = entry.Proposal.OldPath; }, true)) return false;
        InlinePreview.Suspend(); _cards.Clear(); RefreshItems(); return true;
    }
    internal bool RestoreLibrary(LibraryState restored)
    {
        var previous = _state.CreateSnapshot(); InlinePreview.Suspend(); _engine.Stop();
        void Assign(LibraryState value) { _state.Items = value.Items; _state.Playlists = value.Playlists; _state.Settings = value.Settings; }
        if (!CommitMutation(() => Assign(restored), () => Assign(previous), true)) return false;
        L.SetLanguage(_state.Settings.Language); LoadSettings(); _cards.Clear(); RefreshPlaylists(); RefreshLanguage(); return true;
    }
    internal async Task ExportDiagnosticsAsync(string target)
    {
        var text = await CaptureDiagnosticTextAsync();
        foreach (var name in _state.Items.Select(i => i.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderByDescending(n => n.Length)) text = text.Replace(name, "[wallpaper]");
        text = Regex.Replace(text, @"https?://\S+", "[URL removed]");
        var tools = await Task.Run(() => ToolsetManager.Resolve(_store.DirectoryPath));
        var versions = new List<string>();
        foreach (var path in new[] { tools.YtDlp, tools.Deno, tools.Ffmpeg })
        {
            var output = ""; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await new DownloadToolRunner().RunAsync(path, [Path.GetFileName(path) == "ffmpeg.exe" ? "-version" : "--version"], Path.GetDirectoryName(path)!, line => { if (output.Length < 500) output += line + "\n"; }, timeout.Token); }
            catch (Exception error) { output = "Unavailable: " + error.GetType().Name; }
            versions.Add(Path.GetFileName(path) + ": " + output);
        }
        await File.WriteAllTextAsync(target, "TD-WallpaperEngine 1.0.1\n" + DateTime.UtcNow.ToString("O") + "\n" + string.Join("\n", versions) + "\n" + text);
    }
    internal async Task AddStarterAsync()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "StarterPack"); if (!Directory.Exists(root)) return;
        var added = new List<WallpaperItem>();
        foreach (var file in Directory.EnumerateFiles(root).Where(MediaTypes.Supported))
        {
            var (item, fresh) = await ImportLocalFileAsync(file, true);
            if (fresh) { item.Name = Path.GetFileName(file) switch { "galaxy-eye.mp4" => "Galaxy Eye Anime Girl", "water-fantasy.mp4" => "Water Fantasy · 水中的美女", "forest-valley.jpg" => "Forest Valley", _ => "Fortress City" }; added.Add(item); }
        }
        if (added.Count == 0) return;
        if (!CommitMutation(() => _state.Items.AddRange(added), () => { foreach (var item in added) _state.Items.Remove(item); }, true)) return;
        RefreshPlaylists(); RefreshItems();
    }
}