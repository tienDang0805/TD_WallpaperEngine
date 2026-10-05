using System.Text;

namespace TienDang.App;

public partial class MainWindow
{
    private void OpenDiagnostics(object sender, RoutedEventArgs e)
    {
        var dialog = new DiagnosticsWindow(CaptureDiagnosticTextAsync) { Owner = this };
        dialog.ShowDialog();
    }
    internal async Task<string> CaptureDiagnosticTextAsync()
    {
        var desktop = await _engine.CaptureDiagnosticsAsync();
        var preview = await InlinePreview.CaptureDiagnosticsAsync();
        var text = new StringBuilder();
        text.AppendLine(L.Text("Hình nền desktop"));
        if (desktop.Count == 0) text.AppendLine(L.Text("Chưa phát wallpaper"));
        foreach (var entry in desktop)
        {
            text.AppendLine(); text.AppendLine(entry.Display + " · " + entry.Name);
            if (entry.PauseReason.Length > 0) text.AppendLine(entry.PauseReason);
            var sample = entry.Snapshot;
            text.AppendLine(L.Text("Generation đang phát / đang tải") + $": {sample.ActiveGeneration} / {sample.PendingGeneration}");
            text.AppendLine(L.Text("Tiến trình host") + ": " + sample.WorkerProcessId);
            if (sample.Error != null) text.AppendLine(L.Text("Chưa lấy được số đo. Thử cập nhật lại."));
            if (sample.ActiveVideo != null) text.AppendLine(DiagnosticsWindow.FormatVideo(sample.ActiveVideo));
            else text.AppendLine(sample.ActiveKind == "image" ? L.Text("Đang hiển thị ảnh tĩnh") : sample.ActiveKind == "held-frame" ? L.Text("Đang giữ khung hình trước đó · Decoder đã đóng") : L.Text("Chưa có số đo"));
            if (sample.PendingVideo != null) { text.AppendLine(L.Text("Video đang tải")); text.AppendLine(DiagnosticsWindow.FormatVideo(sample.PendingVideo)); }
        }
        text.AppendLine(); text.AppendLine(L.Text("Video xem trước"));
        text.AppendLine(preview == null ? L.Text("Không có video xem trước") : DiagnosticsWindow.FormatVideo(preview));
        return text.ToString();
    }
}