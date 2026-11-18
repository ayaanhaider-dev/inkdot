using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Lveno.App.Controls;

namespace Lveno.App.Overlay;

public enum ScreenEdge { Left, Right, Top, Bottom }

/// <summary>
/// Draws the push-to-switch animation on one screen:
///   resistance  – an ember glow along the edge that stretches and bulges as you push
///   break       – the glow springs out and fades when the switch happens
///   release     – it eases away if you pull back early
///   arrive      – a matching glow where the cursor enters, the dot peeking in, and a screen-name pill
/// Runs at 60 fps only while something is animating.
/// </summary>
public sealed class OverlayCanvas : Control
{
    private const double BreakMs = 180, ReleaseMs = 160, ArriveMs = 380, DotMs = 650, PillMs = 1000;

    private static readonly Color Ember = Color.Parse("#F2551D");
    private static readonly Color Ink = Color.Parse("#121212");
    private static readonly Color Paper = Color.Parse("#F4F4F1");

    private enum BarState { Pushing, Releasing, Breaking }

    private sealed class Bar
    {
        public ScreenEdge Edge;
        public double Along, Target, Shown, From;
        public BarState State;
        public long Since;
    }

    private Bar? _bar;
    private (ScreenEdge Edge, double Along, long Start)? _arrive;
    private (ScreenEdge Edge, double Along, Point Look, long Start)? _dot;
    private (string Text, Point At, long Start)? _pill;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;

    /// <summary>Raised when nothing is left to draw, so the window can hide.</summary>
    public event Action? Idle;

    public OverlayCanvas()
    {
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
    }

    private long? _frozenAt; // dev previews: draw a frame at a fixed moment
    private long Now => _frozenAt ?? _clock.ElapsedMilliseconds;

    /// <summary>Dev only: show a push at a given strength, frozen.</summary>
    public void PreviewPush(ScreenEdge edge, double along, double progress)
    {
        _frozenAt = 0;
        _bar = new Bar { Edge = edge, Along = along, Target = progress, Shown = progress, State = BarState.Pushing };
    }

    /// <summary>Dev only: show the arrival, frozen <paramref name="msIn"/> after it started.</summary>
    public void PreviewArrive(ScreenEdge edge, Point entry, string name, long msIn)
    {
        _frozenAt = 0;
        Arrive(edge, entry, name);
        _timer.Stop();
        _frozenAt = msIn;
    }

    // ---- Commands -------------------------------------------------------------------------------

    public void Push(ScreenEdge edge, double along, double progress)
    {
        if (_bar is null || _bar.Edge != edge || _bar.State != BarState.Pushing)
            _bar = new Bar { Edge = edge, Along = along, Shown = 0, State = BarState.Pushing, Since = Now };
        _bar.Along = along;
        _bar.Target = Math.Clamp(progress, 0, 1);
        Start();
    }

    public void Release()
    {
        if (_bar is not { State: BarState.Pushing } bar) return;
        bar.State = BarState.Releasing;
        bar.From = bar.Shown;
        bar.Since = Now;
        Start();
    }

    public void Break()
    {
        _bar ??= new Bar { Shown = 1 };
        _bar.State = BarState.Breaking;
        _bar.From = Math.Max(_bar.Shown, 0.85);
        _bar.Since = Now;
        Start();
    }

    public void Arrive(ScreenEdge edge, Point entry, string name)
    {
        double along = edge is ScreenEdge.Top or ScreenEdge.Bottom ? entry.X : entry.Y;
        _arrive = (edge, along, Now);
        _dot = (edge, along, entry, Now);
        var inward = ToLocal(edge, along, 46);
        _pill = (name, inward, Now);
        Start();
    }

    private void Start()
    {
        if (!_timer.IsEnabled) _timer.Start();
        InvalidateVisual();
    }

    private void Tick()
    {
        long now = Now;
        if (_bar is { } bar)
        {
            if (bar.State == BarState.Pushing)
                bar.Shown += (bar.Target - bar.Shown) * 0.35; // follow the push smoothly
            else if (now - bar.Since > (bar.State == BarState.Breaking ? BreakMs : ReleaseMs))
                _bar = null;
        }
        if (_arrive is { } a && now - a.Start > ArriveMs) _arrive = null;
        if (_dot is { } d && now - d.Start > DotMs) _dot = null;
        if (_pill is { } p && now - p.Start > PillMs) _pill = null;

        if (_bar is null && _arrive is null && _dot is null && _pill is null)
        {
            _timer.Stop();
            InvalidateVisual();
            Idle?.Invoke();
            return;
        }
        InvalidateVisual();
    }

    // ---- Drawing --------------------------------------------------------------------------------

    public override void Render(DrawingContext ctx)
    {
        long now = Now;

        if (_bar is { } bar)
        {
            double t;
            switch (bar.State)
            {
                case BarState.Pushing:
                    DrawGlow(ctx, bar.Edge, bar.Along, bar.Shown, 1, 1);
                    break;
                case BarState.Releasing:
                    t = Math.Clamp((now - bar.Since) / ReleaseMs, 0, 1);
                    DrawGlow(ctx, bar.Edge, bar.Along, bar.From * (1 - EaseOut(t)), 1 - t, 1);
                    break;
                case BarState.Breaking:
                    // A quick spring: the bulge overshoots, the glow stretches wide, then it's gone.
                    t = Math.Clamp((now - bar.Since) / BreakMs, 0, 1);
                    double spring = bar.From * (1 - t) + Math.Sin(t * Math.PI) * 0.45;
                    DrawGlow(ctx, bar.Edge, bar.Along, spring, 1 - EaseIn(t), 1 + 0.9 * EaseOut(t));
                    break;
            }
        }

        if (_arrive is { } a)
        {
            double t = Math.Clamp((now - a.Start) / ArriveMs, 0, 1);
            double alpha = t < 0.25 ? t / 0.25 : 1 - (t - 0.25) / 0.75;
            DrawGlow(ctx, a.Edge, a.Along, 0.55 * (1 - t) + 0.15, alpha, 1.1);
        }

        if (_dot is { } d) DrawDot(ctx, d.Edge, d.Along, d.Look, Math.Clamp((now - d.Start) / DotMs, 0, 1));
        if (_pill is { } p) DrawPill(ctx, p.Text, p.At, Math.Clamp((now - p.Start) / PillMs, 0, 1));
    }

    /// <summary>The elastic edge glow. Drawn as if on the top edge, then turned onto the real one.</summary>
    private void DrawGlow(DrawingContext ctx, ScreenEdge edge, double along, double strength, double alpha, double stretch)
    {
        if (alpha <= 0.01) return;
        strength = Math.Max(0, strength);
        double length = (70 + 200 * Math.Min(strength, 1.2)) * stretch;
        double depth = 3 + 16 * strength;

        using (ctx.PushTransform(EdgeMatrix(edge)))
        {
            // Soft halo, then the membrane, then a bright core line along the edge.
            DrawMembrane(ctx, along, length * 1.35, depth * 2.2, alpha * 0.22);
            DrawMembrane(ctx, along, length, depth, alpha * 0.85);
            var core = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(WithAlpha(Ember, 0), 0),
                    new GradientStop(WithAlpha(Paper, alpha * 0.9), 0.5),
                    new GradientStop(WithAlpha(Ember, 0), 1),
                },
            };
            ctx.DrawRectangle(core, null, new Rect(along - length * 0.32, 0, length * 0.64, 2.2), 1.1, 1.1);
        }
    }

    private static void DrawMembrane(DrawingContext ctx, double along, double length, double depth, double alpha)
    {
        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(new Point(along - length / 2, 0), true);
            g.CubicBezierTo(
                new Point(along - length * 0.22, 0),
                new Point(along - length * 0.18, depth * 1.6),
                new Point(along, depth * 1.6));
            g.CubicBezierTo(
                new Point(along + length * 0.18, depth * 1.6),
                new Point(along + length * 0.22, 0),
                new Point(along + length / 2, 0));
            g.EndFigure(true);
        }
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(WithAlpha(Ember, alpha), 0),
                new GradientStop(WithAlpha(Ember, alpha * 0.35), 0.6),
                new GradientStop(WithAlpha(Ember, 0), 1),
            },
        };
        ctx.DrawGeometry(fill, null, shape);
    }

    /// <summary>The Lveno dot peeks in from the edge, looks at the cursor, and rolls back out.</summary>
    private void DrawDot(DrawingContext ctx, ScreenEdge edge, double along, Point look, double t)
    {
        const double r = 15;
        double inward = t < 0.3 ? -r + r * 2.3 * EaseOut(t / 0.3)
            : t < 0.62 ? r * 1.3
            : r * 1.3 - r * 2.3 * EaseIn((t - 0.62) / 0.38);
        if (inward < -r) return;

        var center = ToLocal(edge, along + 54, inward);
        var toCursor = look - center;
        double len = Math.Max(1, Math.Sqrt(toCursor.X * toCursor.X + toCursor.Y * toCursor.Y));
        var gaze = new Vector(toCursor.X / len, toCursor.Y / len);

        // Keep the dot from drawing outside the screen it's peeking into.
        using (ctx.PushClip(new Rect(Bounds.Size)))
            DotRenderer.Draw(ctx, center, r, DotExpression.Look, gaze, 0);
    }

    private void DrawPill(DrawingContext ctx, string text, Point at, double t)
    {
        double alpha = t < 0.12 ? t / 0.12 : t > 0.8 ? 1 - (t - 0.8) / 0.2 : 1;
        if (alpha <= 0.01) return;

        var label = BrandBrushes.Text(text, BrandBrushes.Font(this, FontWeight.Bold), 13, new SolidColorBrush(WithAlpha(Paper, alpha)));
        double w = label.Width + 28, h = label.Height + 14;
        double lift = (1 - EaseOut(Math.Min(1, t / 0.12))) * 6;
        var x = Math.Clamp(at.X - w / 2, 8, Bounds.Width - w - 8);
        var y = Math.Clamp(at.Y - h / 2 + lift, 8, Bounds.Height - h - 8);
        var box = new Rect(x, y, w, h);
        ctx.DrawRectangle(new SolidColorBrush(WithAlpha(Ink, 0.92 * alpha)),
            new Pen(new SolidColorBrush(WithAlpha(Ember, 0.6 * alpha)), 1), box, h / 2, h / 2);
        ctx.DrawText(label, new Point(x + 14, y + 7));
    }

    // ---- Geometry helpers ------------------------------------------------------------------------

    /// <summary>Maps (along the edge, distance in from it) to window coordinates for each edge.</summary>
    private Matrix EdgeMatrix(ScreenEdge edge) => edge switch
    {
        ScreenEdge.Top => Matrix.Identity,
        ScreenEdge.Bottom => new Matrix(1, 0, 0, -1, 0, Bounds.Height),
        ScreenEdge.Left => new Matrix(0, 1, 1, 0, 0, 0),
        _ => new Matrix(0, 1, -1, 0, Bounds.Width, 0),
    };

    private Point ToLocal(ScreenEdge edge, double along, double inward) =>
        new Point(along, inward).Transform(EdgeMatrix(edge));

    private static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);
    private static double EaseOut(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);
    private static double EaseIn(double t) => Math.Pow(Math.Clamp(t, 0, 1), 3);
}
