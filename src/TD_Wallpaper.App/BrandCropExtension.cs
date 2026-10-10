using System.Windows.Markup;
using System.Windows.Media.Imaging;
namespace TD_Wallpaper.App;
public sealed class BrandCropExtension : MarkupExtension
{
    public string Part { get; set; } = "standing";
    private static readonly IReadOnlyDictionary<string, BitmapSource> Images = new Dictionary<string, BitmapSource>
    {
        ["standing"] = Load("mascot-library.png"), ["desk"] = Load("mascot-desk.png"), ["empty"] = Load("mascot-empty.png")
    };
    private static BitmapSource Load(string file)
    {
        var source = new BitmapImage();
        source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.DecodePixelWidth = 384;
        source.UriSource = new Uri("pack://application:,,,/TD_Wallpaper;component/Assets/" + file);
        source.EndInit();
        source.Freeze(); return source;
    }
    public override object ProvideValue(IServiceProvider serviceProvider) => Images.TryGetValue(Part, out var image) ? image : Images["standing"];
}
