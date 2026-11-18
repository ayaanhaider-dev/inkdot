using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lveno.Plugins;

/// <summary>Scroll events and the hold tick, for macOS and Windows.</summary>
internal static class SystemInput
{
    /// <summary>
    /// macOS scrolls in pixels. Windows scrolls in wheel units: 120 per notch, about 100 pixels,
    /// and modern apps accept smaller steps for smooth scrolling.
    /// </summary>
    public static float ScrollUnitsPerPoint => OperatingSystem.IsWindows() ? 1.2f : 1f;

    /// <summary>Scrolls so the content follows the pen: positive dy moves it down, positive dx right.</summary>
    public static void Scroll(int dx, int dy)
    {
        try
        {
            if (OperatingSystem.IsMacOS()) MacScroll(dx, dy);
            else if (OperatingSystem.IsWindows()) WindowsScroll(dx, dy);
        }
        catch
        {
            // A dropped scroll step is harmless.
        }
    }

    public static void Tick()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("/usr/bin/afplay")
                {
                    ArgumentList = { "-v", "0.5", "/System/Library/Sounds/Tink.aiff" },
                    UseShellExecute = false,
                })?.Dispose();
            else if (OperatingSystem.IsWindows())
                MessageBeep(0x00000040); // the system "asterisk" sound
        }
        catch
        {
            // No sound is fine; the drag still works.
        }
    }

    // ---- macOS ---------------------------------------------------------------------------------

    private static void MacScroll(int dx, int dy)
    {
        // Pixel units, continuous (trackpad-like).
        var e = CGEventCreateScrollWheelEvent2(IntPtr.Zero, 0, 2, dy, dx, 0);
        if (e == IntPtr.Zero) return;
        CGEventSetIntegerValueField(e, 88, 1); // kCGScrollWheelEventIsContinuous
        CGEventPost(0, e); // kCGHIDEventTap
        CFRelease(e);
    }

    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreateScrollWheelEvent2(IntPtr source, int units, uint wheelCount, int wheel1, int wheel2, int wheel3);
    [DllImport(CoreGraphics)] private static extern void CGEventSetIntegerValueField(IntPtr e, int field, long value);
    [DllImport(CoreGraphics)] private static extern void CGEventPost(int tap, IntPtr e);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr cf);

    // ---- Windows -------------------------------------------------------------------------------

    private const uint InputMouse = 0, MouseWheel = 0x0800, MouseHWheel = 0x1000;

    private static void WindowsScroll(int dx, int dy)
    {
        var inputs = new List<Input>(2);
        // A positive vertical wheel scrolls up, which moves the content down: same sign as dy.
        if (dy != 0) inputs.Add(new Input { type = InputMouse, mi = new MouseInput { mouseData = dy, dwFlags = MouseWheel } });
        // A positive horizontal wheel scrolls right, which moves the content left: opposite of dx.
        if (dx != 0) inputs.Add(new Input { type = InputMouse, mi = new MouseInput { mouseData = -dx, dwFlags = MouseHWheel } });
        if (inputs.Count > 0)
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx, dy;
        public int mouseData;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    // MOUSEINPUT is the largest member of INPUT's union, so this has the right size and padding.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public MouseInput mi;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool MessageBeep(uint type);
}
