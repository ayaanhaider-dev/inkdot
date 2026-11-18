using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lveno.App.Controls;

/// <summary>A half-circle gauge for the pressure reaching your apps, 0 to the pen's maximum.</summary>
public class PressureGauge : Control
{
    private float _value;
    private float _max = 8191;

    public PressureGauge() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public void Set(float value, float max)
    {
        _value = Math.Clamp(value, 0, max);
        _max = Math.Max(1, max);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var fg = BrandBrushes.Get(this, "Lv.Fg", "#121212");
        var line = BrandBrushes.Get(this, "Lv.Line", "#DADAD4");
        var muted = BrandBrushes.Get(this, "Lv.Muted", "#6C6C66");
        var ember = BrandBrushes.Get(this, "Lv.Ember", "#F2551D");
        var mono = BrandBrushes.Mono(this);

        double thickness = 14;
        double radius = Math.Min(Bounds.Width / 2, Bounds.Height - 24) - thickness;
        if (radius <= 10) return;
        var center = new Point(Bounds.Width / 2, radius + thickness);

        ctx.DrawGeometry(null, new Pen(line, thickness, lineCap: PenLineCap.Round), Arc(center, radius, 1));
        float fraction = _value / _max;
        if (fraction > 0.002f)
            ctx.DrawGeometry(null, new Pen(ember, thickness, lineCap: PenLineCap.Round), Arc(center, radius, fraction));

        var number = BrandBrushes.Text($"{_value:0}", BrandBrushes.Font(this, FontWeight.Bold), 34, fg);
        ctx.DrawText(number, new Point(center.X - number.Width / 2, center.Y - number.Height + 4));

        var zero = BrandBrushes.Text("0", mono, 11, muted);
        var max = BrandBrushes.Text($"{_max:0}", mono, 11, muted);
        ctx.DrawText(zero, new Point(center.X - radius - zero.Width / 2, center.Y + 10));
        ctx.DrawText(max, new Point(center.X + radius - max.Width / 2, center.Y + 10));
    }

    /// <summary>Arc from the left end (pointing left) clockwise over the top, covering <paramref name="fraction"/> of 180°.</summary>
    private static StreamGeometry Arc(Point c, double r, double fraction)
    {
        double angle = Math.PI * (1 - fraction); // 180° → 0°
        var end = new Point(c.X + r * Math.Cos(angle), c.Y - r * Math.Sin(angle));
        var g = new StreamGeometry();
        using var ctx = g.Open();
        ctx.BeginFigure(new Point(c.X - r, c.Y), false);
        ctx.ArcTo(end, new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(false);
        return g;
    }
}
