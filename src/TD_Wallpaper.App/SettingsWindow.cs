using System.Text.Json;
using System.Windows.Media.Imaging;
namespace TD_Wallpaper.App;

internal sealed class SettingsWindow : Window
{
    private readonly CheckBox _mute, _fullscreen, _maximized, _battery, _resume, _copy, _startup;
    private readonly Slider _volume;
    private readonly ComboBox _language, _fps, _profile, _ruleAction;
    private readonly TextBox _rules;
    private readonly CheckBox _release;
    private readonly Dictionary<string, RadioButton> _fits = [];
    public AppSettings Result { get; }
    public bool StartWithWindows => _startup.IsChecked == true;

    public SettingsWindow(AppSettings settings, bool startup)
    {
        NameScope.SetNameScope(this, new NameScope());
        Result = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = L.Text("Tùy chọn · TD_Wallpaper");
        Width = 760; Height = Math.Min(790, SystemParameters.WorkArea.Height - 32);
        MinHeight = 500; MinWidth = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var root = new DockPanel { Margin = new Thickness(24) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = L.Text("Hủy"), IsCancel = true };
        var save = new Button { Content = L.Text("Lưu tùy chọn"), IsDefault = true, Style = (Style)FindResource("Primary") };
        save.Click += Save; RegisterName("SavePreferencesButton", save);
        actions.Children.Add(cancel); actions.Children.Add(save); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var form = new StackPanel();
        Heading(form, "Ngôn ngữ");
        _language = Choices(("vi", "Tiếng Việt"), ("en", "English"));
        _language.SelectedValue = settings.Language; RegisterName("LanguageChoice", _language); form.Children.Add(_language);
        Note(form, "Ngôn ngữ sẽ đổi sau khi lưu.");
        Heading(form, "Cách ảnh và video vừa màn hình");
        Note(form, "Minh họa: một ảnh dọc trên màn hình ngang. Chọn cách hiển thị bạn thích.");
        var cards = new Grid { Margin = new Thickness(0, 4, 0, 10) };
        var sample = SampleImage();
        var choices = new[] {
            ("Fill", "Kín màn hình", "Không có viền đen. Các mép ảnh có thể bị cắt.", Stretch.UniformToFill),
            ("Fit", "Giữ trọn ảnh", "Không cắt ảnh. Có thể xuất hiện viền đen.", Stretch.Uniform),
            ("Stretch", "Kéo vừa màn hình", "Phủ kín màn hình. Hình có thể bị kéo ngang hoặc dọc.", Stretch.Fill)
        };
        for (var i = 0; i < choices.Length; i++)
        {
            var (tag, label, description, stretch) = choices[i];
            cards.ColumnDefinitions.Add(new());
            var content = new StackPanel();
            var screen = new Border { Background = Brushes.Black, Height = 100, CornerRadius = new CornerRadius(7), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 10) };
            var picture = new Image { Source = sample, Stretch = stretch };
            RegisterName("FitPreview" + tag, picture); screen.Child = picture; content.Children.Add(screen);
            var radio = new RadioButton { Content = L.Text(label), GroupName = "ImageSizing", IsChecked = settings.Fit == tag, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
            _fits[tag] = radio; RegisterName("FitChoice" + tag, radio); content.Children.Add(radio);
            content.Children.Add(new TextBlock { Text = L.Text(description), TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)FindResource("Muted"), MinHeight = 55 });
            var border = new Border { Child = content, Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(12), BorderThickness = new Thickness(2), Margin = new Thickness(i == 0 ? 0 : 5, 0, i == 2 ? 0 : 5, 0) };
            void Paint() { border.BorderBrush = radio.IsChecked == true ? new SolidColorBrush(Color.FromRgb(39,139,106)) : new SolidColorBrush(Color.FromRgb(222,231,245)); }
            radio.Checked += (_, _) => Paint(); radio.Unchecked += (_, _) => Paint();
            border.MouseLeftButtonUp += (_, _) => radio.IsChecked = true;
            Paint(); Grid.SetColumn(border, i); cards.Children.Add(border);
        }
        form.Children.Add(cards);
        Heading(form, "Độ mượt video");
        _fps = Choices((0, L.Text("Theo video gốc")), (15, "15 FPS"), (30, "30 FPS"), (60, "60 FPS"));
        _fps.SelectedValue = settings.FrameRateLimit; RegisterName("FrameRateChoice", _fps); form.Children.Add(_fps);
        Note(form, "15 FPS: ít mượt hơn · 30 FPS: vừa phải · 60 FPS: mượt hơn. Đây là mức tối đa; video FPS thấp sẽ giữ tốc độ gốc.");
        Note(form, "Giới hạn FPS chỉ áp dụng cho video hình nền. Video xem trước giữ chất lượng gốc. Hiệu năng còn phụ thuộc độ phân giải và định dạng video.");
        Note(form, "Nếu giới hạn FPS làm video giật hơn, thử Theo video gốc hoặc bản video 1080p nhẹ hơn.");
        _mute = Box(form, "Tắt tiếng video hình nền", settings.Muted);
        form.Children.Add(new TextBlock { Text = L.Text("Âm lượng") });
        _volume = new Slider { Minimum = 0, Maximum = 100, Value = settings.Volume, Margin = new Thickness(0, 8, 0, 12) };
        form.Children.Add(_volume);
        Heading(form, "Hiệu năng");
        _profile = Choices(("Custom", L.Text("Tùy chỉnh")), ("Saver", L.Text("Tiết kiệm")), ("Balanced", L.Text("Cân bằng")), ("Quality", L.Text("Chất lượng")));
        _profile.SelectedValue = settings.PerformanceProfile; RegisterName("PerformanceProfileChoice", _profile); form.Children.Add(_profile);
        Note(form, "Tiết kiệm: ngừng bộ phát khi màn hình bị che để nhả RAM/GPU. Cân bằng: tạm dừng khi toàn màn hình hoặc dùng pin. Chất lượng: tiếp tục phát. Các profile giữ FPS gốc; bạn vẫn có thể chỉnh FPS riêng.");
        _release = Box(form, "Ngừng bộ phát để nhả bộ nhớ khi tự tạm dừng", settings.ReleaseWhenBusy);
        Note(form, "Nền Windows sẽ hiện trong lúc ngừng. App nhớ nền và thời gian còn lại để tiếp tục khi bạn quay lại.");
        Heading(form, "Quy tắc ứng dụng");
        Note(form, "Nhập tên file ứng dụng, mỗi dòng một tên, ví dụ game.exe hoặc blender.exe. Quy tắc áp dụng khi cửa sổ đó đang hiển thị trên màn hình.");
        _rules = new TextBox { Text = string.Join(Environment.NewLine, settings.AppRules.Select(r => r.ProcessName)), AcceptsReturn = true, Height = 72, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; form.Children.Add(_rules);
        _ruleAction = Choices(("Pause", L.Text("Tạm dừng")), ("Release", L.Text("Ngừng để nhả bộ nhớ"))); _ruleAction.SelectedValue = settings.AppRules.FirstOrDefault()?.Action ?? "Pause"; form.Children.Add(_ruleAction);
        Heading(form, "Chạy nền");
        _fullscreen = Box(form, "Tạm dừng khi ứng dụng toàn màn hình", settings.PauseFullscreen);
        _maximized = Box(form, "Tạm dừng khi cửa sổ phóng to", settings.PauseMaximized);
        RegisterName("PauseFullscreenChoice", _fullscreen); RegisterName("PauseMaximizedChoice", _maximized);
        Note(form, "Chỉ tạm dừng trên màn hình bị cửa sổ che. Thu nhỏ hoặc đóng cửa sổ để tiếp tục; tạm dừng thủ công vẫn được giữ.");
        _battery = Box(form, "Tạm dừng khi dùng pin", settings.PauseOnBattery);
        _resume = Box(form, "Tiếp tục wallpaper khi mở lại app", settings.ResumeOnLaunch);
        _startup = Box(form, "Khởi động cùng Windows", startup);
        Note(form, "Khi đăng nhập Windows, app chờ 3 giây để hệ thống ổn định. Mở app thủ công không phải chờ.");
        _copy = Box(form, "Chép file vào thư viện khi thêm", settings.CopyOnImport);
        Note(form, "Preview có nút tắt tiếng riêng. Đóng cửa sổ chính sẽ thu app xuống khay hệ thống; hình nền vẫn chạy.");
        root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); Content = root;
    }
    private static ComboBox Choices(params (object Value, string Label)[] choices)
    {
        var combo = new ComboBox { SelectedValuePath = "Tag", Margin = new Thickness(0, 0, 0, 7) };
        foreach (var (value, label) in choices) combo.Items.Add(new ComboBoxItem { Tag = value, Content = label });
        return combo;
    }
    private static void Heading(Panel parent, string label) => parent.Children.Add(new TextBlock { Text = L.Text(label), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 9, 0, 10) });
    private static void Note(Panel parent, string label) => parent.Children.Add(new TextBlock { Text = L.Text(label), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.FindResource("Muted"), FontSize = 12, Margin = new Thickness(0, 0, 0, 10) });
    private static ImageSource SampleImage()
    {
        // One portrait illustration shared by all cards; real WPF sizing shows crop/bars/distortion.
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(175,222,239)), null, new Rect(0,0,90,140));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255,213,126)), null, new Point(62,35), 18,18);
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(76,133,151)), null, Geometry.Parse("M0,112 L38,46 L90,114 Z"));
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(230,242,249)), null, Geometry.Parse("M25,70 L38,46 L54,70 L40,63 L33,72 Z"));
            dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(34,99,94)), null, Geometry.Parse("M0,110 L22,85 L55,140 L0,140 Z M45,140 L73,86 L90,111 L90,140 Z"));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(134,204,205)), null, new Point(44,137), 23,16);
        }
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        Result.Fit = _fits.First(p => p.Value.IsChecked == true).Key;
        Result.Language = (_language.SelectedItem as ComboBoxItem)?.Tag as string ?? "vi";
        Result.FrameRateLimit = (_fps.SelectedItem as ComboBoxItem)?.Tag as int? ?? 0;
        Result.Muted = _mute.IsChecked == true; Result.Volume = (int)_volume.Value;
        Result.PauseFullscreen = _fullscreen.IsChecked == true; Result.PauseMaximized = _maximized.IsChecked == true; Result.PauseOnBattery = _battery.IsChecked == true;
        Result.ResumeOnLaunch = _resume.IsChecked == true; Result.CopyOnImport = _copy.IsChecked == true;
        Result.ReleaseWhenBusy = _release.IsChecked == true;
        Result.AppRules = _rules.Text.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(50).Select(v => new AppRule { ProcessName = v, Action = _ruleAction.SelectedValue as string ?? "Pause" }).ToList();
        var chosenProfile = _profile.SelectedValue as string ?? "Custom";
        if (chosenProfile != Result.PerformanceProfile) PerformanceProfiles.Apply(Result, chosenProfile);
        DialogResult = true;
    }
    private static CheckBox Box(Panel parent, string label, bool value)
    {
        var box = new CheckBox { Content = L.Text(label), IsChecked = value }; parent.Children.Add(box); return box;
    }
}