using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TD_Wallpaper.App;
using TD_Wallpaper.Core;

internal static class QolChecks
{
    internal static async Task Run(MainWindow window, LibraryState state, WallpaperItem video, WallpaperItem image, string directory)
    {
        T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS QoL " + name); }
        var list = Control<ListBox>("WallpaperList");
        var preview = Control<WallpaperPreview>("InlinePreview");
        var search = Control<TextBox>("SearchBox");
        var sort = Control<ComboBox>("SortBox");
        var favorites = Control<CheckBox>("OnlyFavorites");
        void Select(string id) => list.SelectedItem = list.Items.Cast<WallpaperCard>().Single(c => c.Item.Id == id);
        async Task Wait(Func<bool> condition)
        {
            for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(50);
            if (!condition()) Console.WriteLine("DIAGNOSTIC preview: " + preview.SelectedId + " | " + preview.SelectedPath + " | " + Control<TextBlock>("PreviewState").Text + " | selection: " + string.Join(",", list.SelectedItems.Cast<WallpaperCard>().Select(c => c.Item.Id)));
            Check(condition(), "asynchronous preview/control ready");
        }
        window.ResetLibraryFilters(); sort.SelectedIndex = 0; Select(video.Id);
        await Wait(() => preview.IsVideoReady);
        var originalProcess = preview.PreviewProcessId;
        window.ToggleFavoriteItem(image.Id);
        Check(image.Favorite && !video.Favorite && preview.PreviewProcessId == originalProcess, "row favorite affects only its item without reopening selected video");
        favorites.IsChecked = true;
        Check(list.Items.Count == 1 && ((WallpaperCard)list.Items[0]).Item.Id == image.Id, "favorites filter uses updated state");
        window.ResetLibraryFilters();
        video.AddedAt = DateTime.Now.AddDays(-2); image.AddedAt = DateTime.Now.AddDays(-1);
        sort.SelectedIndex = 1;
        Check(list.Items.Cast<WallpaperCard>().Select(c => c.Item.AddedAt).SequenceEqual(state.Items.OrderByDescending(i => i.AddedAt).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).Select(i => i.AddedAt)), "newest order");
        sort.SelectedIndex = 2;
        Check(list.Items.Cast<WallpaperCard>().Select(c => c.Name).SequenceEqual(state.Items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).Select(i => i.Name)), "name order");
        sort.SelectedIndex = 0;
        Check(list.Items.Cast<WallpaperCard>().Select(c => c.Item.Id).SequenceEqual(state.Items.Select(i => i.Id)), "playlist order is preserved by display sorting");
        search.Text = "no-result-qol"; await Wait(() => !window.SearchPending);
        Check(Control<StackPanel>("EmptyState").Visibility == Visibility.Visible && Control<Button>("ClearSearchButton").Visibility == Visibility.Visible, "empty search has recovery actions");
        Control<Button>("ClearSearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(search.Text == "" && list.Items.Count == state.Items.Count, "clear search restores results");
        var first = new Playlist { Id = "qol-one", Name = "Bộ thử 1", ItemIds = [video.Id, image.Id] };
        var second = new Playlist { Id = "qol-two", Name = "Bộ thử 2", ItemIds = [image.Id, video.Id] };
        state.Playlists.Add(first); state.Playlists.Add(second);
        // Force the collection selector to refresh against the live collection list.
        var collection = Control<ComboBox>("PlaylistList");
        collection.ItemsSource = null; collection.ItemsSource = state.Playlists; collection.SelectedItem = state.Playlists[0];
        window.ResetLibraryFilters(); Select(image.Id);
        var originalIds = state.Items.Select(i => i.Id).ToArray();
        window.RemoveSelectedItems();
        Check(!state.Items.Any(i => i.Id == image.Id) && !first.ItemIds.Contains(image.Id) && !second.ItemIds.Contains(image.Id) && File.Exists(image.Path), "library removal updates collections but retains physical file");
        window.UndoLastRemoval();
        Check(state.Items.Select(i => i.Id).SequenceEqual(originalIds) && first.ItemIds.SequenceEqual(new[] { video.Id, image.Id }) && second.ItemIds.SequenceEqual(new[] { image.Id, video.Id }), "undo restores library and membership order");
        collection.SelectedItem = first; Select(image.Id); window.RemoveSelectedItems();
        Check(state.Items.Any(i => i.Id == image.Id) && !first.ItemIds.Contains(image.Id) && second.ItemIds.Contains(image.Id), "collection removal affects only its membership");
        window.UndoLastRemoval();
        Check(first.ItemIds.SequenceEqual(new[] { video.Id, image.Id }), "collection undo restores original position");
        collection.SelectedItem = state.Playlists[0]; window.ResetLibraryFilters(); Select(video.Id);
        var relinkPath = Path.Combine(directory, "moved" + Path.GetExtension(video.Path));
        File.Copy(video.Path, relinkPath);
        video.Favorite = true; video.DurationSeconds = 90;
        var title = video.Name;
        await window.RelinkItemAsync(video.Id, relinkPath);
        await Wait(() => preview.IsVideoReady);
        Check(video.Path == relinkPath && video.Name == title && video.Favorite && video.DurationSeconds == 90 &&
            first.ItemIds.Contains(video.Id) && second.ItemIds.Contains(video.Id), "relink retains identity, name, settings and collection membership");
        var unsupported = Path.Combine(directory, "wrong.txt"); File.WriteAllText(unsupported, "not wallpaper");
        try { await window.RelinkItemAsync(video.Id, unsupported); throw new InvalidOperationException("accepted unsupported relink"); }
        catch (InvalidOperationException ex) when (ex.Message != "accepted unsupported relink") { Check(video.Path == relinkPath, "unsupported relink leaves original record"); }
        try { await window.RelinkItemAsync(video.Id, image.Path); throw new InvalidOperationException("accepted duplicate relink"); }
        catch (InvalidOperationException ex) when (ex.Message != "accepted duplicate relink") { Check(video.Path == relinkPath, "duplicate relink rejected"); }
        state.Settings.CopyOnImport = true;
        await window.RelinkItemAsync(video.Id, relinkPath);
        await Wait(() => preview.IsVideoReady);
        Check(video.Path != relinkPath && File.Exists(video.Path) && video.OriginalPath == relinkPath, "relink honors copy-into-library");
        state.Settings.CopyOnImport = false;
        var interval = Control<TextBox>("IntervalBox");
        interval.Text = "300";
        Check(Control<Button>("SaveRotationButton").IsEnabled && Control<TextBlock>("RotationSaveState").Text == "Chưa lưu", "rotation draft visibly unsaved");
        Control<Button>("SaveRotationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Wait(() => state.Settings.IntervalSeconds == 300 && !Control<Button>("SaveRotationButton").IsEnabled);
        Check(new StateStore(directory).Load().Settings.IntervalSeconds == 300, "save persists interval");
        interval.Text = "invalid";
        Control<Button>("SaveRotationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(80);
        Check(state.Settings.IntervalSeconds == 300 && Control<Button>("SaveRotationButton").IsEnabled, "invalid interval keeps saved value");
        interval.Text = "300";
        window.Width = 1280; window.Height = 850;
        search.Text = "not-a-wallpaper"; await Wait(() => !window.SearchPending);
        await Task.Delay(150); window.SaveRender(Path.Combine(directory, "exact-base-no-results.png"));
        window.ResetLibraryFilters(); Select(video.Id); await Wait(() => preview.IsVideoReady);
        Control<Border>("NoticePanel").Visibility = Visibility.Collapsed;
        await preview.SeekAsync(3); await Task.Delay(200);
        await preview.RenderSnapshotAsync(() => window.SaveRender(Path.Combine(directory, "exact-base-video.png")));
        Select(image.Id); await Wait(() => preview.ImageSource != null);
        window.SaveRender(Path.Combine(directory, "exact-base-image.png"));
        window.Width = 1040; window.Height = 690; await Task.Delay(150);
        Check(Control<Button>("RotationButton").ActualWidth > 90 && interval.ActualHeight <= 40 && search.ActualHeight <= 46, "compact layout retains actions and readable input height");
        window.SaveRender(Path.Combine(directory, "exact-base-compact.png"));
                var popout = new PreviewWindow(video) { Owner = window };
        popout.Show(); await Wait(() => popout.Preview.IsVideoReady);
        var popoutProcess = popout.Preview.PreviewProcessId;
        Check(popout.Preview.IsPaused && popoutProcess > 0, "large preview shares paused playback controls");
        popout.Close(); await Task.Delay(150);
        Check(popout.Preview.PreviewProcessId == 0, "large preview closes owned decoder");
        var settingsWindow = new SettingsWindow(state.Settings, false) { Owner = window };
        settingsWindow.Show(); await Task.Delay(100);
        settingsWindow.Close();
        state.Items.Clear(); first.ItemIds.Clear(); second.ItemIds.Clear(); window.ResetLibraryFilters(); await Task.Delay(150);
        window.Width = 1280; window.Height = 850; await Task.Delay(150);
        window.SaveRender(Path.Combine(directory, "exact-base-empty.png"));
        Check(Control<Image>("EmptyMascot").IsVisible && Control<Button>("EmptyActionButton").Content?.ToString() == "+ Thêm wallpaper", "empty library uses supplied mascot and import action");
        Check(!state.Settings.WasRunning, "QoL checks never apply personal desktop wallpaper");
    }
}