using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TienDang.App;

public sealed class WallpaperPreview : UserControl, IDisposable
{
    private readonly Grid _stage = new() { Background = new SolidColorBrush(Color.FromRgb(231, 238, 250)) };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _empty = new() { Text = L.Text("Chọn một wallpaper trong thư viện\nđể xem ảnh hoặc phát video."), Foreground = new SolidColorBrush(Color.FromRgb(22, 52, 95)), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
    private readonly Button _play = new() { Content = L.Text("Phát"), MinWidth = 78, IsEnabled = false, Padding = new Thickness(10, 4, 10, 4), MinHeight = 30 };
    private readonly CheckBox _mute = new() { Content = L.Text("Tắt tiếng"), IsChecked = true, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Slider _seek = new() { Minimum = 0, Maximum = 1, IsEnabled = false, Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _time = new() { Text = "00:00 / 00:00", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _controls = new() { Margin = new Thickness(0, 9, 0, 0), Visibility = Visibility.Collapsed };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private MpvVideoSurface? _surface;
    private MpvPlayer? _player;
    private WallpaperItem? _item;
    private int _generation, _seekVersion;
    private bool _loaded, _paused = true, _pollBusy, _dragging, _disposed;
    private double _duration;
    public event Action<string>? StateChanged;
    internal event Action? MetadataChanged;
    internal int MediaWidth { get; private set; }
    internal int MediaHeight { get; private set; }
    internal double Duration => _duration;
    internal bool CompactControls { get; set; }
    private readonly Slider _volume = new() { Minimum = 0, Maximum = 100, Value = 30, Width = 60, Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    internal bool IsVideoReady => _loaded;
    internal bool HasVideoFrame { get; private set; }
    internal bool IsPaused => _paused;
    internal Task<PlayerDiagnostics?> CaptureDiagnosticsAsync() => _player == null ? Task.FromResult<PlayerDiagnostics?>(null) : CapturePreviewDiagnosticsAsync(_player);
    private static async Task<PlayerDiagnostics?> CapturePreviewDiagnosticsAsync(MpvPlayer player) => await player.CaptureDiagnosticsAsync();
    internal int PreviewProcessId => _player?.ProcessId ?? 0;
    internal string? SelectedId => _item?.Id;
    private string? _selectedPath;
    internal string? SelectedPath => _selectedPath;
    internal BitmapSource? ImageSource => _image.Source as BitmapSource;

    public WallpaperPreview()
    {
        _controls.Margin = new Thickness(10, 0, 10, 0); _controls.Height = 48;
        _play.Style = (Style)Application.Current.FindResource("DarkTransport");
        _play.Content = "▶"; _play.MinWidth = 38; _play.ToolTip = L.Text("Phát / Tạm dừng");
        _time.Foreground = Brushes.White; _time.FontSize = 11; _mute.Foreground = Brushes.White; _mute.FontSize = 10;
        _volume.ValueChanged += async (_, _) => await MuteAsync();
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _stage.Children.Add(_empty); _stage.Children.Add(_image); root.Children.Add(_stage);
        _controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _controls.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _controls.Children.Add(_play); Grid.SetColumn(_seek, 1); _controls.Children.Add(_seek);
        Grid.SetColumn(_time, 2); _controls.Children.Add(_time); Grid.SetColumn(_mute, 3); _controls.Children.Add(_mute);
        _controls.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); Grid.SetColumn(_volume, 4); _controls.Children.Add(_volume);
        var transport = new Border { Background = new SolidColorBrush(Color.FromRgb(25,59,82)), CornerRadius = new CornerRadius(0,0,12,12), Child = _controls };
        transport.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = _controls });
        Grid.SetRow(transport, 1); root.Children.Add(transport); Content = root;
        _play.Click += async (_, _) => await ToggleAsync();
        _mute.Checked += async (_, _) => await MuteAsync();
        _mute.Unchecked += async (_, _) => await MuteAsync();
        _seek.PreviewMouseLeftButtonDown += (_, _) => _dragging = true;
        _seek.PreviewMouseLeftButtonUp += async (_, _) => { _dragging = false; await SeekAsync(_seek.Value); };
        _seek.LostMouseCapture += (_, _) => _dragging = false;
        _seek.PreviewKeyUp += async (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Home or Key.End or Key.PageUp or Key.PageDown) await SeekAsync(_seek.Value);
        };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    internal void RefreshLanguage()
    {
        _mute.Content = L.Text("Tắt tiếng");
        _play.ToolTip = L.Text("Phát / Tạm dừng");
        if (_item == null) _empty.Text = L.Text("Chọn một wallpaper trong thư viện\nđể xem ảnh hoặc phát video.");
        else if (_empty.Visibility == Visibility.Visible) _empty.Text = L.Text("Không thể xem trước file này.\nKiểm tra file hoặc chọn wallpaper khác.");
        StateChanged?.Invoke(_item == null ? L.Text("Chọn một wallpaper") :
            _item.IsVideo ? (_paused ? L.Text("Video · Tạm dừng") : L.Text("Video · Đang phát")) : L.Text("Ảnh · Xem toàn bộ"));
    }
    internal async Task ShowItemAsync(WallpaperItem? item, bool remote = false)
    {
        if (_disposed) return;
        var generation = ++_generation;
        ReleasePlayer();
        MediaWidth = MediaHeight = 0; _duration = 0;
        _item = item; _selectedPath = item?.Path; _image.Source = null; _image.Visibility = Visibility.Collapsed;
        _empty.Visibility = Visibility.Visible; _controls.Visibility = Visibility.Collapsed;
        _empty.Text = L.Text("Chọn một wallpaper trong thư viện\nđể xem ảnh hoặc phát video.");
        _stage.Background = new SolidColorBrush(item == null ? Color.FromRgb(231, 238, 250) : Color.FromRgb(16, 42, 77));
        _empty.Foreground = item == null ? new SolidColorBrush(Color.FromRgb(22, 52, 95)) : Brushes.White;
        if (item == null) { StateChanged?.Invoke(L.Text("Chọn một wallpaper")); return; }
        if (!remote && !item.Exists) { Fail(L.Text("Không tìm thấy file."), generation); return; }
        try
        {
            if (!item.IsVideo)
            {
                StateChanged?.Invoke(L.Text("Ảnh · Đang mở…"));
                var bitmap = await Task.Run(() => ThumbnailService.LoadImage(item.Path, 0));
                if (generation != _generation || _disposed) return;
                MediaWidth = bitmap.PixelWidth; MediaHeight = bitmap.PixelHeight; MetadataChanged?.Invoke();
                _image.Source = bitmap; _image.Visibility = Visibility.Visible; _empty.Visibility = Visibility.Collapsed;
                StateChanged?.Invoke(L.Text("Ảnh · Xem toàn bộ")); return;
            }
            _controls.Visibility = CompactControls ? Visibility.Collapsed : Visibility.Visible;
            _empty.Visibility = Visibility.Collapsed;
            _time.Text = "00:00 / 00:00"; _seek.Value = 0; _duration = 0;
            _surface = new MpvVideoSurface();
            _stage.Children.Add(_surface); _surface.UpdateLayout();
            var player = new MpvPlayer(_surface.Handle); _player = player;
            _paused = true; _play.Content = "▶";
            StateChanged?.Invoke(L.Text("Video · Đang mở…"));
            player.Loaded += () => Dispatch(async () =>
            {
                if (generation != _generation) return;
                _loaded = true; _play.IsEnabled = true; _seek.IsEnabled = true;
                StateChanged?.Invoke(L.Text("Video · Tạm dừng"));
                _timer.Start(); await PollAsync();
                var parameters = await player.GetPropertyAsync("video-params");
                if (generation != _generation) return;
                if (parameters.TryGetProperty("dw", out var width)) MediaWidth = width.GetInt32();
                if (parameters.TryGetProperty("dh", out var height)) MediaHeight = height.GetInt32();
                MetadataChanged?.Invoke();
            });
            player.FrameReady += () => Dispatch(() =>
            {
                if (generation == _generation) HasVideoFrame = true;
                return Task.CompletedTask;
            });
            player.Ended += () => Dispatch(async () =>
            {
                if (generation != _generation) return;
                await SafeAsync(() => player.RestartAsync());
                if (!_paused) await SafeAsync(() => player.SetPausedAsync(false));
            });
            player.Failed += error => Dispatch(() => { Fail(error, generation); return Task.CompletedTask; });
            if (remote) await player.StartUrlAsync(new Uri(item.Path), _mute.IsChecked == true, (int)_volume.Value, "Fit", true);
            else await player.StartAsync(item.Path, _mute.IsChecked == true, (int)_volume.Value, "Fit", true);
            _ = CheckLoadAsync(generation);
        }
        catch (Exception ex) { Fail(ex.Message, generation); }
    }
    private async Task CheckLoadAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(25));
        if (!_disposed && generation == _generation && !_loaded) Fail(L.Text("Video mở quá lâu. Thử chọn lại hoặc kiểm tra file."), generation);
    }
    internal async Task ToggleAsync()
    {
        if (!_loaded || _player == null) return;
        var player = _player; var generation = _generation; var paused = !_paused;
        await SafeAsync(async () =>
        {
            await player.SetPausedAsync(paused);
            if (generation != _generation) return;
            _paused = paused; _play.Content = paused ? "▶" : "Ⅱ";
            StateChanged?.Invoke(paused ? L.Text("Video · Tạm dừng") : L.Text("Video · Đang phát"));
        });
    }
    internal async Task SeekAsync(double seconds)
    {
        if (_loaded && _player != null)
        {
            var player = _player; var generation = _generation;
            await SafeAsync(async () =>
            {
                if (_duration <= 0)
                {
                    var duration = await player.GetPropertyAsync("duration");
                    if (duration.TryGetDouble(out var d) && d > 0) _duration = d;
                }
                if (generation != _generation) return;
                var target = Math.Clamp(seconds, 0, _duration > 0 ? _duration : double.MaxValue);
                ++_seekVersion;
                await player.SeekAsync(target);
                if (generation != _generation) return;
                _seek.Value = target; _time.Text = Format(target) + " / " + Format(_duration);
            });
        }
    }
    internal async Task<double> PositionAsync() => _player == null ? 0 : (await _player.GetPropertyAsync("time-pos")).GetDouble();
    private async Task MuteAsync()
    {
        if (_loaded && _player != null)
        {
            var player = _player;
            await SafeAsync(() => player.SetOptionsAsync(_mute.IsChecked == true, (int)_volume.Value, "Fit"));
        }
    }
    private async Task PollAsync()
    {
        if (_pollBusy || !_loaded || _player == null) return;
        _pollBusy = true;
        var player = _player; var generation = _generation; var seekVersion = _seekVersion;
        try
        {
            var duration = await player.GetPropertyAsync("duration");
            var position = await player.GetPropertyAsync("time-pos");
            if (generation != _generation || seekVersion != _seekVersion) return;
            if (duration.TryGetDouble(out var d) && d > 0) { _duration = d; _seek.Maximum = d; }
            if (position.TryGetDouble(out var t)) { if (!_dragging && !_seek.IsKeyboardFocusWithin) _seek.Value = t; _time.Text = Format(t) + " / " + Format(_duration); }
        }
        catch (Exception ex) { if (generation == _generation) Fail(ex.Message, generation); }
        finally { _pollBusy = false; }
    }
    private static string Format(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss");
    private async Task SafeAsync(Func<Task> action)
    {
        var generation = _generation;
        try { await action(); } catch (Exception ex) { Fail(ex.Message, generation); }
    }
    private void Dispatch(Func<Task> action)
    {
        if (_disposed || Dispatcher.HasShutdownStarted) return;
        var dispatchGeneration = _generation;
        Dispatcher.BeginInvoke(new Action(async () =>
        {
            try { if (!_disposed) await action(); } catch (Exception ex) { if (!_disposed) Fail(ex.Message, dispatchGeneration); }
        }));
    }
    private void Fail(string message, int generation)
    {
        if (_disposed || generation != _generation) return;
        ReleasePlayer();
        _stage.Background = new SolidColorBrush(Color.FromRgb(231, 238, 250));
        _empty.Foreground = new SolidColorBrush(Color.FromRgb(22, 52, 95));
        _empty.Text = L.Text("Không thể xem trước file này.\nKiểm tra file hoặc chọn wallpaper khác.");
        _empty.ToolTip = message; _empty.Visibility = Visibility.Visible;
        StateChanged?.Invoke(L.Text("Không thể xem trước · ") + message);
    }
    private void ReleasePlayer()
    {
        _timer.Stop(); _loaded = false; HasVideoFrame = false; _play.IsEnabled = false; _seek.IsEnabled = false;
        _player?.Dispose(); _player = null;
        if (_surface != null) { _stage.Children.Remove(_surface); _surface.Dispose(); _surface = null; }
    }
    // HwndHost is drawn by a native GPU window. For a WPF render, use the
    // player's actual decoded frame while preserving its playback state.
    internal async Task RenderSnapshotAsync(Action render)
    {
        var player = _player; var surface = _surface; var generation = _generation;
        if (!_loaded || player == null || surface == null) { render(); return; }
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TienDang-preview-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            await player.ScreenshotAsync(path);
            if (generation != _generation) return;
            _image.Source = ThumbnailService.LoadImage(path, 1600);
            surface.Visibility = Visibility.Collapsed; _image.Visibility = Visibility.Visible;
            UpdateLayout(); render();
        }
        finally
        {
            if (generation == _generation) { surface.Visibility = Visibility.Visible; _image.Visibility = Visibility.Collapsed; _image.Source = null; }
            try { File.Delete(path); } catch (IOException) { }
        }
    }
    internal void Suspend()
    {
        ++_generation; ReleasePlayer(); _image.Source = null; _selectedPath = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        Suspend(); _disposed = true;
    }
}