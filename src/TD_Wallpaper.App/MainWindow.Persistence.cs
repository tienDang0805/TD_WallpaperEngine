namespace TD_Wallpaper.App;

public partial class MainWindow
{
    // Mutations are synchronous on the UI thread. Notify the player/view only after commit.
    private bool CommitMutation(Action change, Action rollback, bool libraryChanged = false)
    {
        change();
        if (!_engine.Save()) { rollback(); return false; }
        if (libraryChanged) _engine.LibraryChanged();
        return true;
    }

    internal Playlist? CreatePlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var playlist = new Playlist { Name = name.Trim() };
        if (!CommitMutation(() => _state.Playlists.Add(playlist), () => _state.Playlists.Remove(playlist))) return null;
        RefreshPlaylists(playlist.Id);
        return playlist;
    }
    internal bool RenamePlaylistTo(Playlist playlist, string name)
    {
        if (playlist.Id == Playlist.AllId || string.IsNullOrWhiteSpace(name)) return false;
        var previous = playlist.Name;
        if (!CommitMutation(() => playlist.Name = name.Trim(), () => playlist.Name = previous)) return false;
        RefreshPlaylists(playlist.Id);
        return true;
    }
    internal bool DeletePlaylistItem(Playlist playlist)
    {
        if (playlist.Id == Playlist.AllId) return false;
        var index = _state.Playlists.IndexOf(playlist);
        if (index < 0) return false;
        var profiles = _state.Settings.Monitors.Where(p => p.PlaylistId == playlist.Id).ToArray();
        var schedules = _state.Settings.Schedules.ToArray();
        if (!CommitMutation(() =>
        {
            _state.Playlists.Remove(playlist);
            foreach (var profile in profiles) profile.PlaylistId = Playlist.AllId;
            _state.Settings.Schedules.RemoveAll(r => r.PlaylistId == playlist.Id);
        }, () =>
        {
            _state.Playlists.Insert(index, playlist);
            foreach (var profile in profiles) profile.PlaylistId = playlist.Id;
            _state.Settings.Schedules.Clear(); _state.Settings.Schedules.AddRange(schedules);
        }, true)) return false;
        RefreshPlaylists(Playlist.AllId);
        ShowNotice(L.Text("Đã xóa bộ. Các file vẫn còn trong thư viện."));
        return true;
    }
    internal bool AddItemsToPlaylist(Playlist playlist, IReadOnlyList<WallpaperItem> items)
    {
        var added = items.Select(i => i.Id).Distinct().Where(id => !playlist.ItemIds.Contains(id)).ToArray();
        if (!CommitMutation(() => playlist.ItemIds.AddRange(added), () => playlist.ItemIds.RemoveAll(added.Contains), true)) return false;
        RefreshItems();
        ShowNotice(L.Text($"Đã thêm {items.Count} wallpaper vào {playlist.Name}."));
        return true;
    }
    internal bool SetItemsDuration(IReadOnlyList<WallpaperItem> items, int? seconds)
    {
        var before = items.Select(i => (Item: i, Value: i.DurationSeconds)).ToArray();
        if (!CommitMutation(() => { foreach (var item in items) item.DurationSeconds = seconds; },
            () => { foreach (var entry in before) entry.Item.DurationSeconds = entry.Value; })) return false;
        _cards.Clear(); RefreshItems();
        return true;
    }
    internal bool MoveSelectedItem(int step)
    {
        if (SelectedItems.Count != 1 || SelectedPlaylist.Id == Playlist.AllId) { ShowNotice(L.Text("Chọn một mục trong bộ riêng để sắp thứ tự.")); return false; }
        var ids = SelectedPlaylist.ItemIds;
        var index = ids.IndexOf(SelectedItems[0].Id); var target = index + step;
        if (index < 0 || target < 0 || target >= ids.Count) return false;
        void Swap() => (ids[index], ids[target]) = (ids[target], ids[index]);
        if (!CommitMutation(Swap, Swap, true)) return false;
        _cards.Clear(); RefreshItems();
        return true;
    }
    private Action CaptureItemOrder()
    {
        var items = _state.Items.ToArray();
        var memberships = _state.Playlists.Select(p => (Playlist: p, Ids: p.ItemIds.ToArray())).ToArray();
        return () =>
        {
            _state.Items.Clear(); _state.Items.AddRange(items);
            foreach (var entry in memberships) { entry.Playlist.ItemIds.Clear(); entry.Playlist.ItemIds.AddRange(entry.Ids); }
        };
    }
    internal bool SaveSchedules(List<ScheduleRule> schedules)
    {
        var previous = _state.Settings.Schedules;
        return CommitMutation(() => _state.Settings.Schedules = schedules, () => _state.Settings.Schedules = previous);
    }
    private async Task<bool> SaveRotationDraftAsync()
    {
        var s = _state.Settings;
        var previous = (s.IntervalSeconds, s.Shuffle, s.AdvanceAtVideoEnd);
        var reset = false;
        if (!CommitMutation(() => reset = CaptureSettings(), () =>
        {
            (s.IntervalSeconds, s.Shuffle, s.AdvanceAtVideoEnd) = previous;
        }))
        {
            UpdateRotationSaveState();
            return false;
        }
        UpdateRotationSaveState();
        await _engine.ApplySavedSettings(reset);
        return true;
    }
    internal async Task<bool> SavePreferencesAsync(AppSettings settings)
    {
        var s = _state.Settings;
        var previous = new AppSettings { PerformanceProfile = s.PerformanceProfile, ReleaseWhenBusy = s.ReleaseWhenBusy, AppRules = s.AppRules.Select(r => new AppRule { ProcessName = r.ProcessName, Action = r.Action }).ToList(), Fit = s.Fit, Muted = s.Muted, Volume = s.Volume,
            PauseFullscreen = s.PauseFullscreen, PauseMaximized = s.PauseMaximized, PauseOnBattery = s.PauseOnBattery,
            ResumeOnLaunch = s.ResumeOnLaunch, CopyOnImport = s.CopyOnImport, Language = s.Language, FrameRateLimit = s.FrameRateLimit };
        void Assign(AppSettings value)
        {
            s.PerformanceProfile = value.PerformanceProfile; s.ReleaseWhenBusy = value.ReleaseWhenBusy; s.AppRules = value.AppRules.Select(r => new AppRule { ProcessName = r.ProcessName, Action = r.Action }).ToList();
            s.Fit = value.Fit; s.Muted = value.Muted; s.Volume = value.Volume;
            s.PauseFullscreen = value.PauseFullscreen; s.PauseMaximized = value.PauseMaximized; s.PauseOnBattery = value.PauseOnBattery;
            s.ResumeOnLaunch = value.ResumeOnLaunch; s.CopyOnImport = value.CopyOnImport;
            s.Language = value.Language == "en" ? "en" : "vi"; s.FrameRateLimit = MpvPlayer.ValidFrameRate(value.FrameRateLimit);
        }
        if (!CommitMutation(() => Assign(settings), () => Assign(previous))) return false;
        L.SetLanguage(s.Language);
        RefreshLanguage();
        await _engine.ApplySavedSettings(false);
        _engine.RequestPolicyRefresh(0);
        return true;
    }
}