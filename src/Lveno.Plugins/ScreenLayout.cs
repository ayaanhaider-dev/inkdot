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
/// The screens connected right now, read fresh from the system each time (the driver's own list is
/// only read once at start). Numbered left to right, then top to bottom.
/// Coordinates match the driver's: the desktop's top-left corner is (0, 0). Points on macOS, pixels on Windows.
/// </summary>
public static class ScreenLayout
{
    public static IReadOnlyList<ScreenInfo> Current()
    {
        var raw = OperatingSystem.IsWindows() ? WindowsScreens.Read()
            : OperatingSystem.IsMacOS() ? MacScreens.Read()
            : [];
        if (raw.Count == 0) return [];
        float minX = raw.Min(r => r.GlobalX), minY = raw.Min(r => r.GlobalY);
        return raw
            .Select(r => r with { X = r.GlobalX - minX, Y = r.GlobalY - minY })
            .OrderBy(s => s.X).ThenBy(s => s.Y)
            .Select((s, i) => s with { Index = i })
            .ToList();
    }

    /// <summary>The top-left corner of the whole desktop in the system's global coordinates, right now.</summary>
    public static Vector2? DesktopOrigin()
    {
        var raw = OperatingSystem.IsWindows() ? WindowsScreens.Read()
            : OperatingSystem.IsMacOS() ? MacScreens.Read()
            : [];
        return raw.Count == 0 ? null : new Vector2(raw.Min(r => r.GlobalX), raw.Min(r => r.GlobalY));
    }

    /// <summary>Windows: the ID a screen gets from its device name (\\.\DISPLAY1…), the same one <see cref="Current"/> uses.</summary>
    public static uint WindowsScreenId(string device) => WindowsScreens.Id(device);

    /// <summary>A short fingerprint of the layout, to notice screens being plugged in, unplugged or moved.</summary>
    public static string Signature(IEnumerable<ScreenInfo> screens) =>
        string.Join(";", screens.Select(s => $"{s.Id}:{s.X:0},{s.Y:0},{s.Width:0},{s.Height:0}"));

    /// <summary>The index of the screen the pointer is on now, or -1.</summary>
    public static int CursorScreen(IReadOnlyList<ScreenInfo> screens)
    {
        if (screens.Count == 0) return -1;
        try
        {
            var p = OperatingSystem.IsWindows() ? WindowsScreens.Cursor()
                : OperatingSystem.IsMacOS() ? MacScreens.Cursor()
                : null;
            if (p is not { } global) return -1;
            foreach (var s in screens)
            {
                if (global.X >= s.GlobalX && global.X < s.GlobalX + s.Width && global.Y >= s.GlobalY && global.Y < s.GlobalY + s.Height)
                    return s.Index;
            }
        }
        catch
        {
            // fall through
        }
        return -1;
    }
}
