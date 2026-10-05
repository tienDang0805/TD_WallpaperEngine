using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Markup;

namespace TienDang.App;

// Translate complete UI messages, never fragments of user-provided names or paths.
public static class L
{
    public sealed class LanguageState : INotifyPropertyChanged
    {
        public string Language { get; internal set; } = "vi";
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void Notify() => PropertyChanged?.Invoke(this, new(nameof(Language)));
    }
    public static LanguageState State { get; } = new();
    private static readonly Dictionary<string, string> English = Load("en");
    private static readonly Dictionary<string, string> Vietnamese = Load("vi");
    private sealed record Template(Regex Pattern, string English, string Vietnamese);
    private static readonly Template[] Templates = English.Where(p => Regex.IsMatch(p.Key, @"\{\d+\}")).Select(p =>
    {
        var pattern = Regex.Escape(p.Key);
        foreach (Match token in Regex.Matches(p.Key, @"\{(\d+)\}"))
            pattern = pattern.Replace(Regex.Escape(token.Value), "(?<p" + token.Groups[1].Value + ">.*?)");
        return new Template(new Regex(@"\A" + pattern + @"\z", RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)),
            p.Value, Vietnamese.GetValueOrDefault(p.Key, p.Key));
    }).ToArray();

    private static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("TienDang.App.Localization." + language + ".json");
        return stream == null ? new(StringComparer.Ordinal) :
            JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    public static void SetLanguage(string language)
    {
        language = language == "en" ? "en" : "vi";
        if (State.Language == language) return;
        State.Language = language;
        State.Notify();
    }
    public static string Text(string source)
    {
        var map = State.Language == "en" ? English : Vietnamese;
        if (map.TryGetValue(source, out var exact)) return exact;
        foreach (var template in Templates)
        {
            Match match;
            try { match = template.Pattern.Match(source); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success) continue;
            var output = State.Language == "en" ? template.English : template.Vietnamese;
            if (State.Language == "en")
            {
                // Resolve singular units before inserting values, so user names stay untouched.
                output = Regex.Replace(output, @"\{(\d+)\} (?:new |supported )?(?:items|images|videos|wallpapers|files)\b",
                    token => match.Groups["p" + token.Groups[1].Value].Value == "1" ? token.Value[..^1] : token.Value);
            }
            return Regex.Replace(output, @"\{(\d+)\}", token => match.Groups["p" + token.Groups[1].Value].Value);
        }
        // Known error prefixes may be followed by OS diagnostics. Do not translate those diagnostics.
        foreach (var key in map.Keys.Where(k => k.EndsWith(": ", StringComparison.Ordinal) || k.EndsWith(". ", StringComparison.Ordinal)))
            if (source.StartsWith(key, StringComparison.Ordinal)) return map[key] + source[key.Length..];
        return source;
    }
    internal static string MediaSummary(int images, int videos) => State.Language == "en" ?
        $"{images} {(images == 1 ? "image" : "images")} · {videos} {(videos == 1 ? "video" : "videos")}" : $"{images} ảnh · {videos} video";
    internal static string ItemCount(int count) => State.Language == "en" ? $"{count} {(count == 1 ? "item" : "items")}" : $"{count} mục";
    internal static string PlaylistName(Playlist playlist) => playlist.Id == Playlist.AllId ? Text("Tất cả wallpaper") : playlist.Name;
    internal static string FileFilter => MediaTypes.FileDialogFilter.Replace("|Tất cả file|", State.Language == "en" ? "|All files|" : "|Tất cả file|");
    internal static void SetPlaylistTemplate(ItemsControl control)
    {
        control.DisplayMemberPath = "";
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new PlaylistLabelConverter() });
        control.ItemTemplate = new DataTemplate { VisualTree = text };
    }
}
public sealed class PlaylistLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is Playlist playlist ? L.PlaylistName(playlist) : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string text) => Text = text;
    public string Text { get; }
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(L.LanguageState.Language)) { Source = L.State, Converter = new LocalizedTextConverter(), ConverterParameter = Text }.ProvideValue(serviceProvider);
}
public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => L.Text((string)parameter);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}