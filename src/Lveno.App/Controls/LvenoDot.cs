using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lveno.App.Controls;

/// <summary>
/// The dot as a live character: it blinks now and then and its eyes follow your pen.
/// Use one per view. The brand never shows the dot twice.
/// </summary>
public class LvenoDot : Control
{
    public static readonly StyledProperty<DotExpression> ExpressionProperty =
        AvaloniaProperty.Register<LvenoDot, DotExpression>(nameof(Expression));

    static LvenoDot()
    {
        AffectsRender<LvenoDot>(ExpressionProperty);
    }

    public DotExpression Expression
    {
        get => GetValue(ExpressionProperty);
        set => SetValue(ExpressionProperty, value);
    }

    private static readonly Vector RestingLook = new(-0.62, 0.88);
    private Vector _look = RestingLook, _lookTarget = RestingLook;
    private double _blink;
    private DateTime _blinkStarted = DateTime.MinValue, _nextBlink = DateTime.UtcNow.AddSeconds(3);
    private DateTime _lastLookInput = DateTime.MinValue;
    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();

    public LvenoDot()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    /// <summary>Point the eyes at a position given in this control's coordinates.</summary>
    public void LookAt(Point point)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var delta = point - center;
        double reach = Math.Max(120, Math.Max(Bounds.Width, Bounds.Height) * 3);
        _lookTarget = new Vector(Math.Clamp(delta.X / reach, -1, 1), Math.Clamp(delta.Y / reach, -1, 1));
        _lastLookInput = DateTime.UtcNow;
    }

    /// <summary>Blink once, right now.</summary>
    public void Blink() => _blinkStarted = DateTime.UtcNow;

    private void Tick()
    {
        var now = DateTime.UtcNow;
        bool changed = false;

        // Drift back to the resting look when nothing has moved for a while.
        if (now - _lastLookInput > TimeSpan.FromSeconds(2.5))
            _lookTarget = RestingLook;

        var next = _look + (_lookTarget - _look) * 0.18;
        if ((next - _look).Length > 0.001)
        {
            _look = next;
            changed = true;
        }

        if (now >= _nextBlink)
        {
            Blink();
            _nextBlink = now.AddSeconds(3 + _random.NextDouble() * 4);
        }

        double t = (now - _blinkStarted).TotalMilliseconds / 160.0;
        double blink = t is >= 0 and <= 1 ? Math.Sin(t * Math.PI) : 0;
        if (Math.Abs(blink - _blink) > 0.001)
        {
            _blink = blink;
            changed = true;
        }

        if (changed)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;

        // Brand SVGs leave 10/120 of air around the dot; keep that so sizes match the files.
        double r = size * 50.0 / 120.0;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        DotRenderer.Draw(context, center, r, Expression, _look, _blink);
    }
}
