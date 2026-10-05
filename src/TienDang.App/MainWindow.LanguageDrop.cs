namespace TienDang.App;
public partial class MainWindow
{
    private void RefreshLanguage()
    {
        InlinePreview.RefreshLanguage();
        foreach (var card in _cards.Values) card.RefreshLanguage();
        L.SetPlaylistTemplate(PlaylistList);
        RefreshDisplays(); RefreshPlaylists(); RefreshItems();
        UpdateRotationSaveState();
        IntervalPresetButton.Content = FriendlyInterval(IntervalBox.Text) + "  ⌄";
        if (_tray?.ContextMenuStrip != null)
            foreach (System.Windows.Forms.ToolStripItem item in _tray.ContextMenuStrip.Items)
                if (item.Tag is string label) item.Text = L.Text(label);
        _engine.RefreshLanguage();
    }
    private void FilesDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        var supported = paths?.Any(p => Directory.Exists(p) || File.Exists(p) && MediaTypes.Supported(p)) == true;
        e.Effects = supported && !_actionBusy && (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = e.Effects == DragDropEffects.Copy ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }
    private void FilesDragLeave(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(this);
        if (position.X < 0 || position.Y < 0 || position.X >= ActualWidth || position.Y >= ActualHeight)
            DropOverlay.Visibility = Visibility.Collapsed;
    }
    internal async Task ImportDroppedPathsAsync(IEnumerable<string> paths)
    {
        if (_actionBusy) { ShowNotice(L.Text("Đang thêm file. Chờ xong rồi thả tiếp nhé.")); return; }
        await Import(paths);
    }
}