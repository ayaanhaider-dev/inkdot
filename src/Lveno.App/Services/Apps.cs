using Avalonia.Media.Imaging;
using Lveno.App.Platform;

namespace Lveno.App.Services;

/// <summary>An app on this computer: its name and where it lives (.app bundle on macOS, .exe on Windows).</summary>
public sealed record DesktopApp(string Name, string? BundlePath);

/// <summary>Which app is in front, which apps are open, their icons, and screen names and wallpapers.</summary>
public static class Apps
{
    public static string? Frontmost() =>
        OperatingSystem.IsWindows() ? WindowsApps.Frontmost() : MacApps.Frontmost();

    public static IReadOnlyList<DesktopApp> RunningApps() =>
        OperatingSystem.IsWindows() ? WindowsApps.RunningApps() : MacApps.RunningApps();

    public static IReadOnlyList<string> Running() => RunningApps().Select(a => a.Name).ToList();

    public static IReadOnlyDictionary<uint, string> ScreenNames() =>
        OperatingSystem.IsWindows() ? WindowsApps.ScreenNames() : MacApps.ScreenNames();

    public static IReadOnlyDictionary<uint, Bitmap> Wallpapers() =>
        OperatingSystem.IsWindows() ? WindowsApps.Wallpapers() : MacApps.Wallpapers();

    /// <summary>Where an app lives, from its name, for apps that aren't open right now (macOS only).</summary>
    public static string? FindBundle(string name) => OperatingSystem.IsMacOS() ? MacApps.FindBundle(name) : null;

    private static readonly Dictionary<string, Bitmap?> IconCache = new();

    /// <summary>The app's icon, small. Cached in memory (and on disk on macOS). Null if there isn't one.</summary>
    public static Bitmap? Icon(string? path, int size = 64)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (IconCache.TryGetValue(path, out var cached)) return cached;

        Bitmap? bitmap = null;
        try
        {
            if (OperatingSystem.IsWindows())
                bitmap = WindowsApps.Icon(path, size);
            else if (OperatingSystem.IsMacOS())
            {
                var folder = Directory.CreateDirectory(Path.Combine(DriverService.AppDataDirectory, "IconCache")).FullName;
                var file = Path.Combine(folder, Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                    System.Text.Encoding.UTF8.GetBytes(path)))[..16] + ".png");
                if (!File.Exists(file) && MacApps.IconPng(path) is { } png)
                    File.WriteAllBytes(file, png);
                if (File.Exists(file))
                {
                    using var stream = File.OpenRead(file);
                    bitmap = Bitmap.DecodeToWidth(stream, size);
                }
            }
        }
        catch
        {
            bitmap = null;
        }
        IconCache[path] = bitmap;
        return bitmap;
    }
}
