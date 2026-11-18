using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Lveno.App.Controls;

/// <summary>
/// A drawing of the pen, upright: a shaded barrel, a metal ring, a soft grip, a tapered tip and the
/// two-button rocker. The half you're editing lights up ember. Button 0 is the lower half (nearer the tip).
/// </summary>
public class PenDrawing : Control
{
    private int _highlight = -1;
    private int _buttonCount = 2;

    public PenDrawing() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public int Highlight
    {
        get => _highlight;
        set { _highlight = value; InvalidateVisual(); }
    }

    public int ButtonCount
    {
        get => _buttonCount;
        set { _buttonCount = Math.Clamp(value, 0, 3); InvalidateVisual(); }
    }

    public override void Render(DrawingContext ctx)
    {
        double h = Bounds.Height;
        double w = Math.Min(Bounds.Width * 0.2, h * 0.085);
        if (h <= 0 || w <= 0) return;
        double cx = Bounds.Width * 0.4;
        bool dark = ActualThemeVariant == ThemeVariant.Dark;

        Color body = Color.Parse(dark ? "#2C2C2A" : "#DCDCD6");
        Color shine = Color.Parse(dark ? "#4E4E4A" : "#FAFAF7");
        Color edge = Color.Parse(dark ? "#1A1A19" : "#B9B9B2");
        Color grip = Color.Parse(dark ? "#1E1E1D" : "#C8C8C1");
        var ember = (ISolidColorBrush)BrandBrushes.Get(this, "Lv.Ember", "#F2551D");
        var muted = BrandBrushes.Get(this, "Lv.Muted", "#6C6C66");
        var fg = BrandBrushes.Get(this, "Lv.Fg", "#121212");
        var line = BrandBrushes.Get(this, "Lv.Line", "#DADAD4");

        // A round barrel: dark at the sides, a highlight left of centre.
        IBrush Cylinder(Color baseColor, Color light) => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(edge, 0), new GradientStop(light, 0.3),
                new GradientStop(baseColor, 0.62), new GradientStop(edge, 1),
            },
        };

        double top = h * 0.02, ringY = h * 0.64, gripEnd = h * 0.82, nibTop = h * 0.955, bottom = h * 0.995;
        var outline = new Pen(new SolidColorBrush(edge), 1);

        // Soft shadow on the ground.
        ctx.DrawEllipse(new SolidColorBrush(Colors.Black, dark ? 0.35 : 0.12), null, new Point(cx + w * 0.3, bottom + 2), w * 0.9, 3);

        // Barrel with a rounded end.
        ctx.DrawRectangle(Cylinder(body, shine), outline, new Rect(cx - w / 2, top, w, ringY - top + w / 2), w / 2, w / 2);

        // Grip: a touch narrower and softer.
        double gw = w * 0.94;
        ctx.DrawRectangle(Cylinder(grip, body), outline, new Rect(cx - gw / 2, ringY, gw, gripEnd - ringY), 2, 2);
        for (int i = 1; i < 6; i++) // fine grip lines
        {
            double y = ringY + (gripEnd - ringY) * i / 6;
            ctx.DrawLine(new Pen(new SolidColorBrush(edge, 0.6), 0.8), new Point(cx - gw * 0.42, y), new Point(cx + gw * 0.42, y));
        }

        // Metal ring between barrel and grip.
        ctx.DrawRectangle(Cylinder(Color.Parse(dark ? "#6A6A66" : "#A8A8A2"), Color.Parse(dark ? "#B5B5AF" : "#FFFFFF")),
            null, new Rect(cx - w * 0.52, ringY - 3, w * 1.04, 6), 2, 2);

        // Tapered tip down to the nib.
        var cone = new StreamGeometry();
        using (var g = cone.Open())
        {
            g.BeginFigure(new Point(cx - gw / 2, gripEnd), true);
            g.LineTo(new Point(cx + gw / 2, gripEnd));
            g.CubicBezierTo(new Point(cx + gw * 0.46, gripEnd + (nibTop - gripEnd) * 0.5),
                new Point(cx + w * 0.16, nibTop - 4), new Point(cx + w * 0.09, nibTop));
            g.LineTo(new Point(cx - w * 0.09, nibTop));
            g.CubicBezierTo(new Point(cx - w * 0.16, nibTop - 4),
                new Point(cx - gw * 0.46, gripEnd + (nibTop - gripEnd) * 0.5), new Point(cx - gw / 2, gripEnd));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(Cylinder(body, shine), outline, cone);
        ctx.DrawRectangle(fg, null, new Rect(cx - w * 0.05, nibTop, w * 0.1, bottom - nibTop), w * 0.05, w * 0.05);

        // The rocker: one capsule, two halves. Upper = button 1, lower = button 0.
        if (_buttonCount == 0) return;
        double rw = w * 0.46, rh = h * 0.17, ry = ringY - h * 0.04 - rh;
        var rocker = new Rect(cx - rw / 2, ry, rw, rh);
        ctx.DrawRectangle(new SolidColorBrush(edge), null, rocker.Inflate(1.5), rw / 2 + 1.5, rw / 2 + 1.5);

        int halves = Math.Min(_buttonCount, 2);
        var mono = BrandBrushes.Mono(this);
        for (int half = 0; half < halves; half++)
        {
            int index = halves == 1 ? 0 : 1 - half; // drawn top-down: upper half first
            double hy = ry + rh * half / halves;
            var part = new Rect(rocker.X, hy, rw, rh / halves);
            bool on = index == _highlight;
            var fill = on
                ? (IBrush)ember
                : Cylinder(Color.Parse(dark ? "#3A3A37" : "#E9E9E4"), Color.Parse(dark ? "#5A5A56" : "#FFFFFF"));
            double r = rw / 2;
            // Round only the outer ends of each half.
            var shape = new StreamGeometry();
            using (var g = shape.Open())
            {
                if (half == 0)
                {
                    g.BeginFigure(new Point(part.Left, part.Bottom - 0.75), true);
                    g.LineTo(new Point(part.Left, part.Top + r));
                    g.ArcTo(new Point(part.Right, part.Top + r), new Size(r, r), 0, false, SweepDirection.Clockwise);
                    g.LineTo(new Point(part.Right, part.Bottom - 0.75));
                }
                else
                {
                    g.BeginFigure(new Point(part.Left, part.Top + 0.75), true);
                    g.LineTo(new Point(part.Right, part.Top + 0.75));
                    g.LineTo(new Point(part.Right, part.Bottom - r));
                    g.ArcTo(new Point(part.Left, part.Bottom - r), new Size(r, r), 0, false, SweepDirection.Clockwise);
                }
                g.EndFigure(true);
            }
            if (on)
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(ember.Color, 0.3), 6), shape);
            ctx.DrawGeometry(fill, null, shape);

            // Tag to the right.
            var tag = BrandBrushes.Text(index == 0 ? "LOWER" : "UPPER", mono, 10, on ? ember : muted);
            double tagX = cx + w / 2 + 18;
            double cy = part.Center.Y;
            ctx.DrawLine(new Pen(on ? ember : line, 1), new Point(cx + rw / 2 + 6, cy), new Point(tagX - 6, cy));
            ctx.DrawEllipse(on ? ember : line, null, new Point(cx + rw / 2 + 6, cy), 2, 2);
            ctx.DrawText(tag, new Point(tagX, cy - tag.Height / 2));
        }
    }
}
