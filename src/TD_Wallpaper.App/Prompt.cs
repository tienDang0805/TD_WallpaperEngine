namespace TD_Wallpaper.App;

internal static class Prompt
{
    public static string? Ask(Window owner, string title, string label, string current)
    {
        var input = new TextBox { Text = current, Margin = new Thickness(0, 8, 0, 18) };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(input);
        var window = Dialog(owner, title, panel, 430, 220);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = L.Text("Hủy"), IsCancel = true };
        var save = new Button { Content = L.Text("Lưu"), IsDefault = true };
        save.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons);
        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text : null;
    }
    public static Playlist? Choose(Window owner, string title, List<Playlist> options)
    {
        var combo = new ComboBox { ItemsSource = options, DisplayMemberPath = "Name", SelectedIndex = 0 };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = L.Text("Chọn bộ sưu tập") });
        panel.Children.Add(combo);
        var window = Dialog(owner, title, panel, 400, 200);
        var save = new Button { Content = L.Text("Thêm"), IsDefault = true };
        save.Click += (_, _) => window.DialogResult = true;
        panel.Children.Add(save);
        return window.ShowDialog() == true ? combo.SelectedItem as Playlist : null;
    }
    private static Window Dialog(Window owner, string title, object content, int width, int height) => new()
    {
        Style = (Style)Application.Current.FindResource(typeof(Window)), Owner = owner, Title = title, Content = content, Width = width, Height = height,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
    };
}