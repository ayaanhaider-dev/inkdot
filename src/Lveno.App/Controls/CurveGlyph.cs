using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lveno.Plugins;

namespace Lveno.App.Controls;

/// <summary>A tiny drawing of a curve, for the preset buttons.</summary>
public class CurveGlyph : Control
{
    public PressureCurve Curve { get; set; } = PressureCurve.Linear;

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<CurveGlyph, IBrush?>(nameof(Stroke));

    static CurveGlyph() => AffectsRender<CurveGlyph>(StrokeProperty);

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(Bounds.Size).Deflate(2);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (int i = 0; i <= 30; i++)
            {
                float x = i / 30f;
                var p = new Point(r.Left + x * r.Width, r.Bottom - Curve.Apply(x) * r.Height);
                if (i == 0) c.BeginFigure(p, false); else c.LineTo(p);
            }
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(Stroke ?? Brushes.Gray, 2, lineCap: PenLineCap.Round), g);
    }
}
