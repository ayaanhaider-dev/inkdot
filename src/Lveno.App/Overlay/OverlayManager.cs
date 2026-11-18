using Avalonia;
using Lveno.App.Services;
using Lveno.Plugins;

namespace Lveno.App.Overlay;

/// <summary>Turns push-to-switch events from the driver into animations, one overlay window per screen.</summary>
public sealed class OverlayManager
{
    private readonly Dictionary<uint, OverlayWindow> _windows = [];

    /// <summary>Raised after the driver moved the drawing area, with the new screen's ID.</summary>
    public event Action<uint>? Switched;

    public void Handle(EdgePushEvent e)
    {
        var screens = ScreenLayout.Current();
        if (screens.Count == 0) return;
        double mainHeight = screens.FirstOrDefault(s => s.IsMain)?.Height ?? screens[0].Height;

        switch (e.Type)
        {
            case "p": // pushing
            {
                if (screens.FirstOrDefault(s => s.Id == e.Screen) is not { } screen || ParseEdge(e.Edge) is not { } edge) return;
                var window = For(screen, mainHeight);
                double along = edge is ScreenEdge.Top or ScreenEdge.Bottom ? e.X - screen.X : e.Y - screen.Y;
                window.Canvas.Push(edge, along, e.Progress);
                break;
            }
            case "c": // pulled back
                foreach (var w in _windows.Values) w.Canvas.Release();
                break;
            case "s": // switched
            {
                if (ParseEdge(e.Edge) is not { } edge) return;
                if (screens.FirstOrDefault(s => s.Id == e.From) is { } from)
                    For(from, mainHeight).Canvas.Break();
                if (screens.FirstOrDefault(s => s.Id == e.To) is { } to)
                {
                    var names = Apps.ScreenNames();
                    string name = $"Screen {to.Index + 1}" + (names.TryGetValue(to.Id, out var n) ? $"  ·  {n}" : "");
                    For(to, mainHeight).Canvas.Arrive(Opposite(edge), new Point(e.X - to.X, e.Y - to.Y), name);
                }
                Switched?.Invoke(e.To);
                break;
            }
        }
    }

    private OverlayWindow For(ScreenInfo screen, double mainHeight)
    {
        if (!_windows.TryGetValue(screen.Id, out var window))
            _windows[screen.Id] = window = new OverlayWindow();
        window.ShowOn(screen, mainHeight);
        return window;
    }

    private static ScreenEdge? ParseEdge(string edge) => edge switch
    {
        "left" => ScreenEdge.Left,
        "right" => ScreenEdge.Right,
        "top" => ScreenEdge.Top,
        "bottom" => ScreenEdge.Bottom,
        _ => null,
    };

    private static ScreenEdge Opposite(ScreenEdge e) => e switch
    {
        ScreenEdge.Left => ScreenEdge.Right,
        ScreenEdge.Right => ScreenEdge.Left,
        ScreenEdge.Top => ScreenEdge.Bottom,
        _ => ScreenEdge.Top,
    };
}
