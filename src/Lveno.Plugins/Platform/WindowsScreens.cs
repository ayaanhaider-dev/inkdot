using System.Numerics;
using System.Runtime.InteropServices;

namespace Lveno.Plugins;

/// <summary>
/// Windows screens, in physical pixels on the virtual desktop, read the same way OpenTabletDriver
/// reads them (monitor list + each monitor's display settings), so both agree on positions.
/// The ID is a hash of the monitor's device name (\\.\DISPLAY1…), stable while it stays connected.
/// </summary>
internal static class WindowsScreens
{
    public static IReadOnlyList<ScreenInfo> Read()
    {
        var screens = new List<ScreenInfo>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Rect r, IntPtr data) =>
            {
                var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
                if (!GetMonitorInfo(monitor, ref info)) return true;
                var mode = new DevMode { dmSize = (short)Marshal.SizeOf<DevMode>() };
                float x = info.rcMonitor.left, y = info.rcMonitor.top;
                float w = info.rcMonitor.right - info.rcMonitor.left, h = info.rcMonitor.bottom - info.rcMonitor.top;
                if (EnumDisplaySettings(info.szDevice, -1, ref mode))
                {
                    x = mode.dmPositionX; y = mode.dmPositionY; w = mode.dmPelsWidth; h = mode.dmPelsHeight;
                }
                screens.Add(new ScreenInfo(Id(info.szDevice), 0, 0, 0, w, h, (info.dwFlags & 1) != 0, false, x, y));
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            return [];
        }
        return screens;
    }

    public static Vector2? Cursor() => GetCursorPos(out var p) ? new Vector2(p.x, p.y) : null;

    /// <summary>FNV-1a of the device name: a steady number to remember a screen by.</summary>
    public static uint Id(string device)
    {
        uint hash = 2166136261;
        foreach (char c in device) { hash ^= c; hash *= 16777619; }
        return hash;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int x, y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor, rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY;
        public int dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")] private static extern bool EnumDisplaySettings(string device, int mode, ref DevMode devMode);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
}
