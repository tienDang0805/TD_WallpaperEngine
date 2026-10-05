using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace TienDang.App;

public partial class MainWindow : Window
{
    private readonly LibraryState _state;
    private readonly StateStore _store;
    private readonly WallpaperEngine _engine;
    private readonly Forms.NotifyIcon? _tray;
    private bool _initialized;
    private readonly ThumbnailService _thumbnails;
    private string _mediaFilter = "all";
    private bool _refreshingItems;
    private readonly System.Windows.Threading.DispatcherTimer _searchDebounce = new(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
    private List<WallpaperCard> _viewCards = [];
    internal event Action<double>? ViewRefreshed;
    internal int ViewRefreshCount { get; private set; }
    internal double LastViewRefreshMs { get; private set; }
    internal bool SearchPending => _searchDebounce.IsEnabled;
    private readonly Dictionary<string, WallpaperCard> _cards = [];
    private bool _exiting;
    private bool _actionBusy;
    private nint _powerRegistration;
    private readonly bool _smoke;
    public MainWindow(LibraryState state, StateStore store, bool smoke = false)
    {
        _state = state;
        _store = store;
        _smoke = smoke;
        L.SetLanguage(state.Settings.Language);
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); RefreshItems(); };
        InitializeComponent();
        L.SetPlaylistTemplate(PlaylistList);
        _thumbnails = new(store.DirectoryPath, 48 * 1024 * 1024); // Below the 64 MiB ceiling; reserve room for WIC/visible-frame overhead.
        InlinePreview.StateChanged += text => { PreviewState.Text = text; PreviewState.ToolTip = text; };
        InlinePreview.MetadataChanged += UpdatePreviewMetadata;
        IsVisibleChanged += async (_, _) => { if (!_initialized || _resettingFilters) return; if (!IsVisible) { InlinePreview.Suspend(); ReleaseThumbnails(); } else { QueueThumbnailRefresh(); await UpdatePreview(); } };
        _engine = new(state, store);
        _downloads = new(store, RegisterDownloadedAsync);
        _engine.Changed += UpdatePlayback;
        _engine.Problem += ShowNotice;
        _engine.DisplaysChanged += RefreshDisplays;
        RefreshDisplays();
        LoadSettings();
        RefreshPlaylists(Playlist.AllId);
        _initialized = true;
        RefreshItems();
        if (!smoke)
        {
            _tray = new Forms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
                Text = "TD-WallpaperEngine",
                Visible = true,
                ContextMenuStrip = new Forms.ContextMenuStrip()
            };
            AddTray("Mở thư viện", () => { Show(); WindowState = WindowState.Normal; Activate(); });
            AddTray("Wallpaper tiếp theo", async () => await Run(_engine.NextAll));
            AddTray("Tạm dừng / Tiếp tục", _engine.TogglePause);
            AddTray("Bỏ ghim", _engine.Unpin);
            AddTray("Dừng wallpaper", _engine.Stop);
            _tray.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
            AddTray("Thoát", Exit);
            _tray.DoubleClick += (_, _) => { Show(); WindowState = WindowState.Normal; Activate(); };
        }
        SourceInitialized += (_, _) =>
        {
            if (smoke) return;
            var source = (HwndSource)PresentationSource.FromVisual(this);
            source.AddHook(WindowMessage);
            var guid = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");
            _powerRegistration = NativeDesktop.RegisterPowerSettingNotification(source.Handle, ref guid, 0);
        };
        Loaded += async (_, _) =>
        {
            if (_store.RecoveryNotice != null) ShowNotice(_store.RecoveryNotice);
            if (!smoke)
            {
                try { Startup.RefreshExisting(_store.DirectoryPath); }
                catch (Exception ex) { _store.Log("Startup refresh failed: " + ex.Message); }
                await Run(_engine.Restore);
            }
            UpdatePlayback();
            await UpdatePreview();
        };
    }

    private nint WindowMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x218 && wParam == 0x8013)
        {
            var guid = Marshal.PtrToStructure<Guid>(lParam);
            if (guid == new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47") && Marshal.ReadInt32(lParam, 16) >= 4)
                _engine.DisplayPower(Marshal.ReadInt32(lParam, 20) == 0);
        }
        return 0;
    }

    private void AddTray(string label, Action action)
    {
        var item = _tray!.ContextMenuStrip!.Items.Add(L.Text(label), null, (_, _) => Dispatcher.BeginInvoke(action));
        item.Tag = label;
    }

    private Playlist SelectedPlaylist => PlaylistList.SelectedItem as Playlist ?? _state.Playlists.First(p => p.Id == Playlist.AllId);
    private string TargetId => (MonitorBox.SelectedItem as DisplayInfo)?.Id ?? "*";
    private List<WallpaperItem> SelectedItems => WallpaperList.SelectedItems.Cast<WallpaperCard>().Select(c => c.Item).ToList();

    private void RefreshDisplays()
    {
        var selected = TargetId;
        MonitorBox.ItemsSource = new[] { new DisplayInfo("*", L.Text("Tất cả màn hình"), 0, 0, 0, 0) }.Concat(NativeDesktop.Displays()).ToList();
        MonitorBox.SelectedItem = MonitorBox.Items.Cast<DisplayInfo>().FirstOrDefault(d => d.Id == selected) ?? MonitorBox.Items[0];
    }
    private void RefreshPlaylists(string? id = null)
    {
        id ??= SelectedPlaylist.Id;
        PlaylistList.ItemsSource = null;
        PlaylistList.ItemsSource = _state.Playlists;
        PlaylistList.SelectedItem = _state.Playlists.Find(p => p.Id == id) ?? _state.Playlists[0];
    }
    private void RefreshItems()
    {
        if (!_initialized || _resettingFilters) return;
        _searchDebounce.Stop();
        var refreshWatch = Stopwatch.StartNew();
        var liveIds = _state.Items.Select(i => i.Id).ToHashSet();
        foreach (var id in _cards.Keys.Where(id => !liveIds.Contains(id)).ToArray()) _cards.Remove(id);
        var selectedIds = SelectedItems.Select(i => i.Id).ToHashSet();
        var search = SearchBox.Text.Trim();
        var all = _state.GetItems(SelectedPlaylist.Id);
        var items = all.Where(i => i.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) &&
            (OnlyFavorites.IsChecked != true || i.Favorite) &&
            (_mediaFilter == "all" || (_mediaFilter == "video") == i.IsVideo)).ToList();
        var sort = (SortBox.SelectedItem as ComboBoxItem)?.Tag as string;
        if (sort == "newest") items = items.OrderByDescending(i => i.AddedAt).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        else if (sort == "name") items = items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _refreshingItems = true;
        try
        {
            var cards = items.Select(i =>
            {
                if (!_cards.TryGetValue(i.Id, out var card) || !ReferenceEquals(card.Item, i))
                {
                    card = new(i); _cards[i.Id] = card;

                }
                return card;
            }).ToList();
            if (!_viewCards.SequenceEqual(cards))
            {
                _viewCards = cards; WallpaperList.ItemsSource = cards;
                foreach (var card in cards.Where(c => selectedIds.Contains(c.Item.Id))) WallpaperList.SelectedItems.Add(card);
            }
            if (WallpaperList.SelectedItems.Count == 0 && items.Count > 0) WallpaperList.SelectedIndex = 0;
        }
        finally { _refreshingItems = false; }
        LibraryTitle.Text = items.Count == all.Count ? L.ItemCount(items.Count) : L.Text($"{items.Count} / {all.Count} mục");
        LibrarySummary.Text = L.MediaSummary(_state.Items.Count(i => !i.IsVideo), _state.Items.Count(i => i.IsVideo));
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        RotationCollection.Text = L.PlaylistName(SelectedPlaylist);
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = all.Count == 0 ? L.Text("Desktop đang chờ bạn") : L.Text("Chưa có kết quả phù hợp");
        EmptyActionButton.Content = all.Count == 0 ? L.Text("+ Thêm wallpaper") : L.Text("Xóa bộ lọc");
        EmptyHint.Text = all.Count == 0 ? L.Text("Thêm ảnh, video hoặc kéo thả từ máy tính.") : L.Text("Thử tên khác, chọn Tất cả hoặc tắt lọc yêu thích.");
        RemoveButton.Header = SelectedPlaylist.Id == Playlist.AllId ? L.Text("Bỏ khỏi thư viện") : L.Text("Bỏ khỏi bộ");
        SyncReferenceControls();
        QueueThumbnailRefresh();
        ViewRefreshCount++; LastViewRefreshMs = refreshWatch.Elapsed.TotalMilliseconds;
        ViewRefreshed?.Invoke(LastViewRefreshMs);
        _ = UpdatePreview();
    }
    private void PlaylistSelected(object sender, SelectionChangedEventArgs e) => RefreshItems();
    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized || _resettingFilters) return;
        _searchDebounce.Stop();
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ClearSearchButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) RefreshItems(); else _searchDebounce.Start();
    }
    private void FilterChanged(object sender, RoutedEventArgs e) => RefreshItems();
    private void TypeFilterChanged(object sender, RoutedEventArgs e)
    {
        _mediaFilter = (sender as RadioButton)?.Tag as string ?? "all";
        RefreshItems();
    }
    private async void ItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized && !_refreshingItems) await UpdatePreview();
    }
    private async Task UpdatePreview()
    {
        if (!IsLoaded || !IsVisible) return;
        SyncFilmstripSelection();
        QueueThumbnailRefresh();
        var items = SelectedItems;
        SelectionInfo.Text = items.Count == 1 ? L.Text("Đã chọn 1 mục") : items.Count > 1 ? L.Text($"Đã chọn {items.Count} mục") : L.Text("Chưa chọn mục");
        SelectionInfo.ToolTip = items.Count == 1 ? items[0].Path : SelectionInfo.Text;
        SelectionActionsButton.IsEnabled = items.Count > 0;
        var item = items.Count == 1 ? items[0] : null;
        ApplySelectedButton.IsEnabled = item?.Exists == true;
        ExpandPreviewButton.IsEnabled = item?.Exists == true;
        LocateFileButton.Visibility = item != null && !item.Exists ? Visibility.Visible : Visibility.Collapsed;
        PreviewName.Text = item?.Name ?? L.Text("Chọn một wallpaper để xem trước");
        PreviewMeta.Text = item == null ? L.Text("Xem trước chưa thay đổi hình nền.") : !item.Exists ? L.Text("Không tìm thấy file · Tìm lại để giữ mục trong bộ") : L.Text($"{(item.IsVideo ? "Video" : L.Text("Ảnh"))} · {System.IO.Path.GetExtension(item.Path).TrimStart('.').ToUpperInvariant()} · Chưa áp dụng");
        if (InlinePreview.SelectedId != item?.Id || InlinePreview.SelectedPath != item?.Path || (item?.IsVideo == true && InlinePreview.PreviewProcessId == 0))
            await InlinePreview.ShowItemAsync(item, poster: (WallpaperList.SelectedItem as WallpaperCard)?.Thumbnail);
        UpdatePreviewMetadata();
    }
    private void UpdatePlayback()
    {
        if (PlaybackStatus == null) return;
        SyncRotationToggle();
        PlaybackStatus.Text = L.Text(_engine.Status);
        PlaybackStatus.ToolTip = PlaybackStatus.Text;
        NowPlaying.Text = string.IsNullOrEmpty(_engine.CurrentNames) ? L.Text("Chưa phát wallpaper") : _engine.CurrentNames;
        PauseButton.Content = _engine.ManualPaused ? L.Text("Tiếp tục") : L.Text("Tạm dừng");
        RotationButton.Content = _engine.Running && !_engine.Pinned ? L.Text("Dừng luân phiên") : L.Text("Bắt đầu luân phiên");
    }
    private void ShowNotice(string message)
    {
        Notice.Text = L.Text(message);
        NoticePanel.Visibility = Visibility.Visible;
    }

    private void LoadSettings()
    {
        var s = _state.Settings;
        IntervalBox.Text = s.IntervalSeconds.ToString();
        ShuffleBox.SelectedIndex = s.Shuffle ? 1 : 0;
        VideoEndBox.IsChecked = s.AdvanceAtVideoEnd;
    }
    private bool CaptureSettings()
    {
        if (!int.TryParse(IntervalBox.Text.Trim(), out var seconds) || seconds < 5 || seconds > 604800)
            throw new InvalidOperationException(L.Text("Khoảng đổi phải là số nguyên từ 5 đến 604800 giây (7 ngày)."));
        var shuffle = ShuffleBox.SelectedIndex == 1;
        var changedOrder = _state.Settings.Shuffle != shuffle;
        _state.Settings.IntervalSeconds = seconds;
        _state.Settings.Shuffle = shuffle;
        _state.Settings.AdvanceAtVideoEnd = VideoEndBox.IsChecked == true;
        return changedOrder;
    }
    private async void OpenSettings(object sender, RoutedEventArgs e)
    {
        var editor = new SettingsWindow(_state.Settings, !_smoke && Startup.Enabled) { Owner = this };
        if (editor.ShowDialog() != true) return;
        await Run(async () =>
        {
            if (!await SavePreferencesAsync(editor.Result)) return;
            if (!_smoke) Startup.Set(editor.StartWithWindows, _store.DirectoryPath);
            ShowNotice(L.Text("Đã lưu tùy chọn."));
        });
    }
    private void OpenAppMenu(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button) { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void DismissNotice(object sender, RoutedEventArgs e) => NoticePanel.Visibility = Visibility.Collapsed;
    private async Task Run(Func<Task> action)
    {
        if (_actionBusy) return;
        _actionBusy = true;
        try { await action(); }
        catch (Exception ex) { _store.Log(ex.ToString()); ShowNotice(ex.Message); }
        finally { _actionBusy = false; }
    }
    private async void ApplyPlaylist(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_engine.Running && !_engine.Pinned) { _engine.Stop(); return; }
        if (!await SaveRotationDraftAsync()) return;
        await _engine.Apply(TargetId, SelectedPlaylist.Id, SelectedItems.Count == 1 ? SelectedItems[0].Id : null);
        ShowNotice(L.Text("Đã bắt đầu luân phiên. Lịch theo giờ (nếu có) được ưu tiên hơn bộ mặc định."));
    });
    private async void SaveSettings(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (!await SaveRotationDraftAsync()) return;
        ShowNotice(L.Text("Đã lưu khoảng đổi và thứ tự luân phiên."));
    });
    private async void NextWallpaper(object sender, RoutedEventArgs e) => await Run(_engine.NextAll);
    private async void PreviousWallpaper(object sender, RoutedEventArgs e) => await Run(_engine.PreviousAll);
    private void PausePlayback(object sender, RoutedEventArgs e) => _engine.TogglePause();
    private void StopPlayback(object sender, RoutedEventArgs e) => _engine.Stop();
    private void UnpinWallpaper(object sender, RoutedEventArgs e) => _engine.Unpin();
    private async void PinSelected(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (SelectedItems.Count != 1) throw new InvalidOperationException(L.Text("Chọn một wallpaper để đặt làm nền."));
        await _engine.SettingsChanged(false);
        await _engine.Pin(SelectedItems[0], TargetId);
        ShowNotice(L.Text("Đã đặt wallpaper đã chọn. Bấm Bắt đầu luân phiên để xoay tua bộ."));
    });

    private async void ImportFiles(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Multiselect = true, Filter = L.FileFilter };
        if (picker.ShowDialog(this) == true) await Import(picker.FileNames);
    }
    private async void ImportFolder(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog { Title = L.Text("Chọn thư mục wallpaper"), Multiselect = true };
        if (picker.ShowDialog(this) == true) await Import(picker.FolderNames);
    }
    private async void FilesDropped(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths) await ImportDroppedPathsAsync(paths);
    }
    public async Task Import(IEnumerable<string> paths)
    {
        await Run(async () =>
        {
            var inputs = paths.ToArray();
            var playlist = SelectedPlaylist;
            var copy = _state.Settings.CopyOnImport;
            ShowNotice(L.Text("Đang quét và thêm wallpaper…"));
            var files = await Task.Run(() =>
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in inputs)
                {
                    if (Directory.Exists(path))
                    {
                        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                        foreach (var file in Directory.EnumerateFiles(path, "*", options)) if (MediaTypes.Supported(file)) result.Add(System.IO.Path.GetFullPath(file));
                    }
                    else if (File.Exists(path) && MediaTypes.Supported(path)) result.Add(System.IO.Path.GetFullPath(path));
                }
                return result.Order(StringComparer.OrdinalIgnoreCase).ToArray();
            });
            if (files.Length == 0) { ShowNotice(L.Text("Không tìm thấy file wallpaper hợp lệ.")); return; }
            var pendingItems = new List<WallpaperItem>();
            var addedItems = new List<WallpaperItem>();
            var addedMemberships = new List<string>();
            var added = 0;
            var errors = 0;
            foreach (var file in files)
            {
                try
                {
                    var (item, isNew) = await ImportLocalFileAsync(file, copy);
                    if (isNew) { added++; addedItems.Add(item); }
                    pendingItems.Add(item);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { errors++; _store.Log($"Import failed: {file}: {ex.Message}"); }
            }
            // Do not expose pending records while asynchronous file copies allow other UI saves.
            _state.Items.AddRange(addedItems);
            if (_state.Playlists.Contains(playlist) && playlist.Id != Playlist.AllId)
                foreach (var item in pendingItems.Where(_state.Items.Contains))
                    if (!playlist.ItemIds.Contains(item.Id)) { playlist.ItemIds.Add(item.Id); addedMemberships.Add(item.Id); }
            if (!_engine.Save())
            {
                foreach (var item in addedItems) _state.Items.Remove(item);
                playlist.ItemIds.RemoveAll(addedMemberships.Contains);
                RefreshItems();
                ShowNotice(L.Text("File đã quét/copy nhưng chưa lưu được thư viện. File trên ổ đĩa được giữ lại; kiểm tra quyền thư mục dữ liệu rồi thêm lại."));
                return;
            }
            _engine.LibraryChanged();
            RefreshItems();
            ShowNotice(L.Text($"Đã thêm {added} wallpaper mới / {files.Length} file hợp lệ.{(errors > 0 ? L.Text($" {errors} file lỗi, xem app.log.") : "")}"));
        });
    }

    // Both local and downloaded files use the same deduplication/copy/item creation flow.
    private async Task<(WallpaperItem Item, bool Added)> ImportLocalFileAsync(string file, bool copy)
    {
        file = System.IO.Path.GetFullPath(file);
        if (!File.Exists(file) || !MediaTypes.Supported(file)) throw new IOException(L.Text("Không tìm thấy file wallpaper hợp lệ."));
        var existing = _state.Items.Find(i => string.Equals(i.Path, file, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(i.OriginalPath, file, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return (existing, false);
        var destination = file;
        if (copy)
        {
            var folder = System.IO.Path.Combine(_store.DirectoryPath, "media"); Directory.CreateDirectory(folder);
            destination = System.IO.Path.Combine(folder, Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(file));
            await Task.Run(() => File.Copy(file, destination));
        }
        var item = new WallpaperItem { Name = System.IO.Path.GetFileNameWithoutExtension(file), Path = destination,
            OriginalPath = file, IsVideo = MediaTypes.IsVideo(file) };
        return (item, true);
    }
    private void NewPlaylist(object sender, RoutedEventArgs e)
    {
        var name = Prompt.Ask(this, L.Text("Tạo bộ sưu tập"), L.Text("Tên bộ"), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        CreatePlaylist(name);
    }
    private void RenamePlaylist(object sender, RoutedEventArgs e)
    {
        if (SelectedPlaylist.Id == Playlist.AllId) { ShowNotice(L.Text("Bộ Tất cả wallpaper có tên cố định.")); return; }
        var name = Prompt.Ask(this, L.Text("Đổi tên bộ"), L.Text("Tên bộ"), SelectedPlaylist.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        RenamePlaylistTo(SelectedPlaylist, name);
    }
    private void DeletePlaylist(object sender, RoutedEventArgs e)
    {
        var playlist = SelectedPlaylist;
        if (playlist.Id == Playlist.AllId) { ShowNotice(L.Text("Bộ Tất cả wallpaper được giữ để quản lý thư viện.")); return; }
        DeletePlaylistItem(playlist);
    }
    private void AddToPlaylist(object sender, RoutedEventArgs e)
    {
        if (SelectedItems.Count == 0) { ShowNotice(L.Text("Chọn wallpaper cần thêm vào bộ.")); return; }
        var playlists = _state.Playlists.Where(p => p.Id != Playlist.AllId).ToList();
        if (playlists.Count == 0) { ShowNotice(L.Text("Tạo một bộ sưu tập trước.")); return; }
        var chosen = Prompt.Choose(this, L.Text("Thêm vào bộ"), playlists);
        if (chosen == null) return;
        AddItemsToPlaylist(chosen, SelectedItems);
    }
    private void ToggleFavorite(object sender, RoutedEventArgs e)
    {
        var before = SelectedItems.Select(i => (Item: i, Value: i.Favorite)).ToArray();
        if (!CommitMutation(() => { foreach (var entry in before) entry.Item.Favorite = !entry.Value; },
            () => { foreach (var entry in before) entry.Item.Favorite = entry.Value; })) return;
        _cards.Clear(); RefreshItems();
    }
    private void SetDuration(object sender, RoutedEventArgs e)
    {
        if (SelectedItems.Count == 0) { ShowNotice(L.Text("Chọn wallpaper cần đặt thời gian.")); return; }
        var text = Prompt.Ask(this, L.Text("Thời gian riêng"), L.Text("Số giây (để trống để dùng thời gian chung)"), SelectedItems[0].DurationSeconds?.ToString() ?? "");
        if (text == null) return;
        int? seconds = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!int.TryParse(text, out var parsed) || parsed < 5 || parsed > 604800) { ShowNotice(L.Text("Thời gian phải từ 5 đến 604800 giây.")); return; }
            seconds = parsed;
        }
        SetItemsDuration(SelectedItems, seconds);
    }
    private void MoveUp(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int step) => MoveSelectedItem(step);
    private void RemoveItems(object sender, RoutedEventArgs e) => RemoveSelectedItems();
    private void PreviewSelected(object sender, RoutedEventArgs e)
    {
        if (SelectedItems.Count != 1) { ShowNotice(L.Text("Chọn một wallpaper để xem trước.")); return; }
        InlinePreview.Suspend();
        try { new PreviewWindow(SelectedItems[0]) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { ShowNotice(ex.Message); }
        finally { _ = UpdatePreview(); }
    }
    private void EditSchedule(object sender, RoutedEventArgs e)
    {
        var editor = new ScheduleWindow(_state.Playlists, _state.Settings.Schedules, NativeDesktop.Displays()) { Owner = this };
        if (editor.ShowDialog() == true)
        {
            if (!SaveSchedules(editor.Result)) return;
            ShowNotice(L.Text("Đã lưu lịch. Lịch áp dụng khi wallpaper đang chạy và không bị ghim/tạm dừng."));
        }
    }
    private void OpenDataFolder(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_store.DirectoryPath) { UseShellExecute = true }); }
        catch (Exception ex) { ShowNotice(ex.Message); }
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exiting && !_smoke)
        {
            e.Cancel = true;
            InlinePreview.Suspend();
            Hide();
            _tray?.ShowBalloonTip(2000, "TD-WallpaperEngine", L.Text("App vẫn chạy ở khay hệ thống. Chọn Thoát trong menu để tắt."), Forms.ToolTipIcon.Info);
        }
        base.OnClosing(e);
    }
    private void ExitApp(object sender, RoutedEventArgs e) => Exit();
    public void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        _searchDebounce.Stop(); ReleaseThumbnails();
        InlinePreview.Dispose();
        _thumbnails.Dispose();
        _downloads.Dispose();
        _engine.Dispose();
        if (_powerRegistration != 0) NativeDesktop.UnregisterPowerSettingNotification(_powerRegistration);
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Close();
        Application.Current.Shutdown();
    }

    internal void SaveRender(string path)
    {
        UpdateLayout();
        var visual = (FrameworkElement)Content;
        var render = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        render.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(render));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
