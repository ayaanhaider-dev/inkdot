using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenTabletDriver.Plugin;

namespace Lveno.App.Controls;

/// <summary>
/// A drawing of the tablet with the part of it that's active. Sizes are in millimetres.
/// </summary>
public class TabletPreview : Control
{
    private float _tabletWidth = 152, _tabletHeight = 95;
    private Area? _area;

    public TabletPreview()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public void SetTablet(float widthMm, float heightMm)
    {
        _tabletWidth = Math.Max(1, widthMm);
        _tabletHeight = Math.Max(1, heightMm);
        InvalidateVisual();
    }

    /// <summary>The active area, center-based, in millimetres from the tablet's top-left.</summary>
    public Area? ActiveArea
    {
        get => _area;
        set
        {
            _area = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var fg = BrandBrushes.Get(this, "Lv.Fg", "#121212");
        var bg = BrandBrushes.Get(this, "Lv.Bg", "#F4F4F1");
        var muted = BrandBrushes.Get(this, "Lv.Muted", "#6C6C66");
        var line = BrandBrushes.Get(this, "Lv.Line", "#DADAD4");
        var mono = BrandBrushes.Mono(this);

        // The tablet body is a little bigger than its drawing surface.
        double bezel = 0.09;
        double bodyW = _tabletWidth * (1 + bezel * 2), bodyH = _tabletHeight * (1 + bezel * 2.4);
        double scale = Math.Min((Bounds.Width - 4) / bodyW, (Bounds.Height - 4) / bodyH);
        var body = new Rect(
            (Bounds.Width - bodyW * scale) / 2,
            (Bounds.Height - bodyH * scale) / 2,
            bodyW * scale,
            bodyH * scale);
        context.DrawRectangle(bg, new Pen(line, 1.5), body, 16, 16);

        var surface = new Rect(
            body.X + _tabletWidth * bezel * scale,
            body.Y + _tabletHeight * bezel * 1.2 * scale,
            _tabletWidth * scale,
            _tabletHeight * scale);
        context.DrawRectangle(null, new Pen(line, 1, new DashStyle([4, 4], 0)), surface, 6, 6);

        if (_area is null)
            return;

        var active = new Rect(
            surface.X + (_area.Position.X - _area.Width / 2) * scale,
            surface.Y + (_area.Position.Y - _area.Height / 2) * scale,
            _area.Width * scale,
            _area.Height * scale);

        var tint = fg is ISolidColorBrush solid
            ? new SolidColorBrush(solid.Color, 0.07)
            : (IBrush)Brushes.Transparent;
        context.DrawRectangle(tint, new Pen(fg, 1.5), active, 4, 4);

        var label = BrandBrushes.Text($"{_area.Width:0.0} × {_area.Height:0.0} mm", mono, 11, muted);
        if (label.Width + 16 < active.Width && label.Height + 12 < active.Height)
            context.DrawText(label, new Point(active.Center.X - label.Width / 2, active.Center.Y - label.Height / 2));
    }
}
