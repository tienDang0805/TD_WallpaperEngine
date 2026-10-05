using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
namespace TienDang.App;
public partial class UrlImportWindow : Window
{
    private readonly Action<UrlMediaInfo, string, int>? _enqueue;
    private readonly StateStore _store;
    private readonly Func<DownloadedMedia, Task<WallpaperItem>> _register;
    private readonly IReadOnlyList<WallpaperItem> _items;
    private readonly WallpaperDownloader _downloader = new();
    private CancellationTokenSource? _active;
    private UrlMediaInfo? _info;
    private YouTubeMediaInfo? _youtubeInfo;
    private YouTubeDownloader _youtubeDownloader;
    private readonly bool _customDownloader;
    private DownloadedMedia? _finished;
    private WallpaperItem? _completed;
    private string _directory = "";
    private bool _busy, _closed, _settingFolder;
    private int _generation;
    internal string Phase { get; private set; } = "input";
    internal WallpaperItem? CompletedItem => _completed;
    public UrlImportWindow(StateStore store, string? initial, Func<DownloadedMedia, Task<WallpaperItem>> register, IReadOnlyList<WallpaperItem> items, YouTubeDownloader? youtubeDownloader = null, Action<UrlMediaInfo, string, int>? enqueue = null)
    {
        _customDownloader = youtubeDownloader != null; _enqueue = enqueue; _store = store; _register = register; _items = items;
        var tools = System.IO.Path.Combine(AppContext.BaseDirectory, "DownloadTools");
        _youtubeDownloader = youtubeDownloader ?? new YouTubeDownloader(new(
            System.IO.Path.Combine(tools, "yt-dlp.exe"), System.IO.Path.Combine(tools, "ffmpeg.exe"),
            System.IO.Path.Combine(tools, "deno.exe")));
        InitializeComponent();
        Height = Math.Min(650, SystemParameters.WorkArea.Height - 60);
        VideoPreview.CompactControls = true;
        _directory = System.IO.Path.Combine(store.DirectoryPath, "media");
        FolderPath.Text = _directory;
        UrlBox.Text = initial ?? "";
        VideoPreview.MetadataChanged += UpdateMetadata;
        VideoPreview.StateChanged += text => { if (text.StartsWith(L.Text("Không thể"))) StatusText.Text = L.Text("Chưa xem trước được từ nguồn. Vẫn có thể tải để kiểm tra file."); };
    }
    private static string Bytes(long n) => n >= 1048576 ? (n / 1048576d).ToString("0.0") + " MB" : (n / 1024d).ToString("0.0") + " KB";
    private void UrlEdited(object sender, TextChangedEventArgs e)
    {
        if (PrimaryButton == null || _busy) return;
        _generation++; _active?.Cancel(); _info = null; _youtubeInfo = null; _completed = null; _finished = null;
        Phase = "input"; PrimaryButton.Content = L.Text("Kiểm tra link");
        VideoPreview.Suspend(); VideoPreview.Visibility = ImagePreview.Visibility = Visibility.Collapsed; PreviewPlaceholder.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = ProgressPanel.Visibility = Visibility.Collapsed;
        FileNameText.Text = L.Text("Wallpaper mới từ một đường link"); FileMetaText.Text = L.Text("Kiểm tra link để xem thông tin trước khi tải.");
    }
    private void SetBusy(bool busy, string phase)
    {
        _busy = busy; Phase = phase;
        UrlBox.IsEnabled = PrimaryButton.IsEnabled = UseDefaultFolder.IsEnabled = !busy;
        CancelButton.Content = phase == "downloading" ? L.Text("Dừng tải") : _completed != null ? L.Text("Đóng") : L.Text("Hủy");
        PrimaryButton.Content = busy ? phase == "analyzing" ? L.Text("Đang kiểm tra…") : L.Text("Đang tải…") : _completed != null ? "Xem ngay" : _info != null ? L.Text("Tải & thêm") : L.Text("Kiểm tra link");
        StepText.Foreground = new SolidColorBrush(Color.FromRgb(49,107,179));
        StepText.Text = phase switch { "analyzing" => L.Text("1  Dán link    ›    2  Đang kiểm tra    ›    3  Tải xuống    ›    4  Hoàn tất"), "downloading" => L.Text("1  Dán link    ›    2  Đã kiểm tra    ›    3  Đang tải    ›    4  Hoàn tất"), "complete" => L.Text("1  Dán link    ›    2  Đã kiểm tra    ›    3  Đã tải    ›    4  Hoàn tất ✓"), _ => L.Text("1  Dán link    ›    2  Kiểm tra    ›    3  Tải xuống    ›    4  Hoàn tất") };
    }
    private async void PrimaryClicked(object sender, RoutedEventArgs e)
    {
        if (_completed != null) { try { if (_finished == null && _info != null) _completed = await _register(new DownloadedMedia(_completed.Path, _info.Url, _info.FileName)); DialogResult = true; } catch (Exception ex) { Error(ex.Message); } return; }
        if (_info == null) await AnalyzeAsync(); else await DownloadAsync();
    }
    internal async Task AnalyzeAsync()
    {
        if (_busy) return;
        _active?.Dispose(); _active = new();
        var token = _active.Token; var generation = _generation;
        ErrorPanel.Visibility = Visibility.Collapsed; SetBusy(true, "analyzing");
        StatusText.Text = L.Text("Đang kiểm tra link và thông tin file…");
        try
        {
            var source = WallpaperDownloader.ParseUrl(UrlBox.Text);
            if (YouTubeDownloader.IsYouTube(source))
            {
                StatusText.Text = L.Text("Đang lấy thông tin YouTube bằng yt-dlp…");
                if (!_customDownloader) _youtubeDownloader = new YouTubeDownloader(await Task.Run(() => ToolsetManager.Resolve(_store.DirectoryPath), token));
                _youtubeInfo = await _youtubeDownloader.InspectAsync(source, token);
                _info = _youtubeInfo.Media;
            }
            else { _youtubeInfo = null; _info = await _downloader.InspectAsync(UrlBox.Text, token); }
            if (_closed || generation != _generation) return;
            var existing = _items.FirstOrDefault(i => i.SourceUrl == _info.Url.AbsoluteUri && i.Exists);
            FileNameText.Text = System.IO.Path.GetFileNameWithoutExtension(_info.FileName);
            FileMetaText.Text = (_info.IsVideo ? "VIDEO" : L.Text("ẢNH")) + "  ·  " + System.IO.Path.GetExtension(_info.FileName).TrimStart('.').ToUpperInvariant() +
                "  ·  " + (_info.Length is { } bytes ? Bytes(bytes) : L.Text("Nguồn chưa cho biết dung lượng"));
            if (existing != null) { _completed = existing; StatusText.Text = L.Text("Wallpaper này đã có trong thư viện."); SetBusy(false, "complete"); return; }
            SetBusy(false, "ready"); StatusText.Text = L.Text("Sẵn sàng tải và thêm vào thư viện nhé!");
            if (_youtubeInfo != null)
            {
                FileNameText.Text = _youtubeInfo.Title;
                FileMetaText.Text = "YouTube  ·  MP4" +
                    (_youtubeInfo.Width is { } width && _youtubeInfo.Height is { } height ? L.Text($"  ·  {width} × {height}") : "") +
                    (_youtubeInfo.Duration is { } duration ? "  ·  " + TimeSpan.FromSeconds(duration).ToString(@"hh\:mm\:ss") : "") +
                    L.Text("  ·  dung lượng tùy luồng tải");
                StatusText.Text = L.Text("Tải video YouTube về máy; ffmpeg sẽ ghép/remux MP4 khi cần.");
                if (_youtubeInfo.Thumbnail is { } thumbnail) _ = LoadImagePreviewAsync(thumbnail, generation, token);
            }
            else if (_info.IsVideo)
            {
                VideoPreview.Visibility = Visibility.Visible; PreviewPlaceholder.Visibility = Visibility.Collapsed;
                await VideoPreview.ShowItemAsync(new WallpaperItem { Path = _info.Url.AbsoluteUri, Name = _info.FileName, IsVideo = true }, true);
            }
            else if (_info.Length is null or <= 10_485_760) _ = LoadImagePreviewAsync(_info.Url, generation, token);
        }
        catch (OperationCanceledException) { if (!_closed && generation == _generation) { SetBusy(false, "input"); StatusText.Text = token.IsCancellationRequested ? L.Text("Đã dừng kiểm tra. Chưa thêm file nào.") : L.Text("Nguồn phản hồi quá lâu. Thử lại hoặc dùng link khác."); } }
        catch (Exception ex) { if (!_closed && generation == _generation) { SetBusy(false, "input"); Error(ex.Message); } }
    }
    private async Task LoadImagePreviewAsync(Uri uri, int generation, CancellationToken token)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream(); var block = new byte[65536];
            while (true) { var count = await source.ReadAsync(block, token); if (count == 0) break; if (buffer.Length + count > 10_485_760) return; buffer.Write(block, 0, count); }
            if (_closed || generation != _generation || token.IsCancellationRequested) return;
            buffer.Position = 0;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = buffer; image.EndInit(); image.Freeze();
            ImagePreview.Source = image; ImagePreview.Visibility = Visibility.Visible; PreviewPlaceholder.Visibility = Visibility.Collapsed;
            if (_youtubeInfo == null) FileMetaText.Text += L.Text($"  ·  {image.PixelWidth} × {image.PixelHeight}");
        }
        catch { /* Thumbnail availability does not authorize or imply an import. */ }
    }
    private void UpdateMetadata()
    {
        if (_info == null || VideoPreview.MediaWidth == 0) return;
        FileMetaText.Text = "VIDEO  ·  " + System.IO.Path.GetExtension(_info.FileName).TrimStart('.').ToUpperInvariant() +
            L.Text($"  ·  {VideoPreview.MediaWidth} × {VideoPreview.MediaHeight}  ·  ") +
            (VideoPreview.Duration > 0 ? TimeSpan.FromSeconds(VideoPreview.Duration).ToString(@"mm\:ss") + "  ·  " : "") +
            (_info.Length is { } bytes ? Bytes(bytes) : L.Text("Chưa biết dung lượng"));
    }
    internal async Task DownloadAsync()
    {
        if (_busy || _info == null) return;
        if (UseDefaultFolder.IsChecked != true && _directory == System.IO.Path.Combine(_store.DirectoryPath, "media"))
        { ChooseFolder(this, new RoutedEventArgs()); if (UseDefaultFolder.IsChecked != true && _directory == System.IO.Path.Combine(_store.DirectoryPath, "media")) return; }
        if (_enqueue != null) { _enqueue(_info, _directory, (QualityChoice.SelectedItem as ComboBoxItem)?.Tag is string height && int.TryParse(height, out var value) ? value : 0); Close(); return; }
        _active?.Cancel(); _active?.Dispose(); _active = new(); var token = _active.Token; var generation = _generation;
        VideoPreview.Suspend(); SetBusy(true, "downloading"); ErrorPanel.Visibility = Visibility.Collapsed; ProgressPanel.Visibility = Visibility.Visible;
        DownloadProgressBar.IsIndeterminate = true; ProgressPercent.Text = ""; ProgressLabel.Text = L.Text("Đang kết nối…"); StatusText.Text = L.Text("Đang tải wallpaper. Mày có thể dừng bất cứ lúc nào.");
        try
        {
            Action<DownloadProgress> showProgress = p =>
            {
                if (_closed || generation != _generation || token.IsCancellationRequested || Phase != "downloading") return;
                DownloadProgressBar.IsIndeterminate = p.Total == null;
                if (p.Total is { } total) { var percent = Math.Min(99, p.Received * 100d / total); DownloadProgressBar.Value = percent; ProgressPercent.Text = percent.ToString("0") + "%"; }
                else ProgressPercent.Text = "";
                ProgressLabel.Text = _youtubeInfo == null ? L.Text("Đang tải…") : L.Text("Đang tải luồng YouTube (video/âm thanh)…");
                ProgressStats.Text = Bytes(p.Received) + (p.Total is { } size ? " / " + Bytes(size) : L.Text(" · chưa biết tổng dung lượng")) + " · " + Bytes((long)p.BytesPerSecond) + "/s";
            };
            var progress = new Progress<DownloadProgress>(showProgress);
            if (_finished == null)
            {
                if (_youtubeInfo == null) _finished = await _downloader.DownloadAsync(_info, _directory, ValidateMediaAsync, progress, token);
                else
                {
                    var youtubeProgress = new Progress<VideoDownloadProgress>(p =>
                    {
                        if (_closed || generation != _generation || token.IsCancellationRequested || Phase != "downloading") return;
                        if (p.Processing) { DownloadProgressBar.IsIndeterminate = true; ProgressPercent.Text = ""; ProgressLabel.Text = L.Text("Đang ghép/remux và kiểm tra MP4…"); ProgressStats.Text = L.Text("Chờ xử lý xong trước khi thêm vào thư viện."); }
                        else if (p.Download is { } download) showProgress(download);
                    });
                    _finished = await _youtubeDownloader.DownloadAsync(_youtubeInfo, _directory, ValidateMediaAsync, youtubeProgress, token);
                }
            }
            token.ThrowIfCancellationRequested();
            if (_closed || generation != _generation) return;
            _completed = await _register(_finished);
            DownloadProgressBar.IsIndeterminate = false; DownloadProgressBar.Value = 100; ProgressPercent.Text = "100%"; ProgressLabel.Text = L.Text("Đã thêm vào thư viện!");
            ProgressStats.Text = Bytes(new FileInfo(_finished.Path).Length) + L.Text(" · tải xong");
            SetBusy(false, "complete"); StatusText.Text = L.Text("Đã tải và thêm. File được giữ trên máy khi đóng app.");
            if (_completed.IsVideo) { VideoPreview.Visibility = Visibility.Visible; await VideoPreview.ShowItemAsync(_completed); }
            else { ImagePreview.Source = ThumbnailService.LoadImage(_completed.Path, 500); ImagePreview.Visibility = Visibility.Visible; PreviewPlaceholder.Visibility = Visibility.Collapsed; }
        }
        catch (OperationCanceledException) { if (!_closed) { SetBusy(false, "ready"); ProgressPanel.Visibility = Visibility.Collapsed; StatusText.Text = token.IsCancellationRequested ? L.Text("Đã dừng tải. Chưa thêm file nào.") : L.Text("Nguồn ngừng phản hồi. Thử tải lại."); } }
        catch (Exception ex) { if (!_closed) { SetBusy(false, "ready"); Error(ex.Message); } }
    }
    internal static async Task ValidateMediaAsync(string path, CancellationToken token)
    {
        using var service = new ThumbnailService(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TienDang-download-validation"));
        using var registration = token.Register(service.Dispose);
        var thumbnail = await service.GetAsync(new WallpaperItem { Path = path, IsVideo = MediaTypes.IsVideo(path) });
        token.ThrowIfCancellationRequested();
        if (thumbnail == null) throw new InvalidOperationException(L.Text("File tải được nhưng không đọc được ảnh/video. Chưa thêm vào thư viện; thử link khác."));
    }
    private void Error(string text) { ErrorText.Text = L.Text(text); ErrorPanel.Visibility = Visibility.Visible; StatusText.Text = L.Text("Chưa thêm file nào. Mày có thể sửa link hoặc thử lại."); OpenSourceButton.IsEnabled = Uri.TryCreate(UrlBox.Text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"; }
    private void CancelClicked(object sender, RoutedEventArgs e) { if (_busy) { _active?.Cancel(); return; } Close(); }
    private void ChooseFolder(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var picker = new OpenFolderDialog { Title = L.Text("Chọn nơi tải wallpaper"), InitialDirectory = Directory.Exists(_directory) ? _directory : _store.DirectoryPath };
        if (picker.ShowDialog(this) == true) { _directory = picker.FolderName; _settingFolder = true; UseDefaultFolder.IsChecked = false; _settingFolder = false; FolderPath.Text = _directory; }
    }
    private void DefaultFolderChanged(object sender, RoutedEventArgs e)
    {
        if (FolderPath == null || _settingFolder || _store == null) return;
        if (UseDefaultFolder.IsChecked == true) { _directory = System.IO.Path.Combine(_store.DirectoryPath, "media"); FolderPath.Text = _directory; }
    }
    private void OpenSource(object sender, RoutedEventArgs e)
    {
        try { var uri = WallpaperDownloader.ParseUrl(UrlBox.Text); Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Error(ex.Message); }
    }
    protected override void OnClosing(CancelEventArgs e) { _closed = true; _generation++; _active?.Cancel(); VideoPreview.Dispose(); _downloader.Dispose(); base.OnClosing(e); }
}
