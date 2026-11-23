using System.Windows;
using System.Windows.Media;

namespace MspTweaker;

/// Color tokens: neutral greys like a vendor control app, one orange that only marks what still needs action.
/// XAML must use {DynamicResource Key} for them, or theme switching will not reach it.
static class Theme
{
    public static bool Dark { get; private set; }

    static readonly Dictionary<string, (string Dark, string Light)> Tokens = new()
    {
        ["Bg"] = ("#161616", "#F3F3F3"),
        ["Surface"] = ("#202020", "#FFFFFF"),
        ["Surface2"] = ("#2B2B2B", "#E8E8E8"),
        ["Hover"] = ("#262626", "#F0F0F0"),
        ["Line"] = ("#2E2E2E", "#E2E2E2"),
        ["LineStrong"] = ("#4A4A4A", "#BDBDBD"),
        ["Text"] = ("#F0F0F0", "#161616"),
        ["Muted"] = ("#A3A3A3", "#575757"),
        ["Subtle"] = ("#878787", "#737373"),
        ["Accent"] = ("#FF5F1F", "#FF5F1F"),
        ["AccentFg"] = ("#161616", "#161616"),
        ["AccentText"] = ("#FF7A45", "#C2410C"),
        ["Warn"] = ("#F5A524", "#A15C00"),
        ["Danger"] = ("#FF6369", "#C62F35"),
        ["DangerFill"] = ("#D93036", "#C62F35"),
        ["Scrim"] = ("#B3000000", "#66161616"),
        ["CloseHover"] = ("#C42B1C", "#C42B1C"),
    };

    public static void Set(bool dark)
    {
        Dark = dark;
        foreach (var (key, c) in Tokens)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? c.Dark : c.Light));
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
    }
}
