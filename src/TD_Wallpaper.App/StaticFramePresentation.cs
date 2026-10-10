using System.Windows.Interop;
using System.Windows.Media;

namespace TD_Wallpaper.App;

internal static class StaticFramePresentation
{
    // This process draws only still images and temporary held frames in WPF.
    // Video is rendered by a separate MPV process directly into a native child.
    // Avoid mixed WPF D3D9 / external D3D11 overlay contexts during handoffs.
    internal static void ConfigureWorker() => RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
}