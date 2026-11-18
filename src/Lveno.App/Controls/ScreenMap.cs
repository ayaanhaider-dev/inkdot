using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lveno.Plugins;

namespace Lveno.App.Controls;

/// <summary>
/// Your screens, laid out the way macOS arranges them and numbered left to right.
/// The main screen has a menu bar strip; the screen your pointer is on has a cursor mark.
/// Click a screen to draw on it.
/// </summary>
public class ScreenMap : Control
{
    public const int AllScreens = -1;
    public const int Custom = -2;

    private IReadOnlyList<ScreenInfo> _displays = [];
    private IReadOnlyDictionary<uint, string> _names = new Dictionary<uint, string>();
    private IReadOnlyDictionary<uint, Avalonia.Media.Imaging.Bitmap> _wallpapers = new Dictionary<uint, Avalonia.Media.Imaging.Bitmap>();
    private int _selected = Custom;
    private int _hover = -1;
    private int _cursor = -1;
    private readonly List<Rect> _rects = [];

    private static readonly Geometry CursorArrow =
        StreamGeometry.Parse("M0,0 L0,17 L4.6,12.6 L7.6,19.6 L10.4,18.4 L7.4,11.6 L13.6,11.6 Z");

    public event EventHandler<int>? SelectionChanged;

    public ScreenMap()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public IReadOnlyList<ScreenInfo> Displays
    {
        get => _displays;
        set { _displays = value; InvalidateVisual(); }
    }

    public IReadOnlyDictionary<uint, string> Names
    {
        get => _names;
        set { _names = value; InvalidateVisual(); }
    }

    /// <summary>Each screen's wallpaper, by display ID.</summary>
    public IReadOnlyDictionary<uint, Avalonia.Media.Imaging.Bitmap> Wallpapers
    {
        get => _wallpapers;
        set { _wallpapers = value; InvalidateVisual(); }
    }

    /// <summary>Index of the chosen screen, <see cref="AllScreens"/>, or <see cref="Custom"/>.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set { _selected = value; InvalidateVisual(); }
    }

    /// <summary>Index of the screen the pointer is on, or -1.</summary>
    public int CursorIndex
    {
        get => _cursor;
        set
        {
            if (_cursor == value) return;
            _cursor = value;
            InvalidateVisual();
        }
    }

    private void Layout()
    {
        _rects.Clear();
        if (_displays.Count == 0 || Bounds.Width <= 0)
            return;

        double left = _displays.Min(d => d.X), top = _displays.Min(d => d.Y);
        double right = _displays.Max(d => d.X + d.Width), bottom = _displays.Max(d => d.Y + d.Height);
        double pad = 14;
        double scale = Math.Min((Bounds.Width - pad * 2) / (right - left), (Bounds.Height - pad * 2) / (bottom - top));
        double offsetX = (Bounds.Width - (right - left) * scale) / 2, offsetY = (Bounds.Height - (bottom - top) * scale) / 2;

        foreach (var d in _displays)
        {
            var rect = new Rect(
                offsetX + (d.X - left) * scale,
                offsetY + (d.Y - top) * scale,
                d.Width * scale,
                d.Height * scale);
            _rects.Add(rect.Deflate(3)); // a small gap between screens that touch
        }
    }

    public override void Render(DrawingContext context)
    {
        Layout();
        var fg = BrandBrushes.Get(this, "Lv.Fg", "#121212");
        var bg = BrandBrushes.Get(this, "Lv.Bg", "#F4F4F1");
        var muted = BrandBrushes.Get(this, "Lv.Muted", "#6C6C66");
        var line = BrandBrushes.Get(this, "Lv.Line", "#DADAD4");
        var hover = BrandBrushes.Get(this, "Lv.Hover", "#EAEAE5");
        var ember = (ISolidColorBrush)BrandBrushes.Get(this, "Lv.Ember", "#F2551D");
        var bold = BrandBrushes.Font(this, FontWeight.Bold);
        var regular = BrandBrushes.Font(this);
        var mono = BrandBrushes.Mono(this);

        for (int i = 0; i < _rects.Count; i++)
        {
            try
            {
                DrawScreen(context, i, fg, bg, muted, line, hover, ember, bold, regular, mono);
            }
            catch (Exception ex)
            {
                // One screen failing to draw must not blank the others.
                Console.Error.WriteLine($"ScreenMap: screen {i + 1}: {ex.Message}");
            }
        }
    }

    private void DrawScreen(DrawingContext context, int i, IBrush fg, IBrush bg, IBrush muted, IBrush line, IBrush hover,
        ISolidColorBrush ember, Typeface bold, Typeface regular, Typeface mono)
    {
        {
            var rect = _rects[i];
            var d = _displays[i];
            bool on = _selected == AllScreens || _selected == i;
            bool isHover = _hover == i && !on;
            var paper = new SolidColorBrush(Color.Parse("#F4F4F1"));
            var shade = new SolidColorBrush(Color.Parse("#121212"), 0.72);
            double radius = 5;

            // The screen's own picture (its wallpaper), cropped to fill; others dimmed so the chosen one stands out.
            // Drawn as a rounded image fill (no clipping: clips misbehave in this renderer).
            context.DrawRectangle(bg, null, rect, radius, radius);
            if (_wallpapers.TryGetValue(d.Id, out var image))
            {
                // Crop the picture to the screen's shape ("fill"), then draw it into the screen.
                double sw = image.Size.Width, sh = image.Size.Height;
                double cut = Math.Min(sw / rect.Width, sh / rect.Height);
                var source = new Rect((sw - rect.Width * cut) / 2, (sh - rect.Height * cut) / 2, rect.Width * cut, rect.Height * cut);
                context.DrawImage(image, source, rect.Deflate(1));
                context.DrawRectangle(new SolidColorBrush(Colors.Black, on ? 0.05 : isHover ? 0.25 : 0.45), null, rect, radius, radius);
            }
            // Main screen: a menu bar along the top, as in macOS display settings.
            if (d.IsMain && rect.Height > 30)
            {
                double barH = Math.Clamp(rect.Height * 0.06, 4, 7);
                context.DrawRectangle(new SolidColorBrush(Colors.White, 0.55), null,
                    new Rect(rect.X + 3, rect.Y + 3, rect.Width - 6, barH), barH / 2, barH / 2);
            }
            context.DrawRectangle(null, on ? new Pen(ember, 2.5) : new Pen(isHover ? muted : line, isHover ? 1.5 : 1), rect, radius, radius);

            // Number badge, top-left.
            double inset = Math.Clamp(rect.Width * 0.06, 6, 10);
            var number = BrandBrushes.Text((i + 1).ToString(), bold, Math.Clamp(rect.Height * 0.14, 11, 16), paper);
            double badge = number.Height + 6;
            var badgeRect = new Rect(rect.Left + inset, rect.Top + inset + 4, Math.Max(badge, number.Width + 12), badge);
            context.DrawRectangle(on ? ember : shade, null, badgeRect, badge / 2, badge / 2);
            context.DrawText(number, new Point(badgeRect.Center.X - number.Width / 2, badgeRect.Center.Y - number.Height / 2));

            // Name and size, bottom-left, on a dark tag so they read on any wallpaper.
            if (rect.Width > 90 && rect.Height > 60)
            {
                string text = _names.TryGetValue(d.Id, out var name) ? name : $"{d.Width:0} × {d.Height:0}";
                var label = BrandBrushes.Text(text, regular, 11, paper);
                label.MaxTextWidth = rect.Width - inset * 2 - 16;
                label.MaxLineCount = 1;
                label.Trimming = TextTrimming.CharacterEllipsis;
                var tag = new Rect(rect.Left + inset, rect.Bottom - inset - label.Height - 6, label.Width + 14, label.Height + 6);
                context.DrawRectangle(shade, null, tag, 6, 6);
                context.DrawText(label, new Point(tag.X + 7, tag.Y + 3));
            }

            // The screen your pointer is on now.
            if (i == _cursor)
            {
                double s = Math.Clamp(rect.Height * 0.16, 10, 16) / 19.6;
                var at = new Point(rect.Right - inset - 13.6 * s, rect.Bottom - inset * 0.7 - 19.6 * s);
                using (context.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(at.X, at.Y)))
                    context.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 1.5 / s), CursorArrow);
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int hit = HitTest(e.GetPosition(this));
        if (hit != _hover)
        {
            _hover = hit;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        int hit = HitTest(e.GetPosition(this));
        if (hit >= 0)
        {
            SelectedIndex = hit;
            SelectionChanged?.Invoke(this, hit);
        }
    }

    private int HitTest(Point p)
    {
        for (int i = 0; i < _rects.Count; i++)
        {
            if (_rects[i].Contains(p))
                return i;
        }
        return -1;
    }
}
