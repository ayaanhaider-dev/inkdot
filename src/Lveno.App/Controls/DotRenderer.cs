using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Lveno.App.Controls;

/// <summary>The dot's moods, from brand/logo/expressions.</summary>
public enum DotExpression
{
    /// <summary>Default. Looking around.</summary>
    Look,
    /// <summary>Success, launches.</summary>
    Happy,
    /// <summary>Loading, working.</summary>
    Focused,
    /// <summary>Easter eggs, calls to action.</summary>
    Wink,
    /// <summary>Empty states.</summary>
    Sleep,
}

/// <summary>
/// Draws the Lveno dot. Geometry is taken from the brand SVGs and expressed in units of the
/// dot's radius, so the same code draws the 120px character and the dot inside the wordmark.
/// The body is always a perfect circle in Ember; eyes are Paper with Ink pupils.
/// </summary>
public static class DotRenderer
{
    private static readonly IBrush Ember = new ImmutableSolidColorBrush(Color.Parse("#F2551D"));
    private static readonly IBrush Paper = new ImmutableSolidColorBrush(Color.Parse("#F4F4F1"));
    private static readonly IBrush Ink = new ImmutableSolidColorBrush(Color.Parse("#121212"));

    /// <param name="look">Where the pupils point, each axis −1…1. (−0.62, 0.88) matches the brand "look".</param>
    /// <param name="blink">0 = eyes open, 1 = shut.</param>
    public static void Draw(DrawingContext ctx, Point center, double r, DotExpression expression, Vector look, double blink)
    {
        ctx.DrawEllipse(Ember, null, center, r, r);

        switch (expression)
        {
            case DotExpression.Look:
                Eye(ctx, center, r, -1, eyeRy: 0.28, eyeDy: -0.12, look, blink);
                Eye(ctx, center, r, +1, eyeRy: 0.28, eyeDy: -0.12, look, blink);
                break;

            case DotExpression.Focused:
                Eye(ctx, center, r, -1, eyeRy: 0.16, eyeDy: -0.04, look * 0.5, blink, pupilRy: 0.10);
                Eye(ctx, center, r, +1, eyeRy: 0.16, eyeDy: -0.04, look * 0.5, blink, pupilRy: 0.10);
                break;

            case DotExpression.Happy:
                Arc(ctx, center, r, -0.52, 0.00, -0.30, -0.40, -0.08, 0.00);
                Arc(ctx, center, r, 0.08, 0.00, 0.30, -0.40, 0.52, 0.00);
                break;

            case DotExpression.Sleep:
                Arc(ctx, center, r, -0.52, -0.04, -0.30, 0.12, -0.08, -0.04);
                Arc(ctx, center, r, 0.08, -0.04, 0.30, 0.12, 0.52, -0.04);
                break;

            case DotExpression.Wink:
                Eye(ctx, center, r, -1, eyeRy: 0.28, eyeDy: -0.12, look, blink);
                Arc(ctx, center, r, 0.08, -0.08, 0.30, 0.08, 0.52, -0.08);
                break;
        }
    }

    private static void Eye(DrawingContext ctx, Point c, double r, int side, double eyeRy, double eyeDy,
        Vector look, double blink, double pupilRy = 0.14)
    {
        double open = Math.Clamp(1 - blink, 0.08, 1);
        var eye = new Point(c.X + side * 0.30 * r, c.Y + eyeDy * r);
        ctx.DrawEllipse(Paper, null, eye, 0.21 * r, eyeRy * r * open);

        // Keep the pupil inside the white of the eye.
        double maxX = (0.21 - 0.104) * r, maxY = Math.Max(0, (eyeRy - pupilRy) * r * open);
        var offset = new Vector(Math.Clamp(look.X, -1, 1) * maxX * 0.75, Math.Clamp(look.Y, -1, 1) * maxY);
        ctx.DrawEllipse(Ink, null, eye + offset, 0.104 * r, pupilRy * r * open);
    }

    private static void Arc(DrawingContext ctx, Point c, double r,
        double x0, double y0, double cx, double cy, double x1, double y1)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(c.X + x0 * r, c.Y + y0 * r), false);
            g.QuadraticBezierTo(new Point(c.X + cx * r, c.Y + cy * r), new Point(c.X + x1 * r, c.Y + y1 * r));
            g.EndFigure(false);
        }

        var pen = new Pen(Paper, 0.14 * r, lineCap: PenLineCap.Round);
        ctx.DrawGeometry(null, pen, geometry);
    }
}
