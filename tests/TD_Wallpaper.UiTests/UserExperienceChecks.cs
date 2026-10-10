using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TD_Wallpaper.App;
using TD_Wallpaper.Core;

internal static class UserExperienceChecks
{
    internal static async Task Run(MainWindow window, LibraryState state, WallpaperItem video, string directory)
    {
        var list = (ListBox)window.FindName("WallpaperList");
        var preview = (WallpaperPreview)window.FindName("InlinePreview");
        var store = new StateStore(directory);
        await Until(() => preview.HasVideoFrame, "initial video preview has a decoded frame");
        var previewProcess = preview.PreviewProcessId;
        var settings = new SettingsWindow(state.Settings, false) { Owner = window };
        foreach (var (tag, stretch) in new[] { ("Fill", Stretch.UniformToFill), ("Fit", Stretch.Uniform), ("Stretch", Stretch.Fill) })
            Require(((Image)settings.FindName("FitPreview" + tag)).Stretch == stretch, tag + " illustration uses the real rendering mode");
        settings.Show(); await Task.Delay(150);
        Capture(settings, Path.Combine(directory, "settings-vi.png"));
        settings.Close();
        var draft = new SettingsWindow(state.Settings, false) { Owner = window };
        draft.Loaded += (_, _) =>
        {
            ((ComboBox)draft.FindName("LanguageChoice")).SelectedValue = "en";
            ((ComboBox)draft.FindName("FrameRateChoice")).SelectedValue = 30;
            ((CheckBox)draft.FindName("PauseMaximizedChoice")).IsChecked = true;
            ((RadioButton)draft.FindName("FitChoiceFit")).IsChecked = true;
            ((Button)draft.FindName("SavePreferencesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        Require(draft.ShowDialog() == true && draft.Result.Language == "en" && draft.Result.FrameRateLimit == 30 && draft.Result.Fit == "Fit" && draft.Result.PauseMaximized, "settings controls produce a language/FPS/fit draft");
        Require(state.Settings.Language == "vi" && state.Settings.FrameRateLimit == 0, "settings draft does not mutate live state");
        Require(await window.SavePreferencesAsync(draft.Result), "language/FPS settings commit");
        Require(preview.PreviewProcessId == previewProcess, "language/FPS changes preserve the preview decoder");
        Require(L.State.Language == "en" && store.Load().Settings.Language == "en" && store.Load().Settings.FrameRateLimit == 30 && store.Load().Settings.PauseMaximized, "language and FPS persist");
        Require(L.Text("Tạm dừng khi cửa sổ phóng to") == "Pause when a window is maximized", "maximized policy uses complete English label");
        Require(((TextBlock)window.FindName("NavAllLabel")).Text == "Library", "XAML labels update without restarting");
        Require(((ComboBoxItem)((ComboBox)window.FindName("ShuffleBox")).Items[1]).Content.ToString() == "Shuffle", "shuffle menu is English");
        Require(((ComboBox)window.FindName("MonitorBox")).Items.Cast<DisplayInfo>().First().Label == "All displays", "display selector is English");
        Require(L.Text("Đã thêm 2 wallpaper vào Thư viện.") == "Added 2 wallpapers to Thư viện.", "user-provided collection names are not translated");
        Require(L.PlaylistName(new Playlist { Id = "user", Name = "Tất cả wallpaper" }) == "Tất cả wallpaper", "user collection names stay intact even when they match built-in labels");
        Require(L.PlaylistName(state.Playlists.First(p => p.Id == Playlist.AllId)) == "All wallpapers", "built-in collection translates by ID");
        Require(L.Text("Nguồn trả lỗi HTTP 403. Kiểm tra lại link hoặc quyền truy cập.") == "The source returned HTTP 403. Check the link or your access permissions.", "dynamic download errors use complete English sentences");
        Require(L.Text("Đã thêm 1 wallpaper vào Thư viện.") == "Added 1 wallpaper to Thư viện.", "English count messages use singular nouns");
        Require(L.Text("Đã thêm 1 wallpaper mới / 1 file hợp lệ.") == "Added 1 new wallpaper / 1 supported file.", "English import summary has correct singular grammar");
        Require(L.Text("Đã thêm 1 wallpaper vào 1 wallpapers.") == "Added 1 wallpaper to 1 wallpapers.", "plural grammar never edits user-provided names");
        Require(L.FileFilter.Contains("|All files|"), "native file picker filter is English");
        var enSettings = new SettingsWindow(state.Settings, false) { Owner = window };
        enSettings.Show(); await Task.Delay(150); Capture(enSettings, Path.Combine(directory, "settings-en.png")); enSettings.Close();
        window.Width = 1380; window.Height = 880; await Task.Delay(150);
        await preview.RenderSnapshotAsync(() => window.SaveRender(Path.Combine(directory, "library-en.png")));
        window.Width = 1040; window.Height = 690; await Task.Delay(150);
        Require(((CheckBox)window.FindName("VideoEndBox")).ActualWidth > 160, "English video-end option remains readable at minimum width");
        await preview.RenderSnapshotAsync(() => window.SaveRender(Path.Combine(directory, "library-en-compact.png")));
        var playlist = window.CreatePlaylist("Drop · Thư viện")!;
        var importFolder = Path.Combine(directory, "drop-files"); Directory.CreateDirectory(importFolder);
        var path = Path.Combine(importFolder, "Dropped" + Path.GetExtension(video.Path));
        File.Copy(video.Path, path);
        var data = new DataObject(DataFormats.FileDrop, new[] { path });
        var drag = Drag(data, list, DragDrop.PreviewDragOverEvent); list.RaiseEvent(drag);
        Require(drag.Handled && drag.Effects == DragDropEffects.Copy && ((Border)window.FindName("DropOverlay")).Visibility == Visibility.Visible, "routed drag-over accepts video and shows the drop zone");
        await preview.RenderSnapshotAsync(() => window.SaveRender(Path.Combine(directory, "drop-overlay-en.png")));
        var dropped = Drag(data, list, DragDrop.PreviewDropEvent); list.RaiseEvent(dropped);
        await Until(() => state.Items.Any(i => i.OriginalPath == path), "routed file drop imports video");
        var item = state.Items.Single(i => i.OriginalPath == path);
        Require(item.IsVideo && playlist.ItemIds.Contains(item.Id) && store.Load().Items.Any(i => i.Id == item.Id), "drop uses the existing import/save/collection flow");
        await Until(() => preview.SelectedId == item.Id && preview.HasVideoFrame, "dropped video is selected and has a decoded preview");
        await Until(() => list.Items.Cast<WallpaperCard>().Single(c => c.Item.Id == item.Id).Thumbnail != null, "dropped video has a real thumbnail");
        Require(((Border)window.FindName("DropOverlay")).Visibility == Visibility.Collapsed, "drop clears the overlay");
        var count = state.Items.Count;
        await window.ImportDroppedPathsAsync([path]);
        Require(state.Items.Count == count && playlist.ItemIds.Count(id => id == item.Id) == 1, "duplicate drop does not duplicate media or collection membership");
        var invalid = Path.Combine(importFolder, "invalid.txt"); File.WriteAllText(invalid, "not wallpaper");
        var invalidDrag = Drag(new DataObject(DataFormats.FileDrop, new[] { invalid }), list, DragDrop.PreviewDragOverEvent);
        list.RaiseEvent(invalidDrag); Require(invalidDrag.Effects == DragDropEffects.None, "unsupported files are not offered as valid drops");
        var nested = Path.Combine(importFolder, "nested"); Directory.CreateDirectory(nested);
        var copiedPath = Path.Combine(nested, "Copied" + Path.GetExtension(video.Path)); File.Copy(video.Path, copiedPath);
        state.Settings.CopyOnImport = true;
        await window.ImportDroppedPathsAsync([importFolder]);
        var copied = state.Items.Single(i => i.OriginalPath == copiedPath);
        Require(copied.IsVideo && copied.Path != copiedPath && File.Exists(copied.Path), "folder drops retain copy-on-import behavior");
        Require(state.Items.Count == count + 1, "folder drops skip duplicates and unsupported files");
        state.Settings.CopyOnImport = false;
        var back = new AppSettings { Language = "vi", FrameRateLimit = 0 };
        Require(await window.SavePreferencesAsync(back), "switch back to Vietnamese");
        Require(((TextBlock)window.FindName("NavAllLabel")).Text == "Thư viện" && L.Text("Lấp đầy · cắt phần dư") == "Kín màn hình", "Vietnamese uses natural labels");
        Require(!state.Settings.WasRunning, "UX checks leave the personal desktop untouched");
        Console.WriteLine("PASS User experience regression; screenshots: " + directory);
    }
    private static DragEventArgs Drag(IDataObject data, DependencyObject target, RoutedEvent routedEvent)
    {
        var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var args = (DragEventArgs)constructor.Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, target, new Point(50,50)]);
        args.RoutedEvent = routedEvent; return args;
    }
    private static void Capture(Window window, string path)
    {
        window.UpdateLayout(); var visual = (FrameworkElement)window;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static async Task Until(Func<bool> predicate, string name)
    {
        for (var i = 0; i < 250 && !predicate(); i++) await Task.Delay(100);
        Require(predicate(), name);
    }
    private static void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS " + name); }
}