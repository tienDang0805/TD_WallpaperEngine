using System.Diagnostics;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace TienDang.App;

internal sealed class WallpaperEngine : IDisposable
{
    private sealed class Session(DisplayInfo display, MonitorProfile profile)
    {
        public DisplayInfo Display = display;
        public MonitorProfile Profile = profile;
        public Rotation Rotation = new();
        public PlaybackClock Clock = new();
        public IPlayerHost? Host;
        public string PlaylistId = "";
        public WallpaperItem? Item;
        public WallpaperItem? Requested;
        public WallpaperItem? RetryItem;
        public bool RetryPinned;
        public string HoldingReason = "Bộ không có file hợp lệ · Giữ nền trước đó";
        public int ActiveGeneration;
        public int HostEpoch;
        public bool RequestedPinned;
        public bool Holding;
        public bool NeedsNext;
        public double RequestStarted;
        public double RetryAt;
        public double StableSince;
        public double? RestoreRemaining;
        public Task Retired = Task.CompletedTask;
        public CancellationTokenSource Lifetime = new();
        public bool SuspendedByPolicy;
        public bool Paused;
        public bool Loaded;
        public bool HasFrame;
        public bool Switching;
        public bool Pinned;
        public bool Disposed;
        public int Generation;
        public int RestartCount;
        public bool Fatal;
        public string PauseReason = "";
    }

    private readonly LibraryState _state;
    private readonly StateStore _store;
    private readonly PlaybackRuntime _runtime;
    private double Now => _runtime.Seconds?.Invoke() ?? _elapsed.Elapsed.TotalSeconds;
    private readonly Dictionary<string, Session> _sessions = [];
    private readonly HashSet<string> _failed = [];
    private readonly HashSet<string> _softwareFallbackNotices = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private double _lastTick;
    private double _lastSave;
    private bool _checkpointDirty;
    private Task<bool>? _checkpointTask;
    private bool _tickBusy;
    private bool _locked;
    private bool _displayOff;
    private bool _disposed;
    private WindowActivityWatcher? _activity;
    private bool _policyBusy, _policyPending;
    private uint _policyEventTime;
    internal readonly List<(string Display, bool Paused, uint EventToAckMs)> PolicyEvidence = [];
    public bool ManualPaused { get; private set; }
    public bool Running => _sessions.Count > 0;
    public bool Pinned => _sessions.Values.Any(s => s.Pinned);
    public event Action? Changed;
    public event Action<string>? Problem;
    public event Action? DisplaysChanged;
    public string Status { get; private set; } = L.Text("Chưa phát · Chọn bộ wallpaper rồi bấm Áp dụng");
    public string CurrentNames => string.Join(" · ", _sessions.Values.Where(s => s.Item != null).Select(s => s.Item!.Name).Distinct());

    public WallpaperEngine(LibraryState state, StateStore store, PlaybackRuntime? runtime = null)
    {
        _state = state;
        _store = store;
        _runtime = runtime ?? new();
        if (!_runtime.Automatic) return;
        try { _activity = new(Application.Current.Dispatcher, RequestPolicyRefresh); }
        catch (Exception ex) { _store.Log("Window hook unavailable; 500ms fallback remains: " + ex.Message); }
        _timer.Tick += async (_, _) => await Tick();
        _timer.Start();
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
    }

    private void SessionChanged(object sender, SessionSwitchEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.RemoteConnect) SetSessionLocked(true);
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.RemoteDisconnect) SetSessionLocked(false);
            RequestPolicyRefresh(0);
        });
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (e.Mode == PowerModes.Suspend) _displayOff = true;
            if (e.Mode == PowerModes.Resume) { _displayOff = false; _lastTick = Now; }
            RequestPolicyRefresh(0);
        });
    }
    internal void SetSessionLocked(bool locked) { _locked = locked; RequestPolicyRefresh(0); }
    public void DisplayPower(bool off) { _displayOff = off; RequestPolicyRefresh(0); }
    private void DisplayChanged(object? sender, EventArgs e) =>
        Application.Current.Dispatcher.BeginInvoke(async () => await RefreshDisplaysAsync());
    internal async Task RefreshDisplaysAsync()
    {
        if (_disposed) return;
        var displays = _runtime.Displays();
        foreach (var id in _sessions.Keys.ToArray())
        {
            var session = _sessions[id];
            var found = displays.Find(d => d.Id == id);
            if (found == null) StopSession(session);
            else if (session.Display != found)
            {
                session.Display = found;
                session.RetryItem = session.Requested ?? session.Item;
                session.RetryPinned = session.Requested != null ? session.RequestedPinned : session.Pinned;
                if (session.Requested == null && session.Clock.RemainingSeconds > 0) session.RestoreRemaining = session.Clock.RemainingSeconds;
                ++session.Generation;
                CloseHost(session); session.Requested = null; session.Loaded = false;
                session.RetryAt = 0;
            }
        }
        if (_state.Settings.WasRunning)
        {
            foreach (var display in displays.Where(d => !_sessions.ContainsKey(d.Id)))
            {
                var profile = _state.Settings.Monitors.Find(p => p.MonitorId == display.Id);
                if (profile == null) continue;
                var restored = new Session(display, profile) { PlaylistId = _state.ResolvePlaylist(DateTime.Now, profile) };
                _sessions.Add(display.Id, restored);
                var last = Available(restored).FirstOrDefault(i => i.Id == profile.LastItemId);
                if (last != null)
                {
                    restored.Rotation.SetCurrent(last.Id);
                    if (profile.RemainingSeconds > 0) restored.RestoreRemaining = profile.RemainingSeconds;
                    await Switch(restored, last);
                }
                else await Next(restored);
            }
        }
        DisplaysChanged?.Invoke();
        await Tick();
    }
    public async Task Apply(string targetId, string playlistId, string? selectedItemId = null)
    {
        _failed.Clear();
        ManualPaused = false;
        foreach (var display in _runtime.Displays().Where(d => targetId == "*" || d.Id == targetId))
        {
            var profile = _state.Settings.Monitors.Find(p => p.MonitorId == display.Id);
            if (profile == null) { profile = new() { MonitorId = display.Id }; _state.Settings.Monitors.Add(profile); }
            profile.PlaylistId = playlistId;
            if (!_sessions.TryGetValue(display.Id, out var session))
            {
                session = new(display, profile);
                _sessions.Add(display.Id, session);
            }
            session.Pinned = false;
            ResetRecovery(session);
            session.PlaylistId = _state.ResolvePlaylist(DateTime.Now, profile);
            var first = session.Rotation.Start(Available(session), _state.Settings.Shuffle, selectedItemId ?? session.Item?.Id);
            if (first != null)
            {
                if (session.Requested == null && session.Item?.Id == first.Id && session.Host != null && session.Loaded)
                    session.Clock.Reset(first.DurationSeconds ?? _state.Settings.IntervalSeconds);
                else await Switch(session, first);
            }
            else await Next(session);
        }
        _state.Settings.WasRunning = Running;
        if (!Save()) throw new IOException(L.Text("Setting chưa được lưu. Kiểm tra quyền ghi thư mục dữ liệu."));
        if (_sessions.Values.Any(s => s.Fatal)) throw new InvalidOperationException(L.Text("Player chưa khởi động được. Xem thông báo lỗi hoặc app.log."));
        UpdateStatus();
    }

    public async Task Restore()
    {
        if (!_state.Settings.WasRunning || !_state.Settings.ResumeOnLaunch) return;
        foreach (var display in _runtime.Displays())
        {
            var profile = _state.Settings.Monitors.Find(p => p.MonitorId == display.Id);
            if (profile == null) continue;
            var session = new Session(display, profile) { PlaylistId = _state.ResolvePlaylist(DateTime.Now, profile) };
            _sessions.Add(display.Id, session);
            var last = Available(session).FirstOrDefault(i => i.Id == profile.LastItemId);
            if (last != null)
            {
                session.Rotation.SetCurrent(last.Id);
                if (profile.RemainingSeconds > 0) session.RestoreRemaining = profile.RemainingSeconds;
                await Switch(session, last);
            }
            else await Next(session);
        }
        UpdateStatus();
    }

    public async Task Pin(WallpaperItem item, string targetId)
    {
        if (!item.Exists) throw new FileNotFoundException(L.Text("File wallpaper không còn tồn tại."), item.Path);
        ManualPaused = false;
        _failed.Remove(item.Id);
        var profiles = _runtime.Displays().Where(d => targetId == "*" || d.Id == targetId).ToList();
        foreach (var display in profiles)
        {
            var profile = _state.Settings.Monitors.Find(p => p.MonitorId == display.Id);
            if (profile == null) { profile = new() { MonitorId = display.Id }; _state.Settings.Monitors.Add(profile); }
            if (!_sessions.TryGetValue(display.Id, out var session)) { session = new(display, profile); _sessions.Add(display.Id, session); }
            ResetRecovery(session); await Switch(session, item, true);
        }
        _state.Settings.WasRunning = Running;
        Save();
        UpdateStatus();
    }

    public void Unpin()
    {
        foreach (var session in _sessions.Values) { session.Pinned = false; session.RequestedPinned = false; session.RetryPinned = false; }
        UpdateStatus();
    }
    public async Task NextAll()
    {
        foreach (var session in _sessions.Values.ToArray()) { session.Pinned = false; await Next(session); }
        UpdateStatus();
    }
    public async Task PreviousAll()
    {
        foreach (var session in _sessions.Values.ToArray())
        {
            session.Pinned = false;
            var previous = session.Rotation.Previous(Available(session));
            if (previous != null) await Switch(session, previous);
        }
        UpdateStatus();
    }
    public void TogglePause() { ManualPaused = !ManualPaused; RequestPolicyRefresh(0); UpdateStatus(); }

    public async Task SettingsChanged(bool resetOrder)
    {
        if (!Save()) throw new IOException(L.Text("Setting chưa được lưu. Kiểm tra quyền ghi thư mục dữ liệu."));
        await ApplySavedSettings(resetOrder);
    }
    public async Task ApplySavedSettings(bool resetOrder)
    {
        if (resetOrder) foreach (var s in _sessions.Values) s.Rotation.Start(Available(s), _state.Settings.Shuffle, s.Item?.Id);
        foreach (var s in _sessions.Values.ToArray())
        {
            if (s.Host != null) await Send(s, Options("options"));
            s.Clock.Reset(s.Item?.DurationSeconds ?? _state.Settings.IntervalSeconds);
        }

    }

    internal void ReportSoftwareFallback(string itemId, string name, PlayerDiagnostics sample)
    {
        if (!sample.SoftwareFallback || !_softwareFallbackNotices.Add(itemId)) return;
        Problem?.Invoke(L.Text("Video đang giải mã bằng CPU. Nếu bị giật, thử bản H.264 8-bit ở 1080p hoặc ảnh tĩnh: ") + name);
    }

    public void LibraryChanged()
    {
        _softwareFallbackNotices.Clear();
        _failed.Clear();
        // The committed item stays truthful until a replacement commits (or Stop).
        foreach (var session in _sessions.Values) { session.Holding = false; session.NeedsNext = true; }
    }

    private IReadOnlyList<WallpaperItem> Available(Session session) =>
        _state.GetItems(session.PlaylistId).Where(i => i.Exists && !_failed.Contains(i.Id)).ToList();

    private async Task Next(Session session)
    {
        if (session.Disposed || session.Switching) return;
        var item = session.Rotation.Next(Available(session), _state.Settings.Shuffle);
        if (item == null)
        {
            session.Requested = null;
            session.RequestedPinned = false;
            session.Pinned = false;
            session.Generation++;
            if (session.Host != null && session.HasFrame)
            {
                session.Holding = true; session.Loaded = false; session.HoldingReason = "Bộ không có file hợp lệ · Giữ nền trước đó";
                await Send(session, Options("cancel"));
                await Send(session, Options("pause"));
                session.Paused = true;
                session.PauseReason = L.Text("Bộ không có file hợp lệ · Giữ nền trước đó");
            }
            else { CloseHost(session); session.Item = null; session.Loaded = false; }
            UpdateStatus();
            return;
        }
        session.Holding = false;
        await Switch(session, item);
    }

    private PlayerMessage Options(string command) => new()
    {
        Command = command, Muted = _state.Settings.Muted, Volume = _state.Settings.Volume, Fit = _state.Settings.Fit, FrameRateLimit = _state.Settings.FrameRateLimit, Language = _state.Settings.Language
    };

    private static void ResetRecovery(Session session)
    {
        session.Fatal = false; session.RestartCount = 0; session.RetryAt = 0;
        session.Holding = false; session.RetryItem = null; session.RetryPinned = false; session.NeedsNext = false;
    }

    private void CloseHost(Session session)
    {
        ++session.HostEpoch; // Invalidate callbacks before closing the pipe.
        var host = session.Host; session.Host = null; session.HasFrame = false;
        if (host != null) { host.Dispose(); session.Retired = session.Retired.IsCompletedSuccessfully ? host.ExitCompletion : Task.WhenAll(session.Retired, host.ExitCompletion); }
    }

    private void ScheduleRecovery(Session session, string error)
    {
        if (session.Requested == null && session.Item != null && session.Clock.RemainingSeconds > 0) session.RestoreRemaining = session.Clock.RemainingSeconds;
        session.RetryItem = session.Requested ?? session.Item;
        session.RetryPinned = session.Requested != null ? session.RequestedPinned : session.Pinned;
        ++session.Generation; // Invalidate the failed transport operation as well as its callbacks.
        CloseHost(session);
        session.Requested = null; session.Loaded = false;
        session.RestartCount++;
        session.Fatal = session.RestartCount >= 3;
        session.RetryAt = Now + Math.Pow(2, session.RestartCount - 1);
        _store.Log($"Player retry {session.RestartCount} ({session.Display.Id}): {error}");
        if (session.Fatal) Problem?.Invoke(L.Text("Player đã dừng. Bấm Áp dụng để thử lại. ") + error);
    }

    private async Task Switch(Session session, WallpaperItem item, bool pinned = false)
    {
        if (session.Disposed || session.Switching || _disposed) return;
        session.Switching = true;
        var generation = ++session.Generation;
        session.Requested = item;
        session.RequestedPinned = pinned;
        session.RequestStarted = Now;
        session.Holding = false;
        try
        {
            session.PauseReason = PauseReason(session);
            session.Paused = session.PauseReason.Length > 0;
            if (ShouldRelease(session)) { await SuspendForPolicy(session); return; }
            if (session.Host == null)
            {
                await session.Retired.WaitAsync(session.Lifetime.Token);
                var epoch = ++session.HostEpoch;
                var host = await _runtime.StartHost(session.Display.Id, message =>
                    Application.Current.Dispatcher.BeginInvoke(async () =>
                    {
                        if (epoch == session.HostEpoch) await OnMessage(session, message);
                    }), session.Lifetime.Token);
                if (session.Disposed || _disposed || epoch != session.HostEpoch) { host.Dispose(); return; }
                session.Host = host;
            }
            await session.Host.Send(Options(session.Paused ? "pause" : "play"), session.Lifetime.Token);
            var load = Options("load");
            load.Path = item.Path; load.IsVideo = item.IsVideo; load.Generation = generation;
            await session.Host.Send(load, session.Lifetime.Token);
        }
        catch (DesktopBusyException ex)
        {
            if (!session.Disposed && !_disposed && generation == session.Generation)
            {
                session.Requested = null; session.RequestedPinned = false;
                session.Loaded = false; session.Fatal = true;
                Problem?.Invoke(ex.Message);
            }
        }
        catch (Exception ex)
        {
            if (!session.Disposed && !_disposed && generation == session.Generation)
                ScheduleRecovery(session, ex.Message);
        }
        finally { session.Switching = false; UpdateStatus(); }
    }

    private async Task OnMessage(Session session, PlayerMessage message)
    {
        if (session.Disposed || _disposed) return;
        switch (message.Command)
        {
            case "diagnostics":
                if (message.Generation == session.ActiveGeneration && message.Diagnostics != null)
                {
                    _store.Log("Playback diagnostics (" + session.Display.Id + "): " + System.Text.Json.JsonSerializer.Serialize(message.Diagnostics));
                    if (session.Item != null && message.Diagnostics.Error == null && message.Diagnostics.ActiveVideo is {} sample)
                        ReportSoftwareFallback(session.Item.Id, session.Item.Name, sample);
                }
                return;
            case "loaded":
                if (message.Generation != session.Generation || session.Requested == null) return;
                session.Item = session.Requested; session.Requested = null; session.RetryItem = null;
                session.ActiveGeneration = message.Generation; session.Loaded = true; session.HasFrame = true;
                session.Pinned = session.RequestedPinned; session.RequestedPinned = false; session.RetryPinned = session.Pinned;
                session.Profile.LastItemId = session.Item.Id;
                session.Clock.Reset(session.RestoreRemaining ?? session.Item.DurationSeconds ?? _state.Settings.IntervalSeconds);
                session.RestoreRemaining = null;
                session.Profile.RemainingSeconds = session.Clock.RemainingSeconds;
                session.StableSince = Now; _checkpointDirty = true;
                break;
            case "ended":
                if (message.Generation == session.ActiveGeneration && session.Requested == null && !session.Holding &&
                    !session.Paused && !session.Pinned && _state.Settings.AdvanceAtVideoEnd) await Next(session);
                break;
            case "failed":
                if (message.Generation != session.Generation || session.Requested == null)
                {
                    if (message.Generation != session.ActiveGeneration || session.Requested != null) return;
                    if (session.Item != null) _failed.Add(session.Item.Id);
                    session.Loaded = false;
                }
                else
                {
                    var rejected = session.Requested;
                    _failed.Add(rejected.Id); session.Requested = null; session.RequestedPinned = false;
                    _store.Log($"Media failed: {rejected.Path}: {message.Error}");
                    Problem?.Invoke(L.Text("Bỏ qua file lỗi: ") + rejected.Name + ". " + message.Error);
                }
                session.Pinned = false; session.NeedsNext = true;
                if (!session.Switching) { session.NeedsNext = false; await Next(session); }
                break;
            case "transition-failed":
                if (message.Generation != session.Generation || session.Requested == null) return;
                session.Requested = null; session.RequestedPinned = false;
                session.Holding = true; session.Loaded = false; session.HoldingReason = "Chưa đổi được nền · Bấm Áp dụng để thử lại";
                session.PauseReason = L.Text("Chưa đổi được nền · Bấm Áp dụng để thử lại");
                Problem?.Invoke(session.PauseReason + ". " + message.Error);
                await Send(session, Options("pause")); session.Paused = true;
                break;
            case "decoder-crashed":
                if (message.Generation != session.Generation && message.Generation != session.ActiveGeneration) return;
                if (session.Host != null) ScheduleRecovery(session, message.Error ?? "Decoder crashed.");
                break;
            case "fatal":
            case "disconnected":
                // The captured host epoch has already rejected obsolete workers.
                if (session.Host == null) return;
                ScheduleRecovery(session, message.Error ?? message.Command);
                break;
        }
        UpdateStatus();
    }

    private string PauseReason(Session session)
    {
        if (ManualPaused) return L.Text("Tạm dừng thủ công");
        if (session.Holding) return L.Text(session.HoldingReason);
        if (_locked || _runtime.Remote()) return L.Text("Khóa máy / Remote Desktop");
        if (_displayOff) return L.Text("Màn hình tắt / Sleep");
        if (_state.Settings.PauseOnBattery && _runtime.Battery()) return L.Text("Đang dùng pin");
        if (RuleAction(session) != null) return L.Text("Quy tắc ứng dụng") + " · " + _runtime.ForegroundProcess(session.Display);
        if (_state.Settings.PauseFullscreen && _runtime.Fullscreen(session.Display)) return L.Text("Ứng dụng toàn màn hình");
        if (_state.Settings.PauseMaximized && _runtime.Maximized(session.Display)) return L.Text("Ứng dụng đang phóng to cửa sổ");
        return "";
    }

    private string? RuleAction(Session session)
    {
        if (_state.Settings.AppRules.Count == 0) return null;
        var process = _runtime.ForegroundProcess(session.Display);
        return _state.Settings.AppRules.FirstOrDefault(r => string.Equals(r.ProcessName, process, StringComparison.OrdinalIgnoreCase) || string.Equals(r.ProcessName + ".exe", process, StringComparison.OrdinalIgnoreCase))?.Action;
    }
    private bool ShouldRelease(Session session) => !ManualPaused && !session.Holding && PauseReason(session).Length > 0 && (_state.Settings.ReleaseWhenBusy || RuleAction(session) == "Release");
    private async Task SuspendForPolicy(Session session)
    {
        if (session.SuspendedByPolicy && session.Host == null) return;
        if (session.Requested == null && session.Item != null) session.RestoreRemaining = session.Clock.RemainingSeconds;
        session.RetryItem = session.Requested ?? session.RetryItem ?? session.Item;
        session.RetryPinned = session.Requested != null ? session.RequestedPinned : session.Pinned;
        ++session.Generation; session.Requested = null; session.Loaded = false; session.SuspendedByPolicy = true;
        CloseHost(session); await session.Retired.WaitAsync(session.Lifetime.Token);
        session.Paused = true; session.PauseReason = L.Text("Đã ngừng bộ phát để tiết kiệm bộ nhớ") + " · " + PauseReason(session);
    }
    private async Task ResumePolicy(Session session)
    {
        session.SuspendedByPolicy = false;
        var item = session.RetryItem ?? session.Item;
        if (item?.Exists == true) await Switch(session, item, session.RetryPinned || session.Pinned);
        else await Next(session);
    }
    internal void RequestPolicyRefresh(uint eventTime)
    {
        if (_disposed) return;
        _policyPending = true; _policyEventTime = eventTime;
        if (!_policyBusy) _ = RefreshPoliciesAsync();
    }
    private async Task RefreshPoliciesAsync()
    {
        _policyBusy = true;
        try
        {
            while (_policyPending && !_disposed)
            {
                _policyPending = false;
                var eventTime = _policyEventTime;
                foreach (var session in _sessions.Values.ToArray())
                {
                    if (_disposed || session.Disposed) continue;
                    var reason = PauseReason(session); var paused = reason.Length > 0;
                    session.PauseReason = reason;
                    if (ShouldRelease(session)) { await SuspendForPolicy(session); continue; }
                    if (session.SuspendedByPolicy && !paused) { await ResumePolicy(session); continue; }
                    if (session.Paused == paused) continue;
                    session.Paused = paused;
                    if (!paused) _lastTick = Now; // Never charge paused time after resume.
                    if (session.Host != null) await Send(session, Options(paused ? "pause" : "play"));
                    if (eventTime != 0)
                    {
                        PolicyEvidence.Add((session.Display.Id, paused, unchecked((uint)Environment.TickCount - eventTime)));
                        if (PolicyEvidence.Count > 64) PolicyEvidence.RemoveAt(0);
                    }
                }
                UpdateStatus();
            }
        }
        catch (Exception ex) { if (!_disposed) _store.Log("Pause policy failed: " + ex); }
        finally { _policyBusy = false; }
    }

    internal async Task Tick()
    {
        if (_tickBusy || _disposed) return;
        _tickBusy = true;
        try
        {
            var nowElapsed = Now;
            var delta = nowElapsed - _lastTick;
            _lastTick = nowElapsed;
            if (delta > 10) delta = 0; // sleep/resume: no burst of missed rotations
            foreach (var session in _sessions.Values.ToArray())
            {
                if (session.Disposed || session.Switching || session.Fatal) continue;
                if (session.NeedsNext) { session.NeedsNext = false; await Next(session); continue; }
                if (session.Requested != null && (!session.Requested.Exists || !_state.Items.Any(i => i.Id == session.Requested.Id)))
                {
                    ++session.Generation; session.Requested = null;
                    await Send(session, Options("cancel"));
                    await Next(session); continue;
                }
                if (session.Requested != null && nowElapsed - session.RequestStarted >= 35)
                {
                    ScheduleRecovery(session, "Load acknowledgement deadline exceeded."); continue;
                }
                if (session.RestartCount > 0 && session.Loaded && nowElapsed - session.StableSince >= 30)
                    session.RestartCount = 0;
                var reason = PauseReason(session);
                var paused = reason.Length > 0;
                if (ShouldRelease(session)) { await SuspendForPolicy(session); continue; }
                if (session.SuspendedByPolicy && !paused) { await ResumePolicy(session); continue; }
                if (paused != session.Paused)
                {
                    session.Paused = paused;
                    if (session.Host != null) await Send(session, Options(paused ? "pause" : "play"));
                }
                session.PauseReason = reason;
                if (paused) continue;
                var playlist = _state.ResolvePlaylist(DateTime.Now, session.Profile);
                if (!session.Pinned && session.PlaylistId != playlist)
                {
                    session.PlaylistId = playlist;
                    session.Rotation.ResetCycle();
                    await Next(session);
                    continue;
                }
                if (session.Host == null && nowElapsed < session.RetryAt) continue;
                if (session.Requested != null) continue;
                var retry = session.RetryItem ?? session.Item;
                if (session.Host == null && retry != null && retry.Exists && _state.Items.Any(i => i.Id == retry.Id) && !_failed.Contains(retry.Id))
                { await Switch(session, retry, session.RetryPinned || session.Pinned); continue; }
                if (session.Holding)
                {
                    if (Available(session).Count > 0) { session.Holding = false; await Next(session); }
                    continue;
                }
                if (session.Item == null || !session.Loaded) { await Next(session); continue; }
                if (!session.Item.Exists || !_state.Items.Any(i => i.Id == session.Item.Id) || (!session.Pinned && !_state.GetItems(session.PlaylistId).Any(i => i.Id == session.Item.Id)) || _failed.Contains(session.Item.Id)) { session.Pinned = false; await Next(session); continue; }
                if (session.Loaded && !session.Pinned && session.Clock.Advance(Math.Min(delta, Math.Max(0, nowElapsed - session.StableSince)), false)) await Next(session);
                if (session.Profile.RemainingSeconds != session.Clock.RemainingSeconds) _checkpointDirty = true;
                session.Profile.RemainingSeconds = session.Clock.RemainingSeconds;
            }
            if (Running && nowElapsed - _lastSave >= 15) { _lastSave = nowElapsed; await SaveCheckpointAsync(); }
            UpdateStatus();
        }
        catch (Exception ex) { _store.Log(ex.ToString()); Problem?.Invoke(ex.Message); }
        finally { _tickBusy = false; }
    }

    private async Task<bool> Send(Session session, PlayerMessage message)
    {
        var host = session.Host; var epoch = session.HostEpoch;
        if (host == null) return false;
        try { await host.Send(message, session.Lifetime.Token); return true; }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // Disconnect and a canceled command can report the same failure.
            if (!session.Disposed && !_disposed && host == session.Host && epoch == session.HostEpoch)
                ScheduleRecovery(session, ex.Message);
            return false;
        }
    }
    internal async Task<IReadOnlyList<(string Display, string Name, string PauseReason, PlaybackDiagnostics Snapshot)>> CaptureDiagnosticsAsync()
    {
        var sessions = _sessions.Values.ToArray();
        return await Task.WhenAll(sessions.Select(async session =>
        {
            var host = session.Host;
            var name = session.Item?.Name ?? ""; var pause = session.PauseReason;
            PlaybackDiagnostics snapshot;
            try { snapshot = host == null ? new() : await host.CaptureDiagnosticsAsync(); }
            catch (Exception ex) { snapshot = new() { Error = ex.Message }; }
            if (session.Disposed || session.Host != host) snapshot = snapshot with { Error = "Playback changed while sampling; refresh to sample the current player." };
            return (session.Display.Label, name, pause, snapshot);
        }));
    }
    internal IReadOnlyList<ControllerSnapshot> ControllerState => _sessions.Values.Select(s => new ControllerSnapshot(
        s.Display.Id, s.Item?.Id, s.Requested?.Id, s.ActiveGeneration, s.Generation, s.Loaded, s.Pinned,
        s.Paused, s.Holding, s.Fatal, s.RestartCount, s.RetryAt, s.Clock.RemainingSeconds)).ToArray();
    internal void RefreshLanguage() => UpdateStatus();
    private void UpdateStatus()
    {
        Status = !Running ? L.Text("Đã dừng · Wallpaper Windows hiện lại")
            : string.Join("  |  ", _sessions.Values.Select(s =>
                $"{L.Text(s.Display.Label).Split('·')[0].Trim()}: " +
                (s.Fatal ? L.Text("Lỗi player · Bấm Áp dụng") : s.Requested != null ? L.Text("Đang tải…") : s.Item == null ? L.Text("Bộ trống / không có file hợp lệ") :
                s.PauseReason.Length > 0 ? s.PauseReason : s.Pinned ? L.Text("Đang ghim") : !s.Loaded ? L.Text("Đang tải…") :
                L.Text($"Đổi sau {TimeSpan.FromSeconds(Math.Ceiling(s.Clock.RemainingSeconds)):g}"))));
        Changed?.Invoke();
    }

    private void StopSession(Session session)
    {
        session.Profile.RemainingSeconds = session.Clock.RemainingSeconds;
        session.Disposed = true; session.Lifetime.Cancel();
        CloseHost(session); session.Requested = null; session.Loaded = false;
        _sessions.Remove(session.Display.Id);
    }
    public void Stop()
    {
        foreach (var session in _sessions.Values.ToArray()) StopSession(session);
        _state.Settings.WasRunning = false;
        Save();
        UpdateStatus();
    }
    public bool Save()
    {
        try { _store.Save(_state); _checkpointDirty = false; return true; }
        catch (Exception ex) { _store.Log(ex.ToString()); Problem?.Invoke(L.Text("Không lưu được thư viện/setting: ") + ex.Message); return false; }
    }
    internal Task<bool> SaveCheckpointAsync(bool force = false)
    {
        if (_checkpointTask is { IsCompleted: false }) return _checkpointTask;
        if (_disposed || !force && !_checkpointDirty) return Task.FromResult(true);
        var revision = _store.Revision;
        var snapshot = _state.CreateSnapshot();
        _checkpointDirty = false;
        return _checkpointTask = WriteCheckpointAsync(snapshot, revision);
    }
    private async Task<bool> WriteCheckpointAsync(LibraryState snapshot, long revision)
    {
        try
        {
            // A later explicit commit invalidates this checkpoint before it can overwrite new data.
            await Task.Run(() => _store.TrySaveSnapshot(snapshot, revision)); return true;
        }
        catch (Exception ex)
        {
            _checkpointDirty = true; _store.Log(ex.ToString());
            if (!_disposed) Problem?.Invoke(L.Text("Không lưu được thư viện/setting: ") + ex.Message);
            return false;
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop(); _activity?.Dispose(); _activity = null;
        // Preserve running state so the next launch can resume.
        _state.Settings.WasRunning = Running;
        foreach (var s in _sessions.Values.ToArray()) StopSession(s);
        Save();
        SystemEvents.SessionSwitch -= SessionChanged;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        SystemEvents.PowerModeChanged -= PowerChanged;
    }
}
