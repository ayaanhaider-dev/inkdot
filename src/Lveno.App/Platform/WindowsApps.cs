using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Lveno.App.Services;
using Lveno.Plugins;

namespace Lveno.App.Platform;

/// <summary>Windows: the app in front, open apps, their icons, screen names and the wallpaper.</summary>
internal static class WindowsApps
{
    /// <summary>An app's display name: its file description ("Adobe Photoshop 2026"), else the process name.</summary>
    private static string NameOf(Process p)
    {
        try
        {
            if (p.MainModule?.FileName is { } file && FileVersionInfo.GetVersionInfo(file).FileDescription is { Length: > 0 } d)
                return d.Trim();
        }
        catch
        {
            // Protected processes don't share their path.
        }
        return p.ProcessName;
    }

    private static string? PathOf(Process p)
    {
        try { return p.MainModule?.FileName; }
        catch { return null; }
    }

    private static bool IsOurs(Process p) =>
        p.ProcessName.StartsWith("Lveno", StringComparison.OrdinalIgnoreCase) || p.ProcessName.StartsWith("InkDot", StringComparison.OrdinalIgnoreCase);

    public static string? Frontmost()
    {
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;
            GetWindowThreadProcessId(window, out uint pid);
            using var p = Process.GetProcessById((int)pid);
            return NameOf(p);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Apps with a window, sorted by name.</summary>
    public static IReadOnlyList<DesktopApp> RunningApps()
    {
        var apps = new Dictionary<string, DesktopApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero || IsOurs(p)) continue;
                var name = NameOf(p);
                apps.TryAdd(name, new DesktopApp(name, PathOf(p)));
            }
            catch
            {
                // Skip what we can't read.
            }
            finally
            {
                p.Dispose();
            }
        }
        return apps.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The program's own icon, at about <paramref name="size"/> pixels.</summary>
    public static Bitmap? Icon(string exePath, int size)
    {
        if (!File.Exists(exePath)) return null;
        if (SHDefExtractIcon(exePath, 0, 0, out var large, out var small, (uint)(size | (16 << 16))) != 0 || large == IntPtr.Zero)
            return null;
        try { return FromIcon(large); }
        finally
        {
            DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
        }
    }

    /// <summary>HICON → Avalonia bitmap (32-bit BGRA).</summary>
    private static Bitmap? FromIcon(IntPtr icon)
    {
        if (!GetIconInfo(icon, out var info)) return null;
        try
        {
            if (GetObject(info.hbmColor, Marshal.SizeOf<BitmapHeader>(), out var bm) == 0) return null;
            int w = bm.bmWidth, h = bm.bmHeight;
            var header = new BitmapInfoHeader { biSize = Marshal.SizeOf<BitmapInfoHeader>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            var pixels = new byte[w * h * 4];
            var dc = GetDC(IntPtr.Zero);
            try { GetDIBits(dc, info.hbmColor, 0, (uint)h, pixels, ref header, 0); }
            finally { ReleaseDC(IntPtr.Zero, dc); }

            // Old icons carry no alpha: then everything is opaque.
            bool anyAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
            using (var fb = bitmap.Lock())
            {
                for (int row = 0; row < h; row++)
                    Marshal.Copy(pixels, row * w * 4, fb.Address + row * fb.RowBytes, w * 4);
            }
            return bitmap;
        }
        finally
        {
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
        }
    }

    /// <summary>Each screen's monitor name, by the same ID the screen layout uses.</summary>
    public static IReadOnlyDictionary<uint, string> ScreenNames()
    {
        var names = new Dictionary<uint, string>();
        try
        {
            for (uint i = 0; ; i++)
            {
                var adapter = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
                if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
                if ((adapter.StateFlags & 1) == 0) continue; // attached to the desktop
                var monitor = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
                string name = EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0) && monitor.DeviceString is { Length: > 0 } m
                    && !m.Contains("Generic", StringComparison.OrdinalIgnoreCase) ? m : $"Display {i + 1}";
                names[ScreenLayout.WindowsScreenId(adapter.DeviceName)] = name;
            }
        }
        catch
        {
            // Unnamed screens are fine.
        }
        return names;
    }

    /// <summary>The desktop wallpaper, small, for every screen (Windows uses one picture by default).</summary>
    public static IReadOnlyDictionary<uint, Bitmap> Wallpapers()
    {
        var result = new Dictionary<uint, Bitmap>();
        try
        {
            var path = new StringBuilder(260);
            if (!SystemParametersInfo(0x0073, (uint)path.Capacity, path, 0) || !File.Exists(path.ToString())) return result; // SPI_GETDESKWALLPAPER
            using var stream = File.OpenRead(path.ToString());
            var bitmap = Bitmap.DecodeToWidth(stream, 640);
            foreach (var screen in ScreenLayout.Current()) result[screen.Id] = bitmap;
        }
        catch
        {
            // No pictures is fine; the map still works.
        }
        return result;
    }

    // ---- Win32 ----------------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)] private struct IconInfo { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapHeader { public int bmType, bmWidth, bmHeight, bmWidthBytes; public short bmPlanes, bmBitsPixel; public IntPtr bmBits; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr obj, int size, out BitmapHeader bitmap);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")] private static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice dd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW")] private static extern bool SystemParametersInfo(uint action, uint param, StringBuilder value, uint winIni);
}
