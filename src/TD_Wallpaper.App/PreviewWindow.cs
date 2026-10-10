using System.Windows.Input;

namespace TD_Wallpaper.App;

internal sealed class PreviewWindow : Window
{
    internal WallpaperPreview Preview { get; } = new();
    public PreviewWindow(WallpaperItem item)
    {
        if (!item.Exists) throw new FileNotFoundException(L.Text("File không còn tồn tại."), item.Path);
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = L.Text("Xem trước · ") + item.Name;
        Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/TD_Wallpaper;component/Assets/app.ico"));
        Width = 1000; Height = 670; MinWidth = 640; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var close = new Button { Content = L.Text("Đóng · Esc"), Margin = new Thickness(14, 0, 0, 0) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = item.Name, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Navy"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = item.Path });
        root.Children.Add(header);
        Grid.SetRow(Preview, 1); root.Children.Add(Preview);
        var status = new TextBlock { Text = L.Text("Xem trước chưa thay đổi hình nền desktop."), FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 12, 0, 0) };
        Preview.StateChanged += text => status.Text = text + L.Text(" · Xem trước chưa áp dụng");
        Grid.SetRow(status, 2); root.Children.Add(status); Content = root;
        Loaded += async (_, _) => await Preview.ShowItemAsync(item);
        Closed += (_, _) => Preview.Dispose();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
}