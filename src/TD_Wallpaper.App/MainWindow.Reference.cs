using System.Windows.Input;
using Microsoft.Win32;
namespace TD_Wallpaper.App;
public partial class MainWindow
{
    private bool _syncingRotation, _syncingFilmstrip;
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void AdaptLayout(object sender, SizeChangedEventArgs e)
    {
        if (SidebarColumn == null || StandingMascot == null) return;
        var compact = ActualWidth < 1170;
        SidebarColumn.Width = new GridLength(compact ? 74 : 194);
        foreach (var label in new[] { BrandName, NavAllLabel, NavVideoLabel, NavImageLabel, NavFavoritesLabel, NavSettingsLabel, NavScheduleLabel, NavLibraryToolsLabel })
            label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in new[] { NavAll, NavVideo, NavImage, NavFavorites, NavSettings, NavSchedule, NavLibraryTools }) { button.Padding = new Thickness(compact ? 7 : 15, 8, compact ? 7 : 15, 8); button.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left; }
        DeskMascot.Visibility = SidebarHelp.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        StandingMascot.Width = compact ? 46 : ActualHeight < 800 ? 90 : 113;
        StandingMascot.Height = compact ? 46 : ActualHeight < 800 ? 89 : 112;
        DeskMascot.MaxHeight = ActualHeight < 800 ? 130 : 175;
        SidebarBrand.Margin = new Thickness(0, 0, 0, compact ? 22 : 16);
    }
    private void NavigateLibrary(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string category }) return;
        OnlyFavorites.IsChecked = category == "favorites";
        if (category is "all" or "favorites") AllFilter.IsChecked = true;
        else if (category == "video") VideoFilter.IsChecked = true;
        else ImageFilter.IsChecked = true;
        RefreshItems();
    }
    private void SyncReferenceControls()
    {
        if (!_initialized) return;
        var active = OnlyFavorites.IsChecked == true ? "favorites" : _mediaFilter;
        foreach (var button in new[] { NavAll, NavVideo, NavImage, NavFavorites })
            button.Background = Equals(button.Tag, active) ? new SolidColorBrush(Color.FromRgb(210,225,250)) : Brushes.Transparent;
        if (!ReferenceEquals(Filmstrip.ItemsSource, WallpaperList.ItemsSource))
        {
            _syncingFilmstrip = true;
            try { Filmstrip.ItemsSource = WallpaperList.ItemsSource; }
            finally { _syncingFilmstrip = false; }
        }
        SyncFilmstripSelection();
    }
    private void SyncFilmstripSelection()
    {
        _syncingFilmstrip = true;
        try
        {
            Filmstrip.SelectedItem = WallpaperList.SelectedItem;
            if (Filmstrip.SelectedItem != null) Filmstrip.ScrollIntoView(Filmstrip.SelectedItem);
        }
        finally { _syncingFilmstrip = false; }
    }
    private void FilmstripSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_initialized && e.WidthChanged) SyncFilmstripSelection();
    }
    private void ScrollFilmstrip(object sender, MouseWheelEventArgs e)
    {
        var viewer = FindFilmstripChild<ScrollViewer>(Filmstrip);
        if (viewer == null || viewer.ScrollableWidth <= 0 || e.Delta == 0) return;
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
    private static T? FindFilmstripChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindFilmstripChild<T>(child) is { } nested) return nested;
        }
        return null;
    }
    private void FilmstripSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingFilmstrip || Filmstrip.SelectedItem is not WallpaperCard card) return;
        WallpaperList.SelectedItem = card;
        WallpaperList.ScrollIntoView(card);
    }
    private void PreviousPreview(object sender, RoutedEventArgs e) => StepPreview(-1);
    private void NextPreview(object sender, RoutedEventArgs e) => StepPreview(1);
    private void StepPreview(int direction)
    {
        if (WallpaperList.Items.Count == 0) return;
        WallpaperList.SelectedIndex = (Math.Max(0, WallpaperList.SelectedIndex) + direction + WallpaperList.Items.Count) % WallpaperList.Items.Count;
        WallpaperList.ScrollIntoView(WallpaperList.SelectedItem);
        Filmstrip.ScrollIntoView(Filmstrip.SelectedItem);
    }
    private void TogglePreviewFavorite(object sender, RoutedEventArgs e)
    {
        if (SelectedItems.Count == 1) ToggleFavoriteItem(SelectedItems[0].Id);
        UpdatePreviewMetadata();
    }
    private void UpdatePreviewMetadata()
    {
        if (PreviewFavoriteButton == null) return;
        var item = SelectedItems.Count == 1 ? SelectedItems[0] : null;
        PreviewFavoriteButton.IsEnabled = item != null;
        PreviewFavoriteButton.Content = item?.Favorite == true ? "♥" : "♡";
        PreviewFavoriteButton.Foreground = item?.Favorite == true ? new SolidColorBrush(Color.FromRgb(237,86,120)) : (Brush)FindResource("Navy");
        if (item?.Exists == true)
        {
            try
            {
                var bytes = new FileInfo(item.Path).Length;
                PreviewMeta.Text = System.IO.Path.GetExtension(item.Path).TrimStart('.').ToUpperInvariant() + "  ·  " +
                    (InlinePreview.MediaWidth > 0 ? L.Text($"{InlinePreview.MediaWidth} × {InlinePreview.MediaHeight}  ·  ") : "") +
                    (InlinePreview.Duration > 0 ? TimeSpan.FromSeconds(InlinePreview.Duration).ToString(@"mm\:ss") + "  ·  " : "") +
                    (bytes / 1048576d).ToString("0.0") + " MB";
            }
            catch (IOException) { }
        }
    }
    private async void ToggleRotation(object sender, RoutedEventArgs e)
    {
        if (!_initialized || _syncingRotation) return;
        await Run(async () =>
        {
            if (RotationToggle.IsChecked != true) { if (!_engine.Pinned) _engine.Stop(); }
            else { if (!await SaveRotationDraftAsync()) return; await _engine.Apply(TargetId, SelectedPlaylist.Id, SelectedItems.Count == 1 ? SelectedItems[0].Id : null); }
        });
        UpdatePlayback();
    }
    private void SyncRotationToggle()
    {
        _syncingRotation = true;
        try { RotationToggle.IsChecked = _engine.Running && !_engine.Pinned; }
        finally { _syncingRotation = false; }
    }
    private static string FriendlyInterval(string text) => int.TryParse(text, out var seconds)
        ? seconds % 3600 == 0 ? L.Text($"{seconds / 3600} giờ") : seconds % 60 == 0 ? L.Text($"{seconds / 60} phút") : L.Text($"{seconds} giây") : L.Text("Thời gian khác");
    private void CustomInterval(object sender, RoutedEventArgs e)
    {
        var text = Prompt.Ask(this, L.Text("Khoảng đổi nền"), L.Text("Số giây (từ 5 đến 604800)"), IntervalBox.Text);
        if (text != null) IntervalBox.Text = text.Trim();
    }
    private void ImportUrl(object sender, RoutedEventArgs e) => OpenUrlImport(null);
    private void ImportClipboardUrl(object sender, RoutedEventArgs e)
    {
        try { OpenUrlImport(Clipboard.ContainsText() ? Clipboard.GetText().Trim() : null); }
        catch { OpenUrlImport(null); ShowNotice(L.Text("Không đọc được clipboard. Dán link bằng Ctrl+V trong hộp thoại.")); }
    }
    private void OpenUrlImport(string? initial)
    {
        var playlistId = SelectedPlaylist.Id;
        InlinePreview.Suspend();
        try { var editor = new UrlImportWindow(_store, initial, result => RegisterDownloadedAsync(result, playlistId), _state.Items, enqueue: (info, directory, height) => { _downloads.Enqueue(info.Url, directory, playlistId, height); ShowDownloads(); }) { Owner = this }; if (editor.ShowDialog() == true && editor.CompletedItem is { } item) { ResetLibraryFilters(); WallpaperList.SelectedItem = WallpaperList.Items.Cast<WallpaperCard>().FirstOrDefault(c => c.Item.Id == item.Id); } }
        finally { _ = UpdatePreview(); }
    }
    internal async Task<WallpaperItem> RegisterDownloadedAsync(DownloadedMedia result, string playlistId)
    {
        var existing = _state.Items.Find(i => i.SourceUrl == result.Source.AbsoluteUri && i.Exists);
        var managedMedia = System.IO.Path.GetFullPath(System.IO.Path.Combine(_store.DirectoryPath, "media")) + System.IO.Path.DirectorySeparatorChar;
        var (item, added) = existing != null ? (existing, false) : await ImportLocalFileAsync(result.Path,
            _state.Settings.CopyOnImport && !System.IO.Path.GetFullPath(result.Path).StartsWith(managedMedia, StringComparison.OrdinalIgnoreCase));
        if (added) _state.Items.Add(item);
        var originalSource = item.SourceUrl;
        item.SourceUrl = result.Source.AbsoluteUri;
        var playlist = _state.Playlists.Find(p => p.Id == playlistId);
        var addedMembership = playlist != null && playlist.Id != Playlist.AllId && !playlist.ItemIds.Contains(item.Id);
        if (addedMembership) playlist!.ItemIds.Add(item.Id);
        if (!_engine.Save())
        {
            if (added) _state.Items.Remove(item);
            else item.SourceUrl = originalSource;
            if (addedMembership) playlist!.ItemIds.Remove(item.Id);
            throw new IOException(L.Text("File đã tải nhưng chưa lưu được thư viện. Kiểm tra quyền thư mục dữ liệu rồi thử lại."));
        }
        ResetLibraryFilters(); RefreshPlaylists(playlist?.Id ?? Playlist.AllId);
        _engine.LibraryChanged(); RefreshItems();
        WallpaperList.SelectedItem = WallpaperList.Items.Cast<WallpaperCard>().FirstOrDefault(c => c.Item.Id == item.Id);
        ShowNotice(L.Text("Đã tải và thêm ") + item.Name + L.Text(". File được giữ trên ổ đĩa."));
        return item;
    }
}
