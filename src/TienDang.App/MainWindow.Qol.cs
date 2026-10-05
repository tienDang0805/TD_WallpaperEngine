using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Win32;

namespace TienDang.App;

public partial class MainWindow
{
    private Action? _undoRemoval;
    private bool _resettingFilters;

    private void SortChanged(object sender, SelectionChangedEventArgs e) => RefreshItems();
    private void ClearSearch(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }
    internal void ResetLibraryFilters()
    {
        _resettingFilters = true;
        SearchBox.Clear(); OnlyFavorites.IsChecked = false; AllFilter.IsChecked = true;
        _resettingFilters = false; RefreshItems();
    }
    private void EmptyAction(object sender, RoutedEventArgs e)
    {
        if (_state.GetItems(SelectedPlaylist.Id).Count == 0) ImportFiles(sender, e);
        else ResetLibraryFilters();
    }
    private async void HandleShortcuts(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; ImportFiles(sender, e); }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox) { e.Handled = true; UndoLastRemoval(); }
        else if (e.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin && SearchBox.Text.Length > 0) { SearchBox.Clear(); e.Handled = true; }
        else if (e.Key == Key.Enter && WallpaperList.IsKeyboardFocusWithin && Keyboard.FocusedElement is not Button)
        { e.Handled = true; PreviewSelected(sender, e); }
        else if (e.Key == Key.Space && InlinePreview.IsKeyboardFocusWithin && Keyboard.FocusedElement is not Button && Keyboard.FocusedElement is not CheckBox && Keyboard.FocusedElement is not Slider)
        { e.Handled = true; await InlinePreview.ToggleAsync(); }
    }
    private void ShowShortcutHelp(object sender, RoutedEventArgs e) =>
        ShowNotice(L.Text("Ctrl+F: tìm kiếm · Ctrl+O: thêm file · Enter trong danh sách: xem lớn · Esc trong ô tìm: xóa · Ctrl+Z: hoàn tác bỏ mục."));

    private void SelectRowForMenu(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WallpaperCard card } && !WallpaperList.SelectedItems.Contains(card))
            WallpaperList.SelectedItem = card;
    }
    private void OpenRowMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Parent is not FrameworkElement) return;
        FrameworkElement? row = button.Parent as FrameworkElement;
        while (row != null && row.ContextMenu == null) row = VisualTreeHelper.GetParent(row) as FrameworkElement;
        if (row?.ContextMenu == null) return;
        if (button.DataContext is WallpaperCard card && !WallpaperList.SelectedItems.Contains(card)) WallpaperList.SelectedItem = card;
        row.ContextMenu.PlacementTarget = button; row.ContextMenu.IsOpen = true;
        e.Handled = true;
    }
    private void ToggleRowFavorite(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WallpaperCard card }) ToggleFavoriteItem(card.Item.Id);
        e.Handled = true;
    }
    internal void ToggleFavoriteItem(string id)
    {
        var item = _state.Items.Find(i => i.Id == id);
        if (item == null) return;
        var favorite = item.Favorite;
        if (!CommitMutation(() => item.Favorite = !favorite, () => item.Favorite = favorite)) return;
        if (_cards.TryGetValue(id, out var card)) card.RefreshFavorite();
        if (OnlyFavorites.IsChecked == true) RefreshItems();
        UpdatePreviewMetadata();
    }
    private void RevealSelectedFile(object sender, RoutedEventArgs e)
    {
        var item = SelectedItems.FirstOrDefault();
        if (item == null) return;
        try
        {
            if (!File.Exists(item.Path)) { ShowNotice(L.Text("File đã chuyển chỗ. Chọn Tìm lại file để cập nhật đường dẫn.")); return; }
            var launch = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            launch.Arguments = "/select,\"" + item.Path + "\"";
            Process.Start(launch);
        }
        catch (Exception ex) { ShowNotice(ex.Message); }
    }
    private void CopySelectedPath(object sender, RoutedEventArgs e)
    {
        try { if (SelectedItems.Count > 0) { Clipboard.SetText(string.Join(Environment.NewLine, SelectedItems.Select(i => i.Path))); ShowNotice(L.Text("Đã sao chép đường dẫn.")); } }
        catch (Exception ex) { ShowNotice(ex.Message); }
    }
    private async void LocateSelectedFile(object sender, RoutedEventArgs e)
    {
        if (SelectedItems.Count != 1) { ShowNotice(L.Text("Chọn một wallpaper để tìm lại file.")); return; }
        var item = SelectedItems[0];
        var picker = new OpenFileDialog { Title = L.Text("Tìm lại file · ") + item.Name, Filter = L.FileFilter, Multiselect = false };
        if (picker.ShowDialog(this) == true) await Run(async () => await RelinkItemAsync(item.Id, picker.FileName));
    }
    internal async Task RelinkItemAsync(string id, string path)
    {
        var item = _state.Items.Find(i => i.Id == id) ?? throw new InvalidOperationException(L.Text("Mục không còn trong thư viện."));
        path = System.IO.Path.GetFullPath(path);
        if (!File.Exists(path) || !MediaTypes.Supported(path)) throw new InvalidOperationException(L.Text("Chọn một file ảnh hoặc video được hỗ trợ."));
        if (_state.Items.Any(i => i.Id != id && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(L.Text("File này đã thuộc một wallpaper khác trong thư viện."));
        var destination = path;
        if (_state.Settings.CopyOnImport)
        {
            var mediaFolder = System.IO.Path.Combine(_store.DirectoryPath, "media");
            Directory.CreateDirectory(mediaFolder);
            destination = System.IO.Path.Combine(mediaFolder, Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(path));
            await Task.Run(() => File.Copy(path, destination));
        }
        var previous = (item.Path, item.OriginalPath, item.IsVideo);
        if (!CommitMutation(() => { item.Path = destination; item.OriginalPath = path; item.IsVideo = MediaTypes.IsVideo(path); },
            () => (item.Path, item.OriginalPath, item.IsVideo) = previous, true)) return;
        InlinePreview.Suspend();
        _cards.Remove(id); RefreshItems();
        await UpdatePreview();
        ShowNotice(L.Text("Đã tìm lại file. Bộ sưu tập, yêu thích và thời gian riêng được giữ."));
    }
    internal void RemoveSelectedItems()
    {
        var selected = SelectedItems; if (selected.Count == 0) return;
        var ids = selected.Select(i => i.Id).ToHashSet();
        var removingLibrary = SelectedPlaylist.Id == Playlist.AllId;
        var removedItems = _state.Items.Select((item, index) => (item, index)).Where(pair => ids.Contains(pair.item.Id)).ToList();
        var affected = (removingLibrary ? _state.Playlists : [SelectedPlaylist])
            .Select(p => (p.Id, Entries: p.ItemIds.Select((id, index) => (id, index)).Where(pair => ids.Contains(pair.id)).ToList())).ToList();
        if (removingLibrary)
        {
            _state.Items.RemoveAll(i => ids.Contains(i.Id));
            foreach (var playlist in _state.Playlists) playlist.ItemIds.RemoveAll(ids.Contains);
        }
        else SelectedPlaylist.ItemIds.RemoveAll(ids.Contains);
        Action undo = () =>
        {
            if (removingLibrary)
                foreach (var (item, index) in removedItems)
                    if (!_state.Items.Any(i => i.Id == item.Id)) _state.Items.Insert(Math.Min(index, _state.Items.Count), item);
            foreach (var (playlistId, entries) in affected)
            {
                var playlist = _state.Playlists.Find(p => p.Id == playlistId);
                if (playlist == null) continue;
                foreach (var (id, index) in entries)
                    if (_state.Items.Any(i => i.Id == id) && !playlist.ItemIds.Contains(id))
                        playlist.ItemIds.Insert(Math.Min(index, playlist.ItemIds.Count), id);
            }
        };
        if (!_engine.Save()) { undo(); return; }
        _undoRemoval = undo;
        _engine.LibraryChanged(); _cards.Clear(); RefreshItems();
        UndoButton.Visibility = Visibility.Visible;
        ShowNotice(L.Text($"Đã bỏ {selected.Count} mục. File trên ổ đĩa được giữ lại."));
    }
    private void UndoRemoval(object sender, RoutedEventArgs e) => UndoLastRemoval();
    internal void UndoLastRemoval()
    {
        if (_undoRemoval == null) return;
        var undo = _undoRemoval;
        if (!CommitMutation(undo, CaptureItemOrder(), true)) return;
        _undoRemoval = null;
        UndoButton.Visibility = Visibility.Collapsed;
        _cards.Clear(); RefreshItems();
        ShowNotice(L.Text("Đã khôi phục mục và vị trí trong bộ."));
    }
    private void SetIntervalPreset(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menu) IntervalBox.Text = menu.Tag?.ToString() ?? "600";
    }
    private void RotationChanged(object sender, RoutedEventArgs e) => UpdateRotationSaveState();
    private void RotationSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRotationSaveState();
    private void UpdateRotationSaveState()
    {
        if (!_initialized || RotationSaveState == null) return;
        IntervalPresetButton.Content = FriendlyInterval(IntervalBox.Text.Trim()) + "  ⌄";
        var dirty = !int.TryParse(IntervalBox.Text.Trim(), out var seconds) || seconds != _state.Settings.IntervalSeconds ||
            (ShuffleBox.SelectedIndex == 1) != _state.Settings.Shuffle || (VideoEndBox.IsChecked == true) != _state.Settings.AdvanceAtVideoEnd;
        RotationSaveState.Text = dirty ? L.Text("Chưa lưu") : L.Text("Đã lưu"); SaveRotationButton.IsEnabled = dirty;
    }
}