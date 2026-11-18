using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;

namespace Lveno.App.Platform;

using Lveno.App.Services;

/// <summary>Asks macOS (NSWorkspace) which app is in front, which apps are open, and what their icons look like.</summary>
internal static class MacApps
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, nuint arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector, nuint arg, IntPtr arg2);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendInt(IntPtr receiver, IntPtr selector);

    private static IntPtr Sel(string name) => sel_registerName(name);
    private static IntPtr Class(string name) => objc_getClass(name);
    private static IntPtr Workspace => Send(Class("NSWorkspace"), Sel("sharedWorkspace"));
    private static IntPtr NSString(string s) => SendString(Class("NSString"), Sel("stringWithUTF8String:"), s);
    private static string? Str(IntPtr nsString) =>
        nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, Sel("UTF8String")));

    private static string? Name(IntPtr app) => app == IntPtr.Zero ? null : Str(Send(app, Sel("localizedName")));

    private static string? BundlePath(IntPtr app)
    {
        var url = Send(app, Sel("bundleURL"));
        return url == IntPtr.Zero ? null : Str(Send(url, Sel("path")));
    }

    /// <summary>The name of the app you're using right now, e.g. "Adobe Photoshop 2026".</summary>
    public static string? Frontmost()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        try { return Name(Send(Workspace, Sel("frontmostApplication"))); }
        catch { return null; }
    }

    /// <summary>Open apps that have a window or Dock icon, sorted by name.</summary>
    public static IReadOnlyList<DesktopApp> RunningApps()
    {
        if (!OperatingSystem.IsMacOS()) return [];
        var apps = new Dictionary<string, DesktopApp>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var list = Send(Workspace, Sel("runningApplications"));
            nint count = SendInt(list, Sel("count"));
            for (nuint i = 0; i < (nuint)count; i++)
            {
                var app = Send(list, Sel("objectAtIndex:"), i);
                if (SendInt(app, Sel("activationPolicy")) != 0) continue; // 0 = a regular app
                if (Name(app) is { } name && !name.StartsWith("Lveno", StringComparison.OrdinalIgnoreCase))
                    apps.TryAdd(name, new DesktopApp(name, BundlePath(app)));
            }
        }
        catch
        {
            // Return whatever we found.
        }
        return apps.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>NSWorkspace iconForFile → PNG bytes.</summary>
    public static byte[]? IconPng(string bundlePath)
    {
        var image = Send(Workspace, Sel("iconForFile:"), NSString(bundlePath));
        if (image == IntPtr.Zero) return null;
        var tiff = Send(image, Sel("TIFFRepresentation"));
        var rep = Send(Class("NSBitmapImageRep"), Sel("imageRepWithData:"), tiff);
        if (rep == IntPtr.Zero) return null;
        var options = Send(Class("NSDictionary"), Sel("dictionary"));
        var png = Send(rep, Sel("representationUsingType:properties:"), (nuint)4, options); // 4 = PNG
        if (png == IntPtr.Zero) return null;
        int length = (int)SendInt(png, Sel("length"));
        var bytes = new byte[length];
        Marshal.Copy(Send(png, Sel("bytes")), bytes, 0, length);
        return bytes;
    }

    /// <summary>macOS's name for each connected screen, by display ID, e.g. "Built-in Retina Display".</summary>
    public static IReadOnlyDictionary<uint, string> ScreenNames()
    {
        var names = new Dictionary<uint, string>();
        if (!OperatingSystem.IsMacOS()) return names;
        try
        {
            var screens = Send(Class("NSScreen"), Sel("screens"));
            nint count = SendInt(screens, Sel("count"));
            var key = NSString("NSScreenNumber");
            for (nuint i = 0; i < (nuint)count; i++)
            {
                var screen = Send(screens, Sel("objectAtIndex:"), i);
                var number = Send(Send(screen, Sel("deviceDescription")), Sel("objectForKey:"), key);
                if (number == IntPtr.Zero) continue;
                uint id = (uint)SendInt(number, Sel("unsignedIntValue"));
                if (Str(Send(screen, Sel("localizedName"))) is { Length: > 0 } name)
                    names[id] = name;
            }
        }
        catch
        {
            // Unnamed screens are fine.
        }
        return names;
    }

    private static readonly Dictionary<string, Bitmap?> WallpaperCache = new();

    /// <summary>
    /// Each screen's desktop wallpaper, small, by display ID. Used as the screen's picture in the map.
    /// (A live screenshot would need the Screen Recording permission; the wallpaper doesn't.)
    /// </summary>
    public static IReadOnlyDictionary<uint, Bitmap> Wallpapers()
    {
        var result = new Dictionary<uint, Bitmap>();
        if (!OperatingSystem.IsMacOS()) return result;
        try
        {
            var screens = Send(Class("NSScreen"), Sel("screens"));
            nint count = SendInt(screens, Sel("count"));
            var key = NSString("NSScreenNumber");
            for (nuint i = 0; i < (nuint)count; i++)
            {
                var screen = Send(screens, Sel("objectAtIndex:"), i);
                var number = Send(Send(screen, Sel("deviceDescription")), Sel("objectForKey:"), key);
                if (number == IntPtr.Zero) continue;
                uint id = (uint)SendInt(number, Sel("unsignedIntValue"));
                var url = Send(Workspace, Sel("desktopImageURLForScreen:"), screen);
                if (url == IntPtr.Zero || Str(Send(url, Sel("path"))) is not { Length: > 0 } path) continue;
                if (WallpaperThumbnail(path) is { } bitmap) result[id] = bitmap;
            }
        }
        catch
        {
            // No pictures is fine; the map still works.
        }
        return result;
    }

    /// <summary>Any wallpaper format (HEIC, JPEG, PNG…) → a cached 640px PNG via sips.</summary>
    private static Bitmap? WallpaperThumbnail(string path)
    {
        if (!File.Exists(path)) return null;
        string key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}";
        if (WallpaperCache.TryGetValue(key, out var cached)) return cached;

        Bitmap? bitmap = null;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(DriverService.AppDataDirectory, "WallpaperCache")).FullName;
            var file = Path.Combine(folder, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes(key)))[..16] + ".png");
            if (!File.Exists(file))
            {
                using var sips = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/sips")
                {
                    ArgumentList = { "-s", "format", "png", "-Z", "640", path, "--out", file },
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                });
                sips?.WaitForExit(5000);
            }
            if (File.Exists(file)) bitmap = new Bitmap(file);
        }
        catch
        {
            bitmap = null;
        }
        WallpaperCache[key] = bitmap;
        return bitmap;
    }

    /// <summary>Where an app lives, from its name, for apps that aren't open right now.</summary>
    public static string? FindBundle(string name)
    {
        foreach (var folder in new[] { "/Applications", "/System/Applications", "/Applications/Utilities",
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications") })
        {
            var path = Path.Combine(folder, name + ".app");
            if (Directory.Exists(path)) return path;
        }
        // Apps like Photoshop sit in a folder of their own.
        var nested = Path.Combine("/Applications", name, name + ".app");
        return Directory.Exists(nested) ? nested : null;
    }
}
