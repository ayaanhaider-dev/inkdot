using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lveno.App.Controls;

/// <summary>Reads theme-aware brand brushes and fonts for controls that draw themselves.</summary>
internal static class BrandBrushes
{
    public static IBrush Get(StyledElement element, string key, string fallback)
    {
        if (element.TryFindResource(key, element.ActualThemeVariant, out var value) && value is IBrush brush)
            return brush;
        return new SolidColorBrush(Color.Parse(fallback));
    }

    public static Typeface Font(StyledElement element, FontWeight weight = FontWeight.Normal) =>
        new(Family(element, "Lv.Font"), FontStyle.Normal, weight);

    public static Typeface Mono(StyledElement element) =>
        new(Family(element, "Lv.Mono"));

    private static FontFamily Family(StyledElement element, string key) =>
        element.TryFindResource(key, out var value) && value is FontFamily family ? family : FontFamily.Default;

    public static FormattedText Text(string text, Typeface typeface, double size, IBrush brush) =>
        new(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush);
}
