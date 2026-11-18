using System.Numerics;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin.Platform.Display;

namespace Lveno.Plugins;

/// <summary>
/// One screen, in the driver's coordinates (X, Y; GlobalX/GlobalY are macOS global coordinates): points, measured from the top-left of the whole desktop.
/// <see cref="Id"/> is macOS's display ID, which stays the same while the screen is connected.
/// </summary>
public sealed record ScreenInfo(uint Id, int Index, float X, float Y, float Width, float Height, bool IsMain, bool IsBuiltIn,
    float GlobalX = 0, float GlobalY = 0)
    : IDisplay
{
    public Vector2 Position => new(X, Y);
}

/// <summary>
/// The screens connected right now, read fresh from macOS each time (the driver's own list is
/// only read once at start). Numbered left to right, then top to bottom.
/// Coordinates match the driver's: the desktop's top-left corner is (0, 0).
/// </summary>
public static class ScreenLayout
{
    public static IReadOnlyList<ScreenInfo> Current()
    {
        if (!OperatingSystem.IsMacOS()) return [];
        try
        {
            var ids = new uint[16];
            if (CGGetActiveDisplayList((uint)ids.Length, ids, out uint count) != 0 || count == 0) return [];

            var raw = ids.Take((int)count).Select(id => (Id: id, Bounds: CGDisplayBounds(id))).ToList();
            double minX = raw.Min(r => r.Bounds.X), minY = raw.Min(r => r.Bounds.Y);
            uint main = CGMainDisplayID();

            return raw
                .Select(r => new ScreenInfo(r.Id, 0,
                    (float)(r.Bounds.X - minX), (float)(r.Bounds.Y - minY),
                    (float)r.Bounds.Width, (float)r.Bounds.Height,
                    r.Id == main, CGDisplayIsBuiltin(r.Id) != 0,
                    (float)r.Bounds.X, (float)r.Bounds.Y))
                .OrderBy(s => s.X).ThenBy(s => s.Y)
                .Select((s, i) => s with { Index = i })
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>The top-left corner of the whole desktop in macOS global coordinates, right now.</summary>
    public static Vector2? DesktopOrigin()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try
        {
            var ids = new uint[16];
            if (CGGetActiveDisplayList((uint)ids.Length, ids, out uint count) != 0 || count == 0) return null;
            var bounds = ids.Take((int)count).Select(CGDisplayBounds).ToList();
            return new Vector2((float)bounds.Min(b => b.X), (float)bounds.Min(b => b.Y));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A short fingerprint of the layout, to notice screens being plugged in, unplugged or moved.</summary>
    public static string Signature(IEnumerable<ScreenInfo> screens) =>
        string.Join(";", screens.Select(s => $"{s.Id}:{s.X:0},{s.Y:0},{s.Width:0},{s.Height:0}"));

    /// <summary>The index of the screen the pointer is on now, or -1.</summary>
    public static int CursorScreen(IReadOnlyList<ScreenInfo> screens)
    {
        if (!OperatingSystem.IsMacOS() || screens.Count == 0) return -1;
        try
        {
            var e = CGEventCreate(IntPtr.Zero);
            if (e == IntPtr.Zero) return -1;
            var p = CGEventGetLocation(e);
            CFRelease(e);

            // Pointer location is in macOS global coordinates; convert using the same corner as Current().
            var ids = new uint[16];
            CGGetActiveDisplayList((uint)ids.Length, ids, out uint count);
            var bounds = ids.Take((int)count).Select(CGDisplayBounds).ToList();
            if (bounds.Count == 0) return -1;
            double x = p.X - bounds.Min(b => b.X), y = p.Y - bounds.Min(b => b.Y);

            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i];
                if (x >= s.X && x < s.X + s.Width && y >= s.Y && y < s.Y + s.Height)
                    return i;
            }
        }
        catch
        {
            // fall through
        }
        return -1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public double X, Y, Width, Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X, Y; }

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(CoreGraphics)] private static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] displays, out uint count);
    [DllImport(CoreGraphics)] private static extern CGRect CGDisplayBounds(uint display);
    [DllImport(CoreGraphics)] private static extern uint CGMainDisplayID();
    [DllImport(CoreGraphics)] private static extern int CGDisplayIsBuiltin(uint display);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr e);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr cf);
}
