using System.Windows;
using System.Windows.Media;
using LumaClip.Models;
using LumaClip.Services;

namespace LumaClip.Core;

public static class ThemeManager
{
    public static bool IsDark { get; private set; }

    public static void Apply(AppTheme theme, bool reducedTransparency = false)
    {
        IsDark = theme == AppTheme.Dark || (theme == AppTheme.System && WindowsIntegration.IsSystemDark());
        var r = Application.Current.Resources;
        void Brush(string key, string color) => r[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        void ColorValue(string key, string color) => r[key] = (Color)ColorConverter.ConvertFromString(color);
        if (IsDark) {
            r["BackdropImageOpacity"] = .16d;
            Brush("BackdropTintBrush", "#C812141A");
            Brush("WindowBackground", "#D513151B");
            Brush("GlassSurface", "#B921242B");
            Brush("GlassSurfaceStrong", "#E82B2E36");
            Brush("GlassSurfaceSubtle", "#96383C45");
            Brush("SidebarSurface", "#A91D2027");
            Brush("InspectorSurface", "#DF252932");
            Brush("TileSurface", "#8D2E333C");
            Brush("TileHoverBrush", "#B53A404A");
            Brush("SelectedTileBrush", "#9A153B61");
            Brush("TileBorderBrush", "#2FFFFFFF");
            Brush("SeparatorBrush", "#24FFFFFF");
            Brush("ImageScrimBrush", "#22000000");
            Brush("SuccessBrush", "#FF32D74B");
            Brush("GlassHighlightBrush", "#38FFFFFF");
            Brush("TextPrimary", "#FFF5F6F7");
            Brush("TextSecondary", "#FFB5B8C0");
            Brush("TextTertiary", "#FF7E828C");
            Brush("BorderBrush", "#26FFFFFF");
            Brush("HoverBrush", "#18FFFFFF");
            Brush("SelectedBrush", "#2F0A84FF");
            Brush("InputBrush", "#84383B42");
            Brush("DangerBrush", "#FFFF6B73");
            Brush("CodeBrush", "#E51A1B20");
            Brush("AmbientBlueBrush", "#284C91FF");
            Brush("AmbientVioletBrush", "#1E9277FF");
            ColorValue("AmbientBlueColor", "#384B86FF");
            ColorValue("AmbientVioletColor", "#2F896FFF");
        } else {
            r["BackdropImageOpacity"] = 1d;
            Brush("BackdropTintBrush", "#0AFFFFFF");
            Brush("WindowBackground", "#D8EDF1F8");
            Brush("GlassSurface", "#BFFFFFFF");
            Brush("GlassSurfaceStrong", "#E8FFFFFF");
            Brush("GlassSurfaceSubtle", "#8FFFFFFF");
            Brush("SidebarSurface", "#78FFFFFF");
            Brush("InspectorSurface", "#B8FFFFFF");
            Brush("TileSurface", "#96FFFFFF");
            Brush("TileHoverBrush", "#B8FFFFFF");
            Brush("SelectedTileBrush", "#A8EAF3FF");
            Brush("TileBorderBrush", "#A8FFFFFF");
            Brush("SeparatorBrush", "#16111820");
            Brush("ImageScrimBrush", "#18FFFFFF");
            Brush("SuccessBrush", "#FF30D158");
            Brush("GlassHighlightBrush", "#E6FFFFFF");
            Brush("TextPrimary", "#FF17181C");
            Brush("TextSecondary", "#FF626771");
            Brush("TextTertiary", "#FF9297A1");
            Brush("BorderBrush", "#18121720");
            Brush("HoverBrush", "#0F101318");
            Brush("SelectedBrush", "#22007AFF");
            Brush("InputBrush", "#A8FFFFFF");
            Brush("DangerBrush", "#FFFF3B30");
            Brush("CodeBrush", "#EAF2F3F6");
            Brush("AmbientBlueBrush", "#2479BFFF");
            Brush("AmbientVioletBrush", "#16A98BFF");
            ColorValue("AmbientBlueColor", "#B896CFFF");
            ColorValue("AmbientVioletColor", "#9AC1A7FF");
        }
        Brush("AccentBrush", IsDark ? "#FF0A84FF" : "#FF007AFF");
        Brush("AccentSoftBrush", IsDark ? "#330A84FF" : "#1F007AFF");
        if (reducedTransparency) {
            Brush("WindowBackground", IsDark ? "#FF17181C" : "#FFF2F3F7");
            Brush("GlassSurface", IsDark ? "#FF25272D" : "#FFFAFAFC");
            Brush("GlassSurfaceStrong", IsDark ? "#FF2C2E34" : "#FFFFFFFF");
            Brush("GlassSurfaceSubtle", IsDark ? "#FF373A41" : "#FFF4F5F8");
            Brush("SidebarSurface", IsDark ? "#FF20232A" : "#FFF5F6F9");
            Brush("InspectorSurface", IsDark ? "#FF282C34" : "#FFFBFBFD");
            Brush("TileSurface", IsDark ? "#FF30353E" : "#FFFFFFFF");
            Brush("TileHoverBrush", IsDark ? "#FF3A404A" : "#FFF6F8FB");
            Brush("SelectedTileBrush", IsDark ? "#FF173F68" : "#FFE6F2FF");
            Brush("InputBrush", IsDark ? "#FF383B42" : "#FFFFFFFF");
        }
    }
}
