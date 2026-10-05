using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace TienDang.App;

public sealed class ThumbnailConverter : IValueConverter
{
    private readonly Dictionary<string, BitmapSource> _cache = [];
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not WallpaperItem item || !item.Exists || item.IsVideo) return null;
        if (_cache.TryGetValue(item.Path, out var cached)) return cached;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 320;
            image.UriSource = new Uri(item.Path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            if (_cache.Count >= 400) _cache.Clear();
            _cache[item.Path] = image;
            return image;
        }
        catch { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}