using System.Globalization;
using System.Text.Json;

namespace TD_Wallpaper.App;

internal sealed class ScheduleWindow : Window
{
    private readonly List<ScheduleRule> _rules;
    private readonly ListBox _list = new() { DisplayMemberPath = "Name", MinHeight = 220 };
    private readonly TextBox _name = new();
    private readonly TextBox _start = new() { Text = "07:00" };
    private readonly TextBox _end = new() { Text = "18:00" };
    private readonly ComboBox _playlist = new() { DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    private readonly ComboBox _monitor = new() { DisplayMemberPath = "Label", SelectedValuePath = "Id" };
    private readonly CheckBox _enabled = new() { Content = L.Text("Bật lịch"), IsChecked = true };
    private readonly Dictionary<DayOfWeek, CheckBox> _days = [];
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
    private ScheduleRule? _editing;
    private bool _refreshing;
    public List<ScheduleRule> Result => _rules;

    public ScheduleWindow(List<Playlist> playlists, List<ScheduleRule> rules, List<DisplayInfo> displays)
    {
        _rules = JsonSerializer.Deserialize<List<ScheduleRule>>(JsonSerializer.Serialize(rules))!;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = L.Text("Lịch xoay tua theo giờ");
        Width = 780; Height = 620; MinHeight = 580;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var root = new DockPanel { Margin = new Thickness(24) };
        var bottom = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        bottom.Children.Add(new TextBlock { Text = L.Text("Qua nửa đêm: chọn ngày bắt đầu. Giờ đầu = giờ cuối: cả ngày.\nLịch màn hình riêng được ưu tiên; lịch phía trên thắng khi trùng giờ."), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted") });
        bottom.Children.Add(_error);
        var finish = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var cancel = new Button { Content = L.Text("Hủy"), IsCancel = true };
        var save = new Button { Content = L.Text("Lưu tất cả"), Style = (Style)FindResource("Primary") };
        save.Click += (_, _) => { if (_list.SelectedItem == null || SaveRule()) DialogResult = true; };
        finish.Children.Add(cancel); finish.Children.Add(save); bottom.Children.Add(finish);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(230) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var left = new DockPanel { Margin = new Thickness(0, 0, 20, 0) };
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var add = new Button { Content = "+" }; add.Click += (_, _) =>
        {
            if (_editing != null && !SaveRule()) return;
            var rule = new ScheduleRule { Name = L.Text($"Lịch {_rules.Count + 1}") };
            _rules.Add(rule); Refresh(rule);
        };
        var remove = new Button { Content = L.Text("Xóa") }; remove.Click += (_, _) =>
        {
            if (_list.SelectedItem is ScheduleRule rule) { _rules.Remove(rule); Refresh(); }
        };
        var up = new Button { Content = "↑" }; up.Click += (_, _) => Move(-1);
        var down = new Button { Content = "↓" }; down.Click += (_, _) => Move(1);
        actions.Children.Add(add); actions.Children.Add(remove); actions.Children.Add(up); actions.Children.Add(down);
        DockPanel.SetDock(actions, Dock.Bottom); left.Children.Add(actions); left.Children.Add(_list); grid.Children.Add(left);
        var form = new StackPanel();
        AddField(form, L.Text("Tên lịch"), _name);
        L.SetPlaylistTemplate(_playlist); _playlist.ItemsSource = playlists; _playlist.SelectedIndex = 0;
        AddField(form, L.Text("Bộ wallpaper"), _playlist);
        var allDisplays = new List<DisplayInfo> { new("*", L.Text("Tất cả màn hình"), 0, 0, 0, 0) };
        allDisplays.AddRange(displays);
        foreach (var id in _rules.Select(r => r.MonitorId).Where(id => id != "*" && allDisplays.All(d => d.Id != id)).Distinct())
            allDisplays.Add(new(id, id + L.Text(" · Đang ngắt kết nối"), 0, 0, 0, 0));
        _monitor.ItemsSource = allDisplays; _monitor.SelectedIndex = 0;
        AddField(form, L.Text("Màn hình"), _monitor);
        var times = new Grid(); times.ColumnDefinitions.Add(new()); times.ColumnDefinitions.Add(new());
        var a = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; AddField(a, L.Text("Bắt đầu (HH:mm)"), _start);
        var b = new StackPanel(); AddField(b, L.Text("Kết thúc (HH:mm)"), _end); Grid.SetColumn(b, 1);
        times.Children.Add(a); times.Children.Add(b); form.Children.Add(times);
        form.Children.Add(new TextBlock { Text = L.Text("Ngày trong tuần"), Margin = new Thickness(0, 8, 0, 8) });
        var weekdays = new WrapPanel();
        foreach (var (day, label) in new[] { (DayOfWeek.Monday, "T2"), (DayOfWeek.Tuesday, "T3"), (DayOfWeek.Wednesday, "T4"), (DayOfWeek.Thursday, "T5"), (DayOfWeek.Friday, "T6"), (DayOfWeek.Saturday, "T7"), (DayOfWeek.Sunday, "CN") })
        {
            var box = new CheckBox { Content = L.Text(label), IsChecked = true, Margin = new Thickness(0, 0, 12, 12) };
            _days.Add(day, box); weekdays.Children.Add(box);
        }
        form.Children.Add(weekdays); form.Children.Add(_enabled);
        var update = new Button { Content = L.Text("Cập nhật lịch đang chọn") }; update.Click += (_, _) => SaveRule();
        form.Children.Add(update);
        Grid.SetColumn(form, 1); grid.Children.Add(form); root.Children.Add(grid); Content = root;
        _list.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _editing != null && _rules.Contains(_editing) && !CommitRule(_editing))
            {
                _refreshing = true;
                _list.SelectedItem = _editing;
                _refreshing = false;
                return;
            }
            if (!_refreshing) LoadRule();
        };
        Refresh(_rules.FirstOrDefault());
    }

    private static void AddField(Panel parent, string label, FrameworkElement control)
    {
        parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 5) });
        control.Margin = new Thickness(0, 0, 0, 12);
        parent.Children.Add(control);
    }
    private void Refresh(ScheduleRule? selected = null)
    {
        _refreshing = true;
        _list.ItemsSource = null; _list.ItemsSource = _rules;
        _list.SelectedItem = selected ?? _rules.FirstOrDefault();
        _refreshing = false;
        LoadRule();
    }
    private void LoadRule()
    {
        _error.Text = "";
        _editing = _list.SelectedItem as ScheduleRule;
        if (_editing is not { } rule) return;
        _name.Text = rule.Name;
        _start.Text = rule.Start.ToString(@"hh\:mm");
        _end.Text = rule.End.ToString(@"hh\:mm");
        _playlist.SelectedValue = rule.PlaylistId;
        _monitor.SelectedValue = rule.MonitorId;
        _enabled.IsChecked = rule.Enabled;
        foreach (var (day, box) in _days) box.IsChecked = rule.Days.Contains(day);
    }
    private bool SaveRule()
    {
        if (_editing is not { } rule) { _error.Text = L.Text("Bấm + để thêm lịch trước."); return false; }
        if (!CommitRule(rule)) return false;
        Refresh(rule);
        _error.Text = L.Text("Đã cập nhật. Bấm Lưu tất cả để áp dụng.");
        return true;
    }
    private bool CommitRule(ScheduleRule rule)
    {
        if (!TimeOnly.TryParseExact(_start.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(_end.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        { _error.Text = L.Text("Nhập giờ đúng HH:mm, ví dụ 07:00 hoặc 22:30."); return false; }
        if (string.IsNullOrWhiteSpace(_name.Text) || !_days.Values.Any(b => b.IsChecked == true))
        { _error.Text = L.Text("Nhập tên lịch và chọn ít nhất một ngày."); return false; }
        if (_playlist.SelectedItem is not Playlist playlist || _monitor.SelectedItem is not DisplayInfo monitor)
        { _error.Text = L.Text("Chọn bộ wallpaper và màn hình."); return false; }
        rule.Name = _name.Text.Trim(); rule.Start = start.ToTimeSpan(); rule.End = end.ToTimeSpan();
        rule.PlaylistId = playlist.Id; rule.MonitorId = monitor.Id; rule.Enabled = _enabled.IsChecked == true;
        rule.Days = _days.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        return true;
    }
    private void Move(int direction)
    {
        if (_list.SelectedItem is not ScheduleRule rule) return;
        if (!SaveRule()) return;
        var index = _rules.IndexOf(rule); var target = index + direction;
        if (target < 0 || target >= _rules.Count) return;
        (_rules[index], _rules[target]) = (_rules[target], _rules[index]); Refresh(rule);
    }
}