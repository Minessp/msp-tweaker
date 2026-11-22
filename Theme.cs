using System.Windows;
using System.Windows.Media;

namespace MspTweaker;

/// Color tokens: dark is a graphite board, light is a white-edition board, gold is the ENIG pad finish.
/// XAML must use {DynamicResource Key} for them, or theme switching will not reach it.
static class Theme
{
    public static bool Dark { get; private set; }

    static readonly Dictionary<string, (string Dark, string Light)> Tokens = new()
    {
        ["Bg"] = ("#161616", "#EDF0EB"),
        ["Surface"] = ("#1E1E1E", "#F7F8F5"),
        ["Surface2"] = ("#101010", "#E3E7E0"),
        ["Hover"] = ("#272727", "#E1E6DE"),
        ["Line"] = ("#333333", "#C3CBC1"),
        ["LineStrong"] = ("#7C7C7C", "#6E786F"),
        ["Text"] = ("#ECEAE6", "#19201B"),
        ["Muted"] = ("#A8A8A8", "#556058"),
        ["Subtle"] = ("#717171", "#8C968E"),
        ["Accent"] = ("#D9B45B", "#C9A245"),
        ["AccentFg"] = ("#161616", "#19201B"),
        ["AccentText"] = ("#E6C46F", "#8A6A16"),
        ["Warn"] = ("#F2B54A", "#A86400"),
        ["Danger"] = ("#FF7B63", "#B8321C"),
        ["DangerFill"] = ("#E0482F", "#C93A22"),
        ["Scrim"] = ("#B3000000", "#66131A15"),
        ["CloseHover"] = ("#C42B1C", "#C42B1C"),
        ["LedOff"] = ("#0D0D0D", "#D6DBD3"),
        ["LedRim"] = ("#3E3E3E", "#AEB7AD"),
        ["LedGold"] = ("#FFD36A", "#E0A800"),
        ["LedAmber"] = ("#FFB13B", "#E08A00"),
        ["LedRed"] = ("#FF5A45", "#D9381E"),
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
