using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lveno.Plugins;

namespace Lveno.App.Controls;

/// <summary>
/// Pressure curve editor. Three handles: start (slides along bottom/left edge), end (top/right edge)
/// and the middle handle, which bends the curve and moves freely. An ember dot shows your press now.
/// </summary>
public class CurveView : Control
{
    private PressureCurve _curve = PressureCurve.Linear;
    private float _live = -1;
    private int _hover = -1, _drag = -1; // 0 start, 1 control, 2 end

    public event EventHandler? CurveChanged;

    public CurveView() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    public PressureCurve Curve
    {
        get => _curve;
        set { _curve = value.Normalized(); InvalidateVisual(); }
    }

    /// <summary>Raw pen pressure 0–1, or −1 when the pen is lifted.</summary>
    public void SetLive(float raw) { _live = raw; InvalidateVisual(); }

    private Rect Plot => new Rect(Bounds.Size).Deflate(10);
    private Point ToScreen(float x, float y) => new(Plot.Left + x * Plot.Width, Plot.Bottom - y * Plot.Height);

    private Point[] Handles => [
        ToScreen(_curve.StartX, _curve.StartY),
        ToScreen(_curve.ControlX, _curve.ControlY),
        ToScreen(_curve.EndX, _curve.EndY)];

    private int HitTest(Point p)
    {
        var handles = Handles;
        int best = -1;
        double bestDistance = 18; // generous, so the pen grabs easily
        for (int i = 0; i < 3; i++)
        {
            double d = Point.Distance(handles[i], p);
            if (d < bestDistance) { best = i; bestDistance = d; }
        }
        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _drag = HitTest(e.GetPosition(this));
        if (_drag < 0) return;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_drag < 0)
        {
            int hover = HitTest(p);
            if (hover != _hover)
            {
                _hover = hover;
                Cursor = hover >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
                InvalidateVisual();
            }
            return;
        }

        float x = (float)Math.Clamp((p.X - Plot.Left) / Plot.Width, 0, 1);
        float y = (float)Math.Clamp((Plot.Bottom - p.Y) / Plot.Height, 0, 1);
        var c = _curve;
        _curve = (_drag switch
        {
            // Start rides the bottom edge or the left edge, whichever the pen is nearer.
            0 => y < x ? c with { StartX = x, StartY = 0 } : c with { StartX = 0, StartY = y },
            // End rides the top edge or the right edge.
            2 => (1 - y) < (1 - x) ? c with { EndX = x, EndY = 1 } : c with { EndX = 1, EndY = y },
            _ => c with { ControlX = x, ControlY = y },
        }).Normalized();

        InvalidateVisual();
        CurveChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag < 0) return;
        _drag = -1;
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var fg = BrandBrushes.Get(this, "Lv.Fg", "#121212");
        var bg = BrandBrushes.Get(this, "Lv.Bg", "#F4F4F1");
        var line = BrandBrushes.Get(this, "Lv.Line", "#DADAD4");
        var ember = BrandBrushes.Get(this, "Lv.Ember", "#F2551D");
        var r = Plot;
        if (r.Width <= 0 || r.Height <= 0) return;

        ctx.DrawRectangle(bg, new Pen(line, 1), r, 8, 8);
        var grid = new Pen(line, 1);
        for (int i = 1; i < 8; i++)
        {
            double gx = r.Left + r.Width * i / 8, gy = r.Top + r.Height * i / 8;
            ctx.DrawLine(grid, new Point(gx, r.Top), new Point(gx, r.Bottom));
            ctx.DrawLine(grid, new Point(r.Left, gy), new Point(r.Right, gy));
        }

        // Curve, with a soft fill under it.
        var curve = new List<Point>();
        for (int i = 0; i <= 160; i++)
        {
            float x = i / 160f;
            curve.Add(ToScreen(x, x <= 0 ? (_curve.StartX > 0 ? 0 : _curve.StartY) : _curve.Apply(x)));
        }

        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(r.BottomLeft, true);
            foreach (var p in curve) g.LineTo(p);
            g.LineTo(r.BottomRight);
            g.EndFigure(true);
        }
        var emberColor = ((ISolidColorBrush)ember).Color;
        ctx.DrawGeometry(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(70, emberColor.R, emberColor.G, emberColor.B), 0), new GradientStop(Color.FromArgb(10, emberColor.R, emberColor.G, emberColor.B), 1) },
        }, null, fill);

        var stroke = new StreamGeometry();
        using (var g = stroke.Open())
        {
            g.BeginFigure(curve[0], false);
            foreach (var p in curve.Skip(1)) g.LineTo(p);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(ember, 3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), stroke);

        // Thin guide from the ends to the middle handle, like a bezier handle.
        var h = Handles;
        var guide = new Pen(BrandBrushes.Get(this, "Lv.Muted", "#6C6C66"), 1, new DashStyle([3, 3], 0));
        ctx.DrawLine(guide, h[0], h[1]);
        ctx.DrawLine(guide, h[1], h[2]);

        for (int i = 0; i < 3; i++)
        {
            bool active = i == _hover || i == _drag;
            double size = active ? 9 : 7.5;
            ctx.DrawEllipse(active ? ember : bg, new Pen(ember, 2.5), h[i], size, size);
        }

        if (_live > 0)
            ctx.DrawEllipse(fg, new Pen(bg, 2), ToScreen(_live, _curve.Apply(_live)), 5, 5);
    }
}
