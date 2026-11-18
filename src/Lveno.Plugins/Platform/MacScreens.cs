using System.Numerics;
using System.Runtime.InteropServices;

namespace Lveno.Plugins;

/// <summary>macOS screens through CoreGraphics. Global coordinates are points from the main screen's top-left.</summary>
internal static class MacScreens
{
    /// <summary>Every active screen with its global position; X/Y and Index are filled in by <see cref="ScreenLayout"/>.</summary>
    public static IReadOnlyList<ScreenInfo> Read()
    {
        try
        {
            var ids = new uint[16];
            if (CGGetActiveDisplayList((uint)ids.Length, ids, out uint count) != 0 || count == 0) return [];
            uint main = CGMainDisplayID();
            return ids.Take((int)count).Select(id =>
            {
                var b = CGDisplayBounds(id);
                return new ScreenInfo(id, 0, 0, 0, (float)b.Width, (float)b.Height, id == main, CGDisplayIsBuiltin(id) != 0, (float)b.X, (float)b.Y);
            }).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>The pointer, in global coordinates.</summary>
    public static Vector2? Cursor()
    {
        var e = CGEventCreate(IntPtr.Zero);
        if (e == IntPtr.Zero) return null;
        var p = CGEventGetLocation(e);
        CFRelease(e);
        return new Vector2((float)p.X, (float)p.Y);
    }

    [StructLayout(LayoutKind.Sequential)] private struct CGRect { public double X, Y, Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct CGPoint { public double X, Y; }

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [DllImport(CoreGraphics)] private static extern int CGGetActiveDisplayList(uint maxDisplays, [Out] uint[] displays, out uint count);
    [DllImport(CoreGraphics)] private static extern CGRect CGDisplayBounds(uint display);
    [DllImport(CoreGraphics)] private static extern uint CGMainDisplayID();
    [DllImport(CoreGraphics)] private static extern int CGDisplayIsBuiltin(uint display);
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr e);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr cf);
}
