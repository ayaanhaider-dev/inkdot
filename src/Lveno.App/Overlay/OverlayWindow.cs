using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Lveno.Plugins;

namespace Lveno.App.Overlay;

/// <summary>
/// A transparent, click-through, always-on-top window covering one screen. Shown only while an
/// animation plays; it never takes focus or clicks.
/// </summary>
public sealed class OverlayWindow : Window
{
    public OverlayCanvas Canvas { get; } = new();
    private bool _nativeReady, _onScreen;
    private (double X, double Y, double W, double H) _frame;

    public OverlayWindow()
    {
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        Content = Canvas;
        Canvas.Idle += () =>
        {
            Native.OrderOut(this);
            _onScreen = false;
        };
    }

    /// <summary>Cover this screen and come to the front, without taking focus.</summary>
    public void ShowOn(ScreenInfo screen, double mainScreenHeight)
    {
        // Cocoa puts the origin at the main screen's bottom-left.
        var frame = ((double)screen.GlobalX, mainScreenHeight - (screen.GlobalY + screen.Height), (double)screen.Width, (double)screen.Height);
        if (_onScreen && frame == _frame) return; // already up; called many times a second while pushing

        Width = screen.Width;
        Height = screen.Height;
        if (!IsVisible) Show();
        if (!_nativeReady) _nativeReady = Native.MakeOverlay(this);
        Native.SetFrame(this, frame.Item1, frame.Item2, frame.Item3, frame.Item4);
        Native.OrderFront(this);
        _frame = frame;
        _onScreen = true;
    }

    /// <summary>The macOS window calls Avalonia doesn't offer: click-through, level, all Spaces.</summary>
    private static class Native
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr r, IntPtr s);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr r, IntPtr s, [MarshalAs(UnmanagedType.I1)] bool v);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendInt(IntPtr r, IntPtr s, nint v);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendUInt(IntPtr r, IntPtr s, nuint v);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendPtr(IntPtr r, IntPtr s, IntPtr v);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendFrame(IntPtr r, IntPtr s, NSRect frame, [MarshalAs(UnmanagedType.I1)] bool display);

        [StructLayout(LayoutKind.Sequential)]
        private struct NSRect { public double X, Y, W, H; }

        private static IntPtr NSWindow(Window w)
        {
            if (!OperatingSystem.IsMacOS() || w.TryGetPlatformHandle() is not { } handle) return IntPtr.Zero;
            return handle.HandleDescriptor == "NSView" ? Send(handle.Handle, sel_registerName("window")) : handle.Handle;
        }

        public static bool MakeOverlay(Window w)
        {
            var win = NSWindow(w);
            if (win == IntPtr.Zero) return false;
            SendBool(win, sel_registerName("setIgnoresMouseEvents:"), true);
            SendBool(win, sel_registerName("setHasShadow:"), false);
            SendBool(win, sel_registerName("setOpaque:"), false);
            SendInt(win, sel_registerName("setLevel:"), 1000); // above everything, like a system overlay
            // canJoinAllSpaces | stationary | ignoresCycle | fullScreenAuxiliary
            SendUInt(win, sel_registerName("setCollectionBehavior:"), (nuint)((1 << 0) | (1 << 4) | (1 << 6) | (1 << 8)));
            return true;
        }

        public static void SetFrame(Window w, double x, double y, double width, double height)
        {
            var win = NSWindow(w);
            if (win != IntPtr.Zero)
                SendFrame(win, sel_registerName("setFrame:display:"), new NSRect { X = x, Y = y, W = width, H = height }, true);
        }

        public static void OrderFront(Window w)
        {
            var win = NSWindow(w);
            if (win != IntPtr.Zero) Send(win, sel_registerName("orderFrontRegardless"));
        }

        public static void OrderOut(Window w)
        {
            var win = NSWindow(w);
            if (win != IntPtr.Zero) SendPtr(win, sel_registerName("orderOut:"), IntPtr.Zero);
        }
    }
}
