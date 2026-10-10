using System.Globalization;
using System.Text;

namespace TD_Wallpaper.App;

internal sealed class DiagnosticsWindow : Window
{
    private readonly TextBox _details = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 13, Padding = new Thickness(14) };
    private readonly Button _refresh = new() { Content = L.Text("Cập nhật số đo") };
    private readonly Func<Task<string>> _capture;
    private bool _closed;
    internal string Details => _details.Text;
    internal DiagnosticsWindow(Func<Task<string>> capture)
    {
        _capture = capture;
        Title = L.Text("Thông tin bộ phát"); Width = 760; Height = 650; MinWidth = 520; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        var root = new DockPanel { Margin = new Thickness(22) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        heading.Children.Add(new TextBlock { Text = Title, FontSize = 24, FontWeight = FontWeights.Bold });
        heading.Children.Add(new TextBlock { Text = L.Text("Số đo tại thời điểm cập nhật. Dùng khi hình nền bị giật hoặc cần kiểm tra hiệu năng."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var close = new Button { Content = L.Text("Đóng"), IsCancel = true }; close.Click += (_, _) => Close();
        _refresh.Click += async (_, _) => await RefreshAsync(); actions.Children.Add(_refresh); actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions); root.Children.Add(_details); Content = root;
        Closed += (_, _) => _closed = true;
        Loaded += async (_, _) => await RefreshAsync();
    }
    internal async Task RefreshAsync()
    {
        if (!_refresh.IsEnabled || _closed) return;
        _refresh.IsEnabled = false;
        try { var text = await _capture(); if (!_closed) _details.Text = text; }
        catch (Exception) { if (!_closed) _details.Text = L.Text("Chưa lấy được số đo. Thử cập nhật lại."); }
        finally { if (!_closed) _refresh.IsEnabled = true; }
    }
    internal static string FormatVideo(PlayerDiagnostics sample)
    {
        var text = new StringBuilder();
        string Value(object? value) => value switch { null => L.Text("Chưa có số đo"), double d => d.ToString("0.##", CultureInfo.InvariantCulture), _ => value.ToString()! };
        void Line(string label, object? value, string suffix = "") => text.AppendLine(L.Text(label) + ": " + Value(value) + (value == null ? "" : suffix));
        Line("Tiến trình video", sample.ProcessId);
        Line("Codec", sample.Codec);
        Line("Kích thước video", sample.Width is {} w && sample.Height is {} h ? $"{w} × {h}" : null);
        Line("FPS nguồn", sample.SourceFps); Line("FPS sau bộ lọc (ước tính)", sample.OutputFps);
        Line("Giới hạn FPS", sample.FrameRateLimit == 0 ? L.Text("Theo video gốc") : sample.FrameRateLimit);
        Line("Giải mã đang dùng", sample.DecodeMode == "hardware" ? L.Text("Phần cứng (GPU)") : sample.DecodeMode == "software" ? L.Text("Phần mềm (CPU)") : null);
        Line("Backend giải mã thực tế", sample.HwdecCurrent); Line("Chế độ giải mã yêu cầu", sample.RequestedHwdec); Line("Đầu ra video", sample.VideoOutput);
        if (sample.SoftwareFallback) text.AppendLine(L.Text("Video đang giải mã bằng CPU dù đã yêu cầu phần cứng. GPU có thể chưa hỗ trợ codec này."));
        if (sample.SoftwareFallback) text.AppendLine(L.Text("Để giảm tải, thử video H.264 8-bit ở 1080p hoặc dùng ảnh tĩnh."));
        if (sample.HwdecCurrent?.EndsWith("-copy", StringComparison.Ordinal) == true)
            text.AppendLine(L.Text("GPU đang giải mã nhưng frame được chép về RAM để xử lý. Giới hạn FPS không bảo đảm giảm CPU."));
        if (sample.Headless) text.AppendLine(L.Text("Mẫu kiểm tra không xuất hình; không dùng để chứng minh giải mã GPU."));
        Line("Đọc xong video", sample.FileLoadedMs, " ms"); Line("Frame đầu sẵn sàng", sample.FirstFrameMs, " ms");
        Line("Frame bỏ qua khi giải mã", sample.DecoderDroppedFrames); Line("Frame bỏ qua khi xuất hình", sample.OutputDroppedFrames); Line("Frame lệch nhịp", sample.MistimedFrames);
        Line("RAM riêng của video", sample.PrivateMiB, " MiB"); Line("RAM đang dùng của video", sample.WorkingSetMiB, " MiB"); Line("Handle của video", sample.HandleCount);
        Line("CPU video (tổng các nhân)", sample.CpuPercentAllCores, "%"); Line("Khoảng lấy mẫu CPU", sample.CpuSampleSeconds, " s");
        if (sample.CpuPercentAllCores == null) text.AppendLine(L.Text("Cập nhật lần nữa sau ít nhất 1 giây để đo CPU."));
        text.AppendLine(L.Text("Số đo này chỉ gồm tiến trình video; chưa bao gồm toàn app, GPU hoặc VRAM."));
        return text.ToString();
    }
}