using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace Lveno.Plugins;

/// <summary>
/// Windows-style pen scrolling:
///   drag            → the page follows the pen
///   flick and lift  → it keeps gliding, then slows down
///   quick tap       → a click
///   hold still, drag → a normal press-and-drag (select text, move files)
///
/// Two plugins share this engine: <see cref="PenScrollTipBinding"/> replaces the pen tip and reports
/// touch and lift; <see cref="PenScrollFilter"/> sees every pen position (in screen points) and decides.
/// </summary>
public static class PenScrollEngine
{
    private enum Mode { Idle, Deciding, Scrolling, Dragging }

    private const float MoveToScroll = 7f;          // points of travel before a touch becomes a scroll
    private const long HoldToDragMs = 400;          // holding still this long makes it a normal drag
    private const float MinFlickSpeed = 250f;       // points per second needed to keep gliding after lift
    private const float FrictionPerFrame = 0.94f;   // how quickly a glide slows down (60 frames per second)

    private static readonly object Gate = new();
    private static Mode _mode;
    private static IMouseButtonHandler? _mouse;
    private static Vector2 _anchor, _last;
    private static bool _haveAnchor, _releaseClick;
    private static readonly Stopwatch Since = Stopwatch.StartNew();
    private static long _downAt, _lastAt;
    private static Vector2 _velocity; // points per second
    private static Vector2 _remainder;
    private static Timer? _glide;

    /// <summary>The pen touched the tablet.</summary>
    public static void TipDown(IMouseButtonHandler? mouse)
    {
        lock (Gate)
        {
            StopGlide();
            _mouse = mouse;
            _mode = Mode.Deciding;
            _haveAnchor = false;
            _downAt = Since.ElapsedMilliseconds;
            _velocity = Vector2.Zero;
            _remainder = Vector2.Zero;
        }
    }

    /// <summary>The pen left the tablet surface.</summary>
    public static void TipUp()
    {
        lock (Gate)
        {
            switch (_mode)
            {
                case Mode.Deciding:
                    // A tap: press now, release on the next report (the driver sends one mouse state per report).
                    _mouse?.MouseDown(MouseButton.Left);
                    _releaseClick = true;
                    break;
                case Mode.Dragging:
                    _mouse?.MouseUp(MouseButton.Left);
                    break;
                case Mode.Scrolling:
                    if (_velocity.Length() >= MinFlickSpeed && Since.ElapsedMilliseconds - _lastAt < 80)
                        StartGlide(_velocity);
                    break;
            }
            _mode = Mode.Idle;
        }
    }

    /// <summary>Every pen report. Returns where the cursor should be.</summary>
    public static Vector2 OnPosition(Vector2 position)
    {
        lock (Gate)
        {
            FinishTap();
            long now = Since.ElapsedMilliseconds;

            switch (_mode)
            {
                case Mode.Deciding:
                    if (!_haveAnchor)
                    {
                        _anchor = _last = position;
                        _lastAt = now;
                        _haveAnchor = true;
                        return _anchor;
                    }
                    if (Vector2.Distance(position, _anchor) > MoveToScroll)
                    {
                        _mode = Mode.Scrolling;
                        Scroll(position, now);
                        return _anchor;
                    }
                    if (now - _downAt >= HoldToDragMs)
                    {
                        // Held still: behave like a normal mouse press from here on, and tell you so.
                        _mode = Mode.Dragging;
                        _mouse?.MouseDown(MouseButton.Left);
                        Tick();
                        Log.Write("Lveno: Pen scrolling", $"Hold: dragging from {_anchor} (mouse {(_mouse is null ? "missing" : "ok")})");
                    }
                    return _anchor;

                case Mode.Scrolling:
                    Scroll(position, now);
                    return _anchor; // the cursor stays where you touched, like a finger

                default:
                    return position;
            }
        }
    }

    /// <summary>Any report at all, even when the pen is away: finishes a tap.</summary>
    public static void OnReport()
    {
        lock (Gate) FinishTap();
    }

    /// <summary>A soft tick: the hold registered, so you can drag or select now.</summary>
    private static void Tick()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            Process.Start(new ProcessStartInfo("/usr/bin/afplay")
            {
                ArgumentList = { "-v", "0.5", "/System/Library/Sounds/Tink.aiff" },
                UseShellExecute = false,
            })?.Dispose();
        }
        catch
        {
            // No sound is fine; the drag still works.
        }
    }

    private static void FinishTap()
    {
        if (!_releaseClick) return;
        _releaseClick = false;
        _mouse?.MouseUp(MouseButton.Left);
    }

    private static void Scroll(Vector2 position, long now)
    {
        var delta = position - _last;
        float dt = Math.Max(1, now - _lastAt) / 1000f;
        _velocity = Vector2.Lerp(_velocity, delta / dt, 0.35f);
        _last = position;
        _lastAt = now;
        Post(delta);
    }

    private static void StartGlide(Vector2 velocity)
    {
        var v = velocity;
        _glide = new Timer(_ =>
        {
            lock (Gate)
            {
                if (_glide is null) return;
                v *= FrictionPerFrame;
                if (v.Length() < 20f) { StopGlide(); return; }
                Post(v / 60f);
            }
        }, null, 16, 16);
    }

    private static void StopGlide()
    {
        _glide?.Dispose();
        _glide = null;
    }

    /// <summary>Scrolls the content under the cursor so it moves with the pen.</summary>
    private static void Post(Vector2 delta)
    {
        if (!OperatingSystem.IsMacOS()) return;
        _remainder += delta;
        int dx = (int)_remainder.X, dy = (int)_remainder.Y;
        if (dx == 0 && dy == 0) return;
        _remainder -= new Vector2(dx, dy);

        // Pixel units, continuous (trackpad-like). Positive moves the content down/right, with the pen.
        var e = CGEventCreateScrollWheelEvent2(IntPtr.Zero, 0, 2, dy, dx, 0);
        if (e == IntPtr.Zero) return;
        CGEventSetIntegerValueField(e, ScrollWheelEventIsContinuous, 1);
        CGEventPost(0, e); // kCGHIDEventTap
        CFRelease(e);
    }

    private const int ScrollWheelEventIsContinuous = 88;
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreateScrollWheelEvent2(IntPtr source, int units, uint wheelCount, int wheel1, int wheel2, int wheel3);

    [DllImport(CoreGraphics)]
    private static extern void CGEventSetIntegerValueField(IntPtr e, int field, long value);

    [DllImport(CoreGraphics)]
    private static extern void CGEventPost(int tap, IntPtr e);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);
}

/// <summary>Pen tip for pen scrolling. Set as the tip binding instead of the normal click.</summary>
[PluginName(PluginName)]
public class PenScrollTipBinding : IStateBinding
{
    public const string PluginName = "Lveno: Pen scrolling (tip)";

    [Resolved]
    public IMouseButtonHandler? MouseButtonHandler { get; set; }

    public void Press(TabletReference tablet, IDeviceReport report) => PenScrollEngine.TipDown(MouseButtonHandler);

    public void Release(TabletReference tablet, IDeviceReport report) => PenScrollEngine.TipUp();

    public override string ToString() => PluginName;
}

/// <summary>Watches pen positions for pen scrolling. Runs after positions are mapped to the screen.</summary>
[PluginName(PluginName)]
public class PenScrollFilter : IPositionedPipelineElement<IDeviceReport>
{
    public const string PluginName = "Lveno: Pen scrolling";

    public PipelinePosition Position => PipelinePosition.PostTransform;

    public event Action<IDeviceReport?>? Emit;

    public void Consume(IDeviceReport? report)
    {
        if (report is IAbsolutePositionReport absolute)
            absolute.Position = PenScrollEngine.OnPosition(absolute.Position);
        else
            PenScrollEngine.OnReport();
        Emit?.Invoke(report);
    }
}
