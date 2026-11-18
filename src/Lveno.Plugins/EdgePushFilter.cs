using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace Lveno.Plugins;

/// <summary>
/// Push to switch screens, like Universal Control: hover the pen against a tablet edge that faces
/// another screen (by the macOS arrangement) for a moment, and the drawing area moves to that screen.
///
/// Runs after positions are mapped to the screen, before the screen-layout fix, so positions are in
/// the live layout (the same space Lveno's areas use). Tells the Lveno app what's happening over a
/// local UDP message so it can animate; the pen never waits on that.
/// </summary>
[PluginName(PluginName)]
public class EdgePushFilter : IPositionedPipelineElement<IDeviceReport>
{
    public const string PluginName = "Lveno: Push to switch screens";
    public const int EventPort = 47815;

    [SliderProperty("Edge width (mm)", 0.5f, 6f, 2f), DefaultPropertyValue(2f)]
    public float EdgeMm { get; set; } = 2f;

    [SliderProperty("Push time (ms)", 300f, 2000f, 1000f), DefaultPropertyValue(1000f)]
    public float HoldMs { get; set; } = 1000f;

    [BooleanProperty("Match screen shape", "Crop the tablet area to the new screen's shape."), DefaultPropertyValue(true)]
    public bool MatchScreenShape { get; set; } = true;

    [Resolved] public IDriver? Driver { get; set; }
    [TabletReference] public TabletReference? Tablet { get; set; }

    public PipelinePosition Position => PipelinePosition.PostTransform;
    public event Action<IDeviceReport?>? Emit;

    private enum Edge { None, Left, Right, Top, Bottom }

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private AbsoluteOutputMode? _mode;
    private Area? _configuredInput;

    // Static so it survives the driver rebuilding the pen pipeline (e.g. when settings are re-applied).
    private static Edge _edge = Edge.None;
    private static long _pushStart;
    private static float _lastSentProgress = -1;
    private static long _lastSentAt;
    private static bool _armed = true;     // after a switch, wait until the pen leaves the edge
    private const float GlideMs = 260f;
    private static Vector2 _entry;         // where the cursor came in on the new screen
    private static long _glideStart = -1;  // after a switch, glide from the entry spot to where the pen really is
    private IReadOnlyList<ScreenInfo> _screens = [];
    private long _screensAt = -10_000;

    public void Consume(IDeviceReport? report)
    {
        try
        {
            if (report is ITabletReport tablet)
                Handle(tablet);
            else if (report is not null && _edge != Edge.None)
                Cancel(); // pen taken away from the tablet
        }
        catch (Exception ex)
        {
            Log.Write(PluginName, ex.Message, LogLevel.Debug);
        }
        Emit?.Invoke(report);
    }

    private void Handle(ITabletReport report)
    {
        var mode = _mode ??= FindOutputMode();
        if (mode?.Output is not { } output || mode.Input is not { } input) return;

        float pxPerMm = output.Width / Math.Max(1f, input.Width);
        var pos = report.Position;

        float left = output.Position.X - output.Width / 2, top = output.Position.Y - output.Height / 2;
        float right = left + output.Width, bottom = top + output.Height;

        // After a switch, the cursor glides from where it came in to where the pen really is,
        // so the whole tablet stays usable and nothing jumps.
        if (_glideStart >= 0)
        {
            float t = (Clock.ElapsedMilliseconds - _glideStart) / GlideMs;
            if (t < 1f)
            {
                float ease = 1 - MathF.Pow(1 - t, 3);
                report.Position = Vector2.Lerp(_entry, pos, ease);
                return; // no pushing mid-glide
            }
            _glideStart = -1;
        }

        // Only while hovering, never while drawing.
        if (report.Pressure > 0)
        {
            if (_edge != Edge.None) Cancel();
            return;
        }

        float zone = Math.Max(4f, EdgeMm * pxPerMm);

        var edge = Edge.None;
        float best = zone;
        void Try(Edge e, float distance) { if (distance <= best) { best = distance; edge = e; } }
        Try(Edge.Left, pos.X - left);
        Try(Edge.Right, right - pos.X);
        Try(Edge.Top, pos.Y - top);
        Try(Edge.Bottom, bottom - pos.Y);

        if (edge == Edge.None)
        {
            _armed = true;
            if (_edge != Edge.None) Cancel();
            return;
        }
        if (!_armed) return;

        var screens = Screens();
        var current = screens.FirstOrDefault(s => Contains(s, output.Position));
        var target = current is null ? null : Neighbour(screens, current, edge, pos);
        if (target is null)
        {
            if (_edge != Edge.None) Cancel();
            return;
        }

        long now = Clock.ElapsedMilliseconds;
        if (edge != _edge)
        {
            _edge = edge;
            _pushStart = now;
            _lastSentProgress = -1;
        }

        float progress = Math.Clamp((now - _pushStart) / Math.Max(1f, HoldMs), 0f, 1f);
        if (progress < 1f)
        {
            if (progress - _lastSentProgress >= 0.03f || now - _lastSentAt >= 33)
            {
                Send($"{{\"t\":\"p\",\"e\":\"{Name(edge)}\",\"s\":{current!.Id},\"x\":{F(pos.X)},\"y\":{F(pos.Y)},\"p\":{F(progress)}}}");
                _lastSentProgress = progress;
                _lastSentAt = now;
            }
            return;
        }

        SwitchTo(mode, current!, target, edge, pos, report);
    }

    private void SwitchTo(AbsoluteOutputMode mode, ScreenInfo from, ScreenInfo to, Edge edge, Vector2 pos, ITabletReport report)
    {
        _configuredInput ??= mode.Input;
        var area = ScreenMath.AreaForScreen(to);
        mode.Output = area;
        if (MatchScreenShape && _configuredInput is { } configured)
            mode.Input = ScreenMath.FitAspect(configured, area.Width / area.Height);

        // Land at the matching spot on the new screen's opposite edge.
        float along = edge is Edge.Top or Edge.Bottom
            ? (pos.X - from.X) / from.Width
            : (pos.Y - from.Y) / from.Height;
        along = Math.Clamp(along, 0.02f, 0.98f);
        var entry = edge switch
        {
            Edge.Top => new Vector2(to.X + along * to.Width, to.Y + to.Height - 2),
            Edge.Bottom => new Vector2(to.X + along * to.Width, to.Y + 2),
            Edge.Left => new Vector2(to.X + to.Width - 2, to.Y + along * to.Height),
            _ => new Vector2(to.X + 2, to.Y + along * to.Height),
        };

        // Enter at the matching spot, then glide to the pen (see above).
        _entry = entry;
        _glideStart = Clock.ElapsedMilliseconds;
        report.Position = entry;

        _armed = false;
        _edge = Edge.None;
        Send($"{{\"t\":\"s\",\"e\":\"{Name(edge)}\",\"from\":{from.Id},\"to\":{to.Id},\"x\":{F(entry.X)},\"y\":{F(entry.Y)}}}");
        Log.Write(PluginName, $"Switched to screen {to.Index + 1}.");
    }

    private void Cancel()
    {
        Send($"{{\"t\":\"c\",\"e\":\"{Name(_edge)}\"}}");
        _edge = Edge.None;
        _lastSentProgress = -1;
    }

    // ---- Screens --------------------------------------------------------------------------------

    private IReadOnlyList<ScreenInfo> Screens()
    {
        if (Clock.ElapsedMilliseconds - _screensAt > 500)
        {
            _screens = ScreenLayout.Current();
            _screensAt = Clock.ElapsedMilliseconds;
        }
        return _screens;
    }

    private static bool Contains(ScreenInfo s, Vector2 p) =>
        p.X >= s.X && p.X <= s.X + s.Width && p.Y >= s.Y && p.Y <= s.Y + s.Height;

    /// <summary>The screen touching this edge of the current one, preferring the one across from the pen.</summary>
    private static ScreenInfo? Neighbour(IReadOnlyList<ScreenInfo> screens, ScreenInfo cur, Edge edge, Vector2 pen)
    {
        const float touch = 4f;
        IEnumerable<ScreenInfo> candidates = edge switch
        {
            Edge.Top => screens.Where(s => Math.Abs(s.Y + s.Height - cur.Y) <= touch && Overlap(s.X, s.Width, cur.X, cur.Width) > 0),
            Edge.Bottom => screens.Where(s => Math.Abs(s.Y - (cur.Y + cur.Height)) <= touch && Overlap(s.X, s.Width, cur.X, cur.Width) > 0),
            Edge.Left => screens.Where(s => Math.Abs(s.X + s.Width - cur.X) <= touch && Overlap(s.Y, s.Height, cur.Y, cur.Height) > 0),
            Edge.Right => screens.Where(s => Math.Abs(s.X - (cur.X + cur.Width)) <= touch && Overlap(s.Y, s.Height, cur.Y, cur.Height) > 0),
            _ => [],
        };
        var list = candidates.Where(s => s.Id != cur.Id).ToList();
        if (list.Count == 0) return null;

        bool horizontal = edge is Edge.Top or Edge.Bottom;
        float coord = horizontal ? pen.X : pen.Y;
        return list
            .OrderBy(s => horizontal
                ? (coord >= s.X && coord <= s.X + s.Width ? 0 : Math.Min(Math.Abs(coord - s.X), Math.Abs(coord - s.X - s.Width)))
                : (coord >= s.Y && coord <= s.Y + s.Height ? 0 : Math.Min(Math.Abs(coord - s.Y), Math.Abs(coord - s.Y - s.Height))))
            .First();
    }

    private static float Overlap(float a, float aLen, float b, float bLen) =>
        Math.Min(a + aLen, b + bLen) - Math.Max(a, b);

    private AbsoluteOutputMode? FindOutputMode()
    {
        if (Driver is null || Tablet is null) return null;
        if (Driver.GetType().GetProperty("InputDevices")?.GetValue(Driver) is not IEnumerable devices) return null;
        foreach (var device in devices)
        {
            if (device?.GetType().GetProperty("OutputMode")?.GetValue(device) is AbsoluteOutputMode absolute &&
                absolute.Tablet?.Properties.Name == Tablet.Properties.Name)
                return absolute;
        }
        return null;
    }

    // ---- Messages to the app -------------------------------------------------------------------

    private static readonly UdpClient Udp = new();

    private static void Send(string json)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            Udp.Send(bytes, bytes.Length, "127.0.0.1", EventPort);
        }
        catch
        {
            // Nobody listening is fine.
        }
    }

    private static string Name(Edge e) => e.ToString().ToLowerInvariant();
    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
