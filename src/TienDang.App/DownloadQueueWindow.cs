namespace TienDang.App;
internal sealed class DownloadQueueWindow : Window
{
    private readonly DownloadQueue _queue;
    private readonly ListBox _list = new() { MinHeight = 240 };
    public DownloadQueueWindow(DownloadQueue queue)
    {
        _queue = queue; Style = (Style)Application.Current.FindResource(typeof(Window)); ShowInTaskbar = false; Title = L.Text("Hàng đợi tải") + " · TD-WallpaperEngine";
        Width = Math.Min(700, SystemParameters.WorkArea.Width); Height = Math.Min(540, SystemParameters.WorkArea.Height); MinWidth = 500; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(24) };
        var heading = new TextBlock { Text = L.Text("Hàng đợi tải"), FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(heading, Dock.Top); panel.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = L.Text("Hủy tải") }; cancel.Click += (_, _) => { if (_list.SelectedItem is DownloadJob job) queue.Cancel(job); };
        var retry = new Button { Content = L.Text("Thử lại") }; retry.Click += (_, _) => { if (_list.SelectedItem is DownloadJob job) queue.Retry(job); };
        var close = new Button { Content = L.Text("Đóng") }; close.Click += (_, _) => Close();
        buttons.Children.Add(cancel); buttons.Children.Add(retry); buttons.Children.Add(close); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        var hint = new TextBlock { Text = L.Text("Một file được tải mỗi lần. Đóng cửa sổ này vẫn tiếp tục tải; thoát app sẽ dừng tải. File tải xong tự thêm vào thư viện."), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
        DockPanel.SetDock(hint, Dock.Bottom); panel.Children.Add(hint); panel.Children.Add(_list); Content = panel;
        queue.Changed += Refresh; Closed += (_, _) => queue.Changed -= Refresh; Refresh();
    }
    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var selected = _list.SelectedItem; _list.ItemsSource = _queue.Jobs.ToArray(); _list.Items.Refresh(); _list.SelectedItem = selected;
    }
}