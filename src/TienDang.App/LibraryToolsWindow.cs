using Microsoft.Win32;
namespace TienDang.App;
internal sealed class LibraryToolsWindow : Window
{
    private readonly MainWindow _main;
    private readonly TabControl _tabs = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly ListBox _health = new(), _storage = new();
    private readonly ListBox _proposals = new();
    private IReadOnlyList<RelinkProposal> _relinks = [];
    private string? _quarantine;
    private CancellationTokenSource? _active;
    private bool _busy;
    public LibraryToolsWindow(MainWindow main)
    {
        _main = main; Style = (Style)Application.Current.FindResource(typeof(Window)); ShowInTaskbar = false; Title = L.Text("Tiện ích thư viện") + " · TD-WallpaperEngine";
        Width = Math.Min(850, SystemParameters.WorkArea.Width); Height = Math.Min(650, SystemParameters.WorkArea.Height); MinWidth = 620; MinHeight = 430; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        var footer = new DockPanel(); var cancel = new Button { Content = L.Text("Dừng tác vụ") }; cancel.Click += (_, _) => _active?.Cancel();
        DockPanel.SetDock(cancel, Dock.Right); footer.Children.Add(cancel); footer.Children.Add(_status); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var heading = new TextBlock { Text = L.Text("Tiện ích thư viện"), FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        root.Children.Add(_tabs); Content = root;
        var health = Tab("Kiểm tra thư viện"); Note(health, "Quét file thiếu và dung lượng. Tìm lại theo tên file, bỏ qua tên trùng; ID, bộ, yêu thích và thời gian riêng được giữ.");
        Button(health, "Quét thư viện", async token => { var snapshot = main.ToolsState.CreateSnapshot(); var rows = await Task.Run(() => LibraryUtilities.Scan(snapshot), token); _health.ItemsSource = rows.Select(r => (r.Missing ? L.Text("Thiếu file") : L.Text("File tồn tại")) + " · " + r.Name + " · " + (r.Bytes / 1048576d).ToString("0.0") + " MB"); _status.Text = L.Text("File thiếu") + ": " + rows.Count(r => r.Missing); });
        _health.ItemsSource = new[] { L.Text("Bấm Quét thư viện để xem file thiếu.") }; _health.Height = 130; health.Children.Add(_health);
        Button(health, "Kiểm tra ảnh và video", async token =>
        {
            var rows = new List<string>();
            foreach (var item in main.ToolsState.CreateSnapshot().Items)
            {
                token.ThrowIfCancellationRequested();
                try { await UrlImportWindow.ValidateMediaAsync(item.Path, token); rows.Add(item.Name + " · OK"); }
                catch (OperationCanceledException) { throw; }
                catch { rows.Add(item.Name + " · " + L.Text("Không đọc được file")); }
                _health.ItemsSource = rows.ToArray();
            }
        });
        Button(health, "Tìm lại trong thư mục", async token =>
        {
            var picker = new OpenFolderDialog { Title = L.Text("Chọn thư mục wallpaper") }; if (picker.ShowDialog(this) != true) return;
            var snapshot = main.ToolsState.CreateSnapshot(); _relinks = await Task.Run(() => LibraryUtilities.FindRelinks(snapshot, picker.FolderName), token);
            _proposals.ItemsSource = _relinks.Select(r => Path.GetFileName(r.OldPath) + " → " + r.NewPath); _status.Text = L.Text("Đề xuất tìm lại") + ": " + _relinks.Count;
        });
        _proposals.ItemsSource = new[] { L.Text("Các đường dẫn tìm lại sẽ hiện tại đây để bạn kiểm tra.") }; _proposals.Height = 120; health.Children.Add(_proposals);
        Button(health, "Áp dụng đường dẫn đã xem", _ => { if (_relinks.Count == 0) return Task.CompletedTask; if (!main.CommitRelinks(_relinks)) throw new IOException(L.Text("Chưa lưu được thay đổi.")); _status.Text = L.Text("Đã tìm lại file. Bộ sưu tập, yêu thích và thời gian riêng được giữ."); _relinks = []; _proposals.ItemsSource = null; return Task.CompletedTask; });

        var storage = Tab("Dung lượng"); Note(storage, "Chỉ chuyển file trong media của app không còn được thư viện dùng vào khu vực khôi phục. Không xóa file gốc ngoài thư viện; không dọn khi đang tải.");
        Button(storage, "Xem dung lượng và file không dùng", async token =>
        {
            var snapshot = main.ToolsState.CreateSnapshot(); var data = main.ToolsStore.DirectoryPath;
            var unused = await Task.Run(() => LibraryUtilities.UnreferencedMedia(snapshot, data), token);
            var healthRows = await Task.Run(() => LibraryUtilities.Scan(snapshot), token);
            _storage.ItemsSource = unused.Select(p => Path.GetFileName(p) + " · " + (new FileInfo(p).Length / 1048576d).ToString("0.0") + " MB");
            var cacheRoot = Path.Combine(data, "thumbnails", "v2");
            var cacheBytes = await Task.Run(() => Directory.Exists(cacheRoot) ? Directory.EnumerateFiles(cacheRoot, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).Sum(p => new FileInfo(p).Length) : 0L, token);
            _status.Text = L.Text("Dung lượng media") + ": " + (healthRows.Sum(r => r.Bytes) / 1048576d).ToString("0.0") + " MB · " + L.Text("Dung lượng thumbnail") + ": " + (cacheBytes / 1048576d).ToString("0.0") + " MB · " + L.Text("File không dùng") + ": " + unused.Length;
        });
        _storage.Height = 180; storage.Children.Add(_storage);
        Button(storage, "Chuyển file không dùng sang khôi phục", async token =>
        {
            IdleRequired(); var paths = LibraryUtilities.UnreferencedMedia(main.ToolsState.CreateSnapshot(), main.ToolsStore.DirectoryPath);
            if (paths.Length == 0) { _status.Text = L.Text("Không có file cần dọn."); return; }
            if (MessageBox.Show(this, L.Text("Chuyển các file không dùng sang khu vực khôi phục?") + "\n" + paths.Length, Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            _quarantine = await Task.Run(() => LibraryUtilities.Quarantine(main.ToolsStore.DirectoryPath, paths), token); _status.Text = L.Text("Đã dọn. Có thể khôi phục bằng nút bên dưới.");
        });
        Button(storage, "Khôi phục lần dọn gần nhất", async token =>
        {
            IdleRequired(); var root = Path.Combine(main.ToolsStore.DirectoryPath, "quarantine");
            _quarantine ??= Directory.Exists(root) ? Directory.EnumerateDirectories(root).OrderByDescending(Directory.GetLastWriteTimeUtc).FirstOrDefault() : null;
            if (_quarantine == null) return;
            var count = await Task.Run(() => LibraryUtilities.RestoreQuarantine(main.ToolsStore.DirectoryPath, _quarantine), token); _status.Text = L.Text("Đã khôi phục") + ": " + count;
        });

        var backup = Tab("Sao lưu"); Note(backup, "Sao lưu kèm media dùng được trên máy khác. Bản chỉ có dữ liệu giữ đường dẫn gốc. File thiếu không được sao chép. Khôi phục thay nội dung thư viện sau khi bạn xác nhận; media hiện có vẫn giữ trên máy.");
        Button(backup, "Sao lưu kèm ảnh và video", token => Export(true, token)); Button(backup, "Sao lưu dữ liệu", token => Export(false, token));
        Button(backup, "Khôi phục từ bản sao lưu", async token =>
        {
            IdleRequired(); var picker = new OpenFileDialog { Filter = "TD backup|*.tdbackup;*.zip" }; if (picker.ShowDialog(this) != true) return;
            if (MessageBox.Show(this, L.Text("Thay nội dung thư viện bằng bản sao lưu?"), Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var restored = await LibraryArchive.ReadAsync(picker.FileName, main.ToolsStore.DirectoryPath, token);
            if (!main.RestoreLibrary(restored)) throw new IOException(L.Text("Chưa lưu được thay đổi.")); _status.Text = L.Text("Đã khôi phục thư viện. Hãy chọn nền để phát.");
        });
        Button(backup, "Thêm lại bộ wallpaper mẫu", async _ => { await main.AddStarterAsync(); _status.Text = L.Text("Đã thêm bộ mẫu."); });

        var support = Tab("Bộ tải và hỗ trợ"); Note(support, "Xuất thông tin bộ phát và phiên bản công cụ để kiểm tra lỗi. Báo cáo không kèm đường dẫn thư viện hoặc link nguồn. Cập nhật yt-dlp/Deno từ release chính thức, kiểm tra SHA256 và phiên bản trước khi đổi; ffmpeg giữ bản đã đóng gói.");
        Button(support, "Xuất báo cáo chẩn đoán", async _ => { var picker = new SaveFileDialog { Filter = "Text report|*.txt", FileName = "TD-WallpaperEngine-diagnostics.txt" }; if (picker.ShowDialog(this) == true) { await main.ExportDiagnosticsAsync(picker.FileName); _status.Text = L.Text("Đã xuất báo cáo."); } });
        Button(support, "Cập nhật bộ tải đã xác minh", async token => { IdleRequired(); await Task.Run(() => ToolsetManager.UpdateAsync(main.ToolsStore.DirectoryPath, new Progress<string>(message => Dispatcher.BeginInvoke(() => _status.Text = message)), token), token); _status.Text = L.Text("Đã cập nhật bộ tải."); });
        Button(support, "Quay lại bộ tải trước", _ => { IdleRequired(); ToolsetManager.Rollback(main.ToolsStore.DirectoryPath); _status.Text = L.Text("Đã quay lại bộ tải trước."); return Task.CompletedTask; });
        Button(support, "Nguồn và giấy phép bộ mẫu", _ => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "StarterPack", "CREDITS.md")) { UseShellExecute = true }); return Task.CompletedTask; });
        Closing += (_, e) => { if (_busy) { _active?.Cancel(); e.Cancel = true; _status.Text = L.Text("Đang dừng tác vụ…"); } };
    }
    private StackPanel Tab(string title)
    {
        var panel = new StackPanel { Margin = new Thickness(12) }; _tabs.Items.Add(new TabItem { Header = L.Text(title), Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel;
    }
    private static void Note(Panel parent, string text) => parent.Children.Add(new TextBlock { Text = L.Text(text), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 14) });
    private void Button(Panel parent, string text, Func<CancellationToken, Task> action)
    {
        var button = new Button { Content = L.Text(text), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 5) };
        System.Windows.Automation.AutomationProperties.SetName(button, L.Text(text));
        button.Click += async (_, _) =>
        {
            if (_busy) return; _busy = true; _tabs.IsEnabled = false; _active = new(); _status.Text = L.Text("Đang xử lý…");
            try { await action(_active.Token); }
            catch (OperationCanceledException) { _status.Text = L.Text("Đã hủy"); }
            catch (Exception error) { _status.Text = L.Text(error.Message); }
            finally { _active.Dispose(); _active = null; _tabs.IsEnabled = true; _busy = false; }
        };
        parent.Children.Add(button);
    }
    private void IdleRequired() { if (_main.Downloads.Busy) throw new IOException(L.Text("Chờ tải xong hoặc hủy hàng đợi trước khi làm thao tác này.")); }
    private async Task Export(bool media, CancellationToken token)
    {
        var picker = new SaveFileDialog { Filter = "TD backup|*.tdbackup", FileName = "TD-WallpaperEngine-backup.tdbackup" }; if (picker.ShowDialog(this) != true) return;
        var snapshot = _main.ToolsState.CreateSnapshot(); await Task.Run(() => LibraryArchive.ExportAsync(snapshot, picker.FileName, media, token), token); _status.Text = L.Text("Đã sao lưu.");
    }
}