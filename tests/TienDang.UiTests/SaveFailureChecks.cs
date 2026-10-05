using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TienDang.App;
using TienDang.Core;

internal static class SaveFailureChecks
{
    internal static async Task Run(MainWindow window, LibraryState state, string directory)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS Save failure " + message); }
        T C<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        await Task.Delay(500);
        var store = new StateStore(directory);
        store.Save(state);
        FileStream Lock() => new(store.StatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var list = C<ListBox>("WallpaperList");
        var playlists = C<ComboBox>("PlaylistList");
        void Select(string id) => list.SelectedItem = list.Items.Cast<WallpaperCard>().Single(c => c.Item.Id == id);
        bool HasError() => C<TextBlock>("Notice").Text.Contains("Không lưu được");
        var image = state.Items.First(i => !i.IsVideo);
        var favorite = image.Favorite;
        using (Lock())
        {
            window.ToggleFavoriteItem(image.Id);
            Check(image.Favorite == favorite && HasError(), "favorite rolls back and shows save error");
        }
        var source = Path.Combine(directory, "new-import.jpg");
        File.Copy(image.Path, source);
        state.Settings.CopyOnImport = true; store.Save(state);
        var collection = new Playlist { Id = "save-failure", Name = "Save failure", ItemIds = [image.Id] };
        state.Playlists.Add(collection); store.Save(state);
        playlists.ItemsSource = null; playlists.ItemsSource = state.Playlists; playlists.SelectedItem = collection;
        var ids = state.Items.Select(i => i.Id).ToArray();
        var members = collection.ItemIds.ToArray();
        using (Lock())
        {
            await window.Import([source]);
            Check(state.Items.Select(i => i.Id).SequenceEqual(ids) && collection.ItemIds.SequenceEqual(members), "failed import rolls back records and membership");
            Check(C<TextBlock>("Notice").Text.Contains("chưa lưu") && File.Exists(source) && Directory.GetFiles(Path.Combine(directory, "media")).Length == 1, "failed import retains original and copied media with honest notice");
        }
        state.Settings.CopyOnImport = false;
        Select(image.Id);
        using (Lock())
        {
            window.RemoveSelectedItems();
            Check(collection.ItemIds.SequenceEqual(members) && HasError(), "failed collection removal retains membership");
        }
        window.RemoveSelectedItems();
        Check(!collection.ItemIds.Contains(image.Id), "removal succeeds after lock release");
        using (Lock())
        {
            window.UndoLastRemoval();
            Check(!collection.ItemIds.Contains(image.Id) && C<Button>("UndoButton").Visibility == Visibility.Visible && HasError(), "failed undo retains removed state and retry action");
        }
        window.UndoLastRemoval();
        Check(collection.ItemIds.SequenceEqual(members), "undo can retry after save error");
        playlists.SelectedItem = state.Playlists[0]; window.ResetLibraryFilters(); Select(image.Id);
        using (Lock())
        {
            window.RemoveSelectedItems();
            Check(state.Items.Select(i => i.Id).SequenceEqual(ids) && collection.ItemIds.SequenceEqual(members), "failed library removal restores identity and order");
        }
        var originalPath = image.Path;
        using (Lock())
        {
            await window.RelinkItemAsync(image.Id, source);
            Check(image.Path == originalPath && HasError(), "failed relink keeps original metadata");
        }
        C<TextBox>("IntervalBox").Text = "123";
        using (Lock())
        {
            C<Button>("SaveRotationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            Check(state.Settings.IntervalSeconds == 600 && C<TextBlock>("RotationSaveState").Text == "Chưa lưu" && C<Button>("SaveRotationButton").IsEnabled, "failed rotation save keeps draft and persisted settings");
        }
        C<Button>("SaveRotationButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Task.Delay(100);
        Check(store.Load().Settings.IntervalSeconds == 123 && C<TextBlock>("RotationSaveState").Text == "Đã lưu", "rotation draft retries successfully");
        var beforePlaylists = state.Playlists.Select(p => p.Id).ToArray();
        var originalName = collection.Name;
        var video = state.Items.First(i => i.IsVideo);
        using (Lock())
        {
            Check(window.CreatePlaylist("Not committed") == null && state.Playlists.Select(p => p.Id).SequenceEqual(beforePlaylists), "failed create retains collection list");
            Check(!window.RenamePlaylistTo(collection, "Not committed") && collection.Name == originalName, "failed rename restores original name");
            Check(!window.AddItemsToPlaylist(collection, [video]) && collection.ItemIds.SequenceEqual(members), "failed add-to-collection retains membership");
            Check(!window.SetItemsDuration([image, video], 60) && image.DurationSeconds == null && video.DurationSeconds == null, "failed bulk duration restores prior values");
            var oldSchedules = state.Settings.Schedules;
            Check(!window.SaveSchedules([new() { PlaylistId = collection.Id }]) && ReferenceEquals(oldSchedules, state.Settings.Schedules), "failed schedule edit restores previous schedule list");
            Check(!await window.SavePreferencesAsync(new AppSettings { Fit = "Stretch", Volume = 95, CopyOnImport = true, Language = "en", FrameRateLimit = 15, PauseMaximized = true, PerformanceProfile = "Saver", ReleaseWhenBusy = true, AppRules = [new() { ProcessName = "game.exe", Action = "Release" }] }) &&
                state.Settings.Fit == "Fill" && state.Settings.Volume == 30 && !state.Settings.CopyOnImport && state.Settings.Language == "vi" && state.Settings.FrameRateLimit == 0 && !state.Settings.PauseMaximized && state.Settings.PerformanceProfile == "Custom" && !state.Settings.ReleaseWhenBusy && state.Settings.AppRules.Count == 0 && L.State.Language == "vi", "failed preferences restore saved fields and language/FPS");
            try
            {
                await window.RegisterDownloadedAsync(new(source, new Uri("https://fixture.test/new.jpg"), "new.jpg"), collection.Id);
                throw new InvalidOperationException("accepted failed URL registration");
            }
            catch (IOException)
            {
                Check(state.Items.Select(i => i.Id).SequenceEqual(ids) && collection.ItemIds.SequenceEqual(members) && File.Exists(source), "failed URL registration retains downloaded media and rolls back record");
            }
            var oldUrl = image.SourceUrl;
            try
            {
                await window.RegisterDownloadedAsync(new(image.Path, new Uri("https://fixture.test/existing.jpg"), "existing.jpg"), collection.Id);
                throw new InvalidOperationException("accepted failed existing registration");
            }
            catch (IOException) { Check(image.SourceUrl == oldUrl && state.Items.Select(i => i.Id).SequenceEqual(ids), "failed duplicate registration restores source URL"); }
        }
        Check(window.AddItemsToPlaylist(collection, [video]), "add-to-collection retries after unlock");
        playlists.SelectedItem = collection; window.ResetLibraryFilters(); Select(image.Id);
        var order = collection.ItemIds.ToArray();
        using (Lock())
        {
            Check(!window.MoveSelectedItem(1) && collection.ItemIds.SequenceEqual(order), "failed move retains collection order");
        }
        Check(window.MoveSelectedItem(1) && store.Load().Playlists.Single(p => p.Id == collection.Id).ItemIds.SequenceEqual(order.Reverse()), "move commits after unlock");
        Check(window.MoveSelectedItem(-1), "restore collection order");
        var profile = new MonitorProfile { MonitorId = "fixture", PlaylistId = collection.Id };
        var schedule = new ScheduleRule { PlaylistId = collection.Id };
        state.Settings.Monitors.Add(profile); state.Settings.Schedules.Add(schedule); store.Save(state);
        using (Lock())
        {
            Check(!window.DeletePlaylistItem(collection) && state.Playlists.Select(p => p.Id).SequenceEqual(beforePlaylists) &&
                profile.PlaylistId == collection.Id && state.Settings.Schedules.Contains(schedule), "failed delete restores collection, monitor and schedule references");
        }
        Check(window.RenamePlaylistTo(collection, "Committed name") && store.Load().Playlists.Single(p => p.Id == collection.Id).Name == "Committed name", "rename retries and persists");
        var created = window.CreatePlaylist("Committed collection");
        Check(created != null && store.Load().Playlists.Any(p => p.Id == created.Id), "create retries and persists");
        Check(window.DeletePlaylistItem(collection) && profile.PlaylistId == Playlist.AllId && !state.Settings.Schedules.Contains(schedule) &&
            store.Load().Playlists.All(p => p.Id != collection.Id), "delete retries and persists reference cleanup");
        Check(state.Items.Select(i => i.Id).SequenceEqual(ids) && File.Exists(image.Path), "collection mutations retain wallpaper records and media");
        playlists.SelectedItem = created; window.ResetLibraryFilters();
        await window.Import([source]);
        var imported = state.Items.Single(i => i.OriginalPath == source);
        Check(imported.Path == source && created!.ItemIds.Contains(imported.Id) && store.Load().Items.Any(i => i.Id == imported.Id), "failed local import retries and commits into selected collection");
        var importedCount = state.Items.Count;
        await window.Import([source]);
        Check(state.Items.Count == importedCount && created!.ItemIds.Count(id => id == imported.Id) == 1, "local retry deduplicates records and membership");
        var engine = (WallpaperEngine)typeof(MainWindow).GetField("_engine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        C<TextBlock>("Notice").Text = "";
        using (Lock()) Check(!await engine.SaveCheckpointAsync(true) && HasError(), "background checkpoint failure reports honest error");
        Check(await engine.SaveCheckpointAsync(true) && store.Load().Items.Count == state.Items.Count, "background checkpoint retries after unlock");
        Check(!state.Settings.WasRunning, "checks do not apply desktop wallpaper");
    }
}