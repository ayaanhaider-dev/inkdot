using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lveno.App.Controls;

/// <summary>
/// The "lveno●" wordmark, drawn from brand/logo/svg/lveno-wordmark-*.svg.
/// Its full stop is a live <see cref="LvenoDot"/>, so the app's status lives in the logo itself.
/// </summary>
public class Wordmark : Control
{
    // Letter outlines from the brand SVG (viewBox 0 0 339.6 132).
    private const string LettersPath =
        "M49.50 100L40.60 100Q34 100 30.30 96.70Q26.60 93.40 26.60 86L26.60 86L26.60 29L41.60 29L41.60 84.40Q41.60 86.80 42.70 87.80Q43.80 88.80 45.90 88.80L45.90 88.80L49.50 88.80L49.50 100Z" +
        "M84.90 100L67.50 100L47.90 46.40L63.60 46.40L76.30 84.90L88.90 46.40L104.60 46.40L84.90 100Z" +
        "M131.90 101.20L131.90 101.20Q123.70 101.20 117.650 97.75Q111.60 94.30 108.25 88Q104.90 81.70 104.90 73.20L104.90 73.20Q104.90 64.70 108.25 58.45Q111.60 52.20 117.60 48.70Q123.60 45.20 131.80 45.20L131.80 45.20Q139.80 45.20 145.70 48.70Q151.60 52.20 154.85 58.70Q158.10 65.20 158.10 74.20L158.10 74.20L158.10 77.20L120.40 77.20Q120.70 83.60 123.85 86.70Q127.00 89.80 132.20 89.80L132.20 89.80Q136 89.80 138.55 88.20Q141.10 86.60 142.20 83.30L142.20 83.30L157.30 84.20Q155.20 92.20 148.40 96.70Q141.60 101.20 131.90 101.20Z" +
        "M120.40 68L120.40 68L142.70 68Q142.40 62.10 139.45 59.20Q136.50 56.30 131.80 56.30L131.80 56.30Q127.10 56.30 124.05 59.35Q121.00 62.40 120.40 68Z" +
        "M177.80 100L162.80 100L162.80 46.40L176.30 46.40L176.90 62.10L175 61.60Q175.80 55.60 178.25 52Q180.70 48.40 184.40 46.80Q188.10 45.20 192.50 45.20L192.50 45.20Q198.30 45.20 202.35 47.70Q206.40 50.20 208.55 54.75Q210.70 59.30 210.70 65.50L210.70 65.50L210.70 100L195.70 100L195.70 70.50Q195.70 66.20 195 63.20Q194.30 60.20 192.50 58.65Q190.70 57.10 187.50 57.10L187.50 57.10Q182.80 57.10 180.30 60.55Q177.80 64 177.80 70.50L177.80 70.50L177.80 100Z" +
        "M242.70 101.20L242.70 101.20Q234.50 101.20 228.40 97.75Q222.30 94.30 218.90 88Q215.50 81.70 215.50 73.20L215.50 73.20Q215.50 64.70 218.90 58.45Q222.30 52.20 228.40 48.70Q234.50 45.20 242.70 45.20L242.70 45.20Q250.90 45.20 257 48.70Q263.10 52.20 266.50 58.45Q269.90 64.70 269.90 73.20L269.90 73.20Q269.90 81.70 266.50 88Q263.10 94.30 257 97.75Q250.90 101.20 242.70 101.20Z" +
        "M242.70 89.70L242.70 89.70Q248.30 89.70 251.40 85.35Q254.50 81 254.50 73.20L254.50 73.20Q254.50 65.40 251.40 61.05Q248.30 56.70 242.70 56.70L242.70 56.70Q237.10 56.70 234 61.05Q230.90 65.40 230.90 73.20L230.90 73.20Q230.90 81 234 85.35Q237.10 89.70 242.70 89.70Z";

    // Visible content inside the SVG viewBox (letters plus dot), so layout hugs the mark.
    private static readonly Rect Content = new(24, 26, 296, 77);
    private static readonly Point DotCenter = new(296.6, 79.0);
    private const double DotRadius = 21.0;

    private static readonly Geometry Letters = StreamGeometry.Parse(LettersPath);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<Wordmark, IBrush?>(nameof(Foreground));

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The full stop, as a live character.</summary>
    public LvenoDot Dot { get; } = new() { IsHitTestVisible = false };

    static Wordmark()
    {
        AffectsRender<Wordmark>(ForegroundProperty);
    }

    public Wordmark()
    {
        VisualChildren.Add(Dot);
        LogicalChildren.Add(Dot);
    }

    private double Scale(Size size) =>
        Math.Min(size.Width / Content.Width, size.Height / Content.Height);

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = double.IsInfinity(availableSize.Height) ? 28 : availableSize.Height;
        if (!double.IsNaN(Height)) height = Height;
        return new Size(height * Content.Width / Content.Height, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double s = Scale(finalSize);
        // LvenoDot draws its circle at 50/120 of its box, so give it a box 120/50 of the diameter.
        double box = DotRadius * 2 * s * 120.0 / 100.0;
        var center = new Point((DotCenter.X - Content.X) * s, (DotCenter.Y - Content.Y) * s);
        Dot.Arrange(new Rect(center.X - box / 2, center.Y - box / 2, box, box));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        double s = Scale(Bounds.Size);
        using (context.PushTransform(Matrix.CreateTranslation(-Content.X, -Content.Y) * Matrix.CreateScale(s, s)))
        {
            context.DrawGeometry(Foreground ?? Brushes.Black, null, Letters);
        }
    }
}
