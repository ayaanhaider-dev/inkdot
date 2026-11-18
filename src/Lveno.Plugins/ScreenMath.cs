using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Platform.Display;

namespace Lveno.Plugins;

/// <summary>
/// Geometry shared by the Lveno app and the Lveno plugin, so both map areas the same way.
/// OpenTabletDriver areas are center-based: <see cref="Area.Position"/> is the middle of the area.
/// Screens report their top-left corner, in points, relative to the top-left of the whole desktop.
/// </summary>
public static class ScreenMath
{
    /// <summary>The output area that covers one screen.</summary>
    public static Area AreaForScreen(IDisplay display) =>
        new(display.Width, display.Height,
            new Vector2(display.Position.X + display.Width / 2f, display.Position.Y + display.Height / 2f));

    /// <summary>The output area that covers every screen.</summary>
    public static Area AreaForAllScreens(IEnumerable<IDisplay> displays)
    {
        var list = displays.ToList();
        if (list.Count == 0)
            return new Area(1, 1);

        float left = list.Min(d => d.Position.X);
        float top = list.Min(d => d.Position.Y);
        float right = list.Max(d => d.Position.X + d.Width);
        float bottom = list.Max(d => d.Position.Y + d.Height);
        float width = right - left, height = bottom - top;
        return new Area(width, height, new Vector2(left + width / 2f, top + height / 2f));
    }

    /// <summary>
    /// The largest area with the given width/height ratio that fits inside <paramref name="bounds"/>,
    /// scaled by <paramref name="scale"/> (0–1) and centered on the bounds.
    /// </summary>
    public static Area FitAspect(Area bounds, float aspect, float scale = 1f)
    {
        if (aspect <= 0 || float.IsNaN(aspect))
            aspect = bounds.Width / bounds.Height;

        float width = bounds.Width, height = bounds.Width / aspect;
        if (height > bounds.Height)
        {
            height = bounds.Height;
            width = bounds.Height * aspect;
        }

        scale = Math.Clamp(scale, 0.05f, 1f);
        return new Area(width * scale, height * scale, bounds.Position, bounds.Rotation);
    }

    /// <summary>True when two areas cover the same rectangle, give or take a pixel.</summary>
    public static bool SameArea(Area a, Area b, float tolerance = 1.5f) =>
        Math.Abs(a.Width - b.Width) <= tolerance &&
        Math.Abs(a.Height - b.Height) <= tolerance &&
        Math.Abs(a.Position.X - b.Position.X) <= tolerance &&
        Math.Abs(a.Position.Y - b.Position.Y) <= tolerance;

    /// <summary>Index into <paramref name="displays"/> of the screen an area covers, or -1.</summary>
    public static int IndexOfScreen(IReadOnlyList<IDisplay> displays, Area area)
    {
        for (int i = 0; i < displays.Count; i++)
        {
            if (SameArea(AreaForScreen(displays[i]), area))
                return i;
        }
        return -1;
    }
}
