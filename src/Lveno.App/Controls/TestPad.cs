using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Lveno.App.Controls;

/// <summary>
/// Scribble here to feel the curve. Line width follows the pressure the driver reports,
/// run through the same curve your apps get.
/// </summary>
public class TestPad : Control
{
    private readonly List<List<(Point P, double W)>> _strokes = [];
    private List<(Point, double)>? _current;

    /// <summary>Pressure (after the curve) to use for the next point, 0–1.</summary>
    public Func<double>? PressureSource { get; set; }

    public TestPad()
    {
        Cursor = new Cursor(StandardCursorType.Cross);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public void Clear()
    {
        _strokes.Clear();
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _current = [];
        _strokes.Add(_current);
        Add(e.GetCurrentPoint(this));
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_current == null) return;
        Add(e.GetCurrentPoint(this));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _current = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void Add(PointerPoint point)
    {
        // Prefer the driver's reading; fall back to what macOS reports for the event.
        double p = PressureSource?.Invoke() ?? point.Properties.Pressure;
        if (p < 0) p = point.Properties.Pressure;
        _current!.Add((point.Position, 0.6 + p * 14));
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        var bg = BrandBrushes.Get(this, "Lv.Bg", "#F4F4F1");
        var line = BrandBrushes.Get(this, "Lv.Line", "#DADAD4");
        var fg = BrandBrushes.Get(this, "Lv.Fg", "#121212");
        ctx.DrawRectangle(bg, new Pen(line, 1), new Rect(Bounds.Size), 10, 10);

        if (_strokes.Count == 0)
        {
            var hint = BrandBrushes.Text("Draw here.", BrandBrushes.Font(this), 14, BrandBrushes.Get(this, "Lv.Muted", "#6C6C66"));
            ctx.DrawText(hint, new Point((Bounds.Width - hint.Width) / 2, (Bounds.Height - hint.Height) / 2));
        }

        using (ctx.PushClip(new Rect(Bounds.Size)))
        {
            foreach (var stroke in _strokes)
            {
                for (int i = 1; i < stroke.Count; i++)
                {
                    var (a, wa) = stroke[i - 1];
                    var (b, wb) = stroke[i];
                    ctx.DrawLine(new Pen(fg, (wa + wb) / 2, lineCap: PenLineCap.Round), a, b);
                }
                if (stroke.Count == 1)
                    ctx.DrawEllipse(fg, null, stroke[0].P, stroke[0].W / 2, stroke[0].W / 2);
            }
        }
    }
}
