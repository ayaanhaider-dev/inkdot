using System.Numerics;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace Lveno.Plugins;

/// <summary>
/// Drawing mode on Windows: the pen shows up as a real pen (Windows Ink), with pressure, tilt,
/// the eraser end and the barrel button, so Photoshop, Krita, Clip Studio and other Ink apps feel it.
/// Uses Windows' own pen injection (Windows 10 1809 or later). No extra driver to install.
/// </summary>
[PluginName(PluginName), SupportedPlatform(PluginPlatform.Windows)]
public class WindowsInkMode : AbsoluteOutputMode
{
    public const string PluginName = "InkDot: Windows Ink";

    private IAbsolutePointer? _pointer;

    public override IAbsolutePointer? Pointer
    {
        set => _pointer = value;
        get => _pointer ??= OperatingSystem.IsWindows() ? new WindowsInkPointer() : null;
    }
}

/// <summary>
/// One synthetic pen. The driver calls the Set… methods for every report, then <see cref="Flush"/>,
/// which sends one pen frame to Windows. Contact comes from the tip binding (so the tip threshold applies).
/// </summary>
public sealed class WindowsInkPointer : IAbsolutePointer, IPressureHandler, ITiltHandler, IEraserHandler,
    IPenActionHandler, IMouseButtonHandler, ISynchronousPointer, IDisposable
{
    private readonly IntPtr _device;
    private readonly object _gate = new();
    private Vector2 _position;
    private float _pressure;
    private Vector2 _tilt;
    private bool _eraser, _tipDown, _eraserDown, _barrel, _inRange, _wasContact;

    public WindowsInkPointer()
    {
        _device = CreateSyntheticPointerDevice(PtPen, 1, PointerFeedbackDefault);
        if (_device == IntPtr.Zero)
            Log.Write(WindowsInkMode.PluginName, "Windows didn't create the pen (needs Windows 10 1809 or later).", LogLevel.Error);
    }

    public void SetPosition(Vector2 pos)
    {
        lock (_gate) { _position = pos; _inRange = true; }
    }

    public void SetPressure(float percentage)
    {
        lock (_gate) _pressure = Math.Clamp(percentage, 0f, 1f);
    }

    public void SetTilt(Vector2 tilt)
    {
        lock (_gate) _tilt = tilt;
    }

    public void SetEraser(bool isEraser)
    {
        lock (_gate) _eraser = isEraser;
    }

    public void Activate(PenAction action)
    {
        lock (_gate)
        {
            switch (action)
            {
                case PenAction.Tip: _tipDown = true; break;
                case PenAction.Eraser: _eraserDown = true; break;
                case PenAction.BarrelButton1: _barrel = true; break;
                default: Mouse(action == PenAction.BarrelButton2 ? MouseButton.Middle : MouseButton.Right, true); break;
            }
        }
    }

    public void Deactivate(PenAction action)
    {
        lock (_gate)
        {
            switch (action)
            {
                case PenAction.Tip: _tipDown = false; break;
                case PenAction.Eraser: _eraserDown = false; break;
                case PenAction.BarrelButton1: _barrel = false; break;
                default: Mouse(action == PenAction.BarrelButton2 ? MouseButton.Middle : MouseButton.Right, false); break;
            }
        }
    }

    // Plain mouse buttons, for bindings that ask for a mouse click (and pen scrolling).
    public void MouseDown(MouseButton button)
    {
        if (button == MouseButton.Left) { lock (_gate) _tipDown = true; }
        else Mouse(button, true);
    }

    public void MouseUp(MouseButton button)
    {
        if (button == MouseButton.Left) { lock (_gate) _tipDown = false; }
        else Mouse(button, false);
    }

    /// <summary>The pen left the tablet: tell Windows it's out of range.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (_wasContact) Send(contact: false, up: true);
            if (_inRange) Send(contact: false, up: false, leaving: true);
            _inRange = _wasContact = _tipDown = _eraserDown = _barrel = false;
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (!_inRange) return;
            bool contact = _tipDown || _eraserDown;
            if (contact) Send(contact: true, up: false);
            else if (_wasContact) Send(contact: false, up: true);
            else Send(contact: false, up: false);
            _wasContact = contact;
        }
    }

    private void Send(bool contact, bool up, bool leaving = false)
    {
        if (_device == IntPtr.Zero) return;

        uint flags;
        uint change = 0;
        if (leaving) flags = FlagUpdate; // no INRANGE: the pen leaves
        else if (contact && !_wasContact) { flags = FlagInRange | FlagInContact | FlagFirstButton | FlagDown | FlagPrimary; change = ChangeFirstDown; }
        else if (contact) flags = FlagInRange | FlagInContact | FlagFirstButton | FlagUpdate | FlagPrimary;
        else if (up) { flags = FlagInRange | FlagUp | FlagPrimary; change = ChangeFirstUp; }
        else flags = FlagInRange | FlagUpdate | FlagPrimary;

        uint penFlags = 0;
        if (_barrel) penFlags |= PenFlagBarrel;
        if (_eraser || _eraserDown) penFlags |= contact ? PenFlagEraser : PenFlagInverted;

        // Driver positions start at the virtual desktop's top-left; Windows wants real screen pixels.
        int x = (int)MathF.Round(_position.X) + GetSystemMetrics(SmXVirtualScreen);
        int y = (int)MathF.Round(_position.Y) + GetSystemMetrics(SmYVirtualScreen);

        var info = new PointerTypeInfo
        {
            type = PtPen,
            pen = new PointerPenInfo
            {
                pointerInfo = new PointerInfo
                {
                    pointerType = PtPen,
                    pointerId = 0,
                    pointerFlags = flags,
                    ptPixelLocation = new Point { x = x, y = y },
                    ButtonChangeType = change,
                },
                penFlags = penFlags,
                penMask = PenMaskPressure | PenMaskTiltX | PenMaskTiltY,
                pressure = contact ? (uint)Math.Clamp(MathF.Round(_pressure * 1024f), 1f, 1024f) : 0,
                tiltX = Math.Clamp((int)MathF.Round(_tilt.X), -90, 90),
                tiltY = Math.Clamp((int)MathF.Round(_tilt.Y), -90, 90),
            },
        };
        if (!InjectSyntheticPointerInput(_device, [info], 1))
            Log.Debug(WindowsInkMode.PluginName, $"Pen frame rejected ({Marshal.GetLastWin32Error()}).");
    }

    private static void Mouse(MouseButton button, bool down)
    {
        uint flag = (button, down) switch
        {
            (MouseButton.Right, true) => 0x0008u, (MouseButton.Right, false) => 0x0010u,
            (MouseButton.Middle, true) => 0x0020u, (MouseButton.Middle, false) => 0x0040u,
            (MouseButton.Left, true) => 0x0002u, (MouseButton.Left, false) => 0x0004u,
            _ => 0u,
        };
        if (flag == 0) return;
        var input = new MouseInputEvent { type = 0, mi = new MouseInput { dwFlags = flag } };
        SendInput(1, [input], Marshal.SizeOf<MouseInputEvent>());
    }

    public void Dispose()
    {
        if (_device != IntPtr.Zero) DestroySyntheticPointerDevice(_device);
    }

    // ---- Win32 pen injection ------------------------------------------------------------------

    private const int PtPen = 3, PointerFeedbackDefault = 1, SmXVirtualScreen = 76, SmYVirtualScreen = 77;
    private const uint FlagInRange = 0x2, FlagInContact = 0x4, FlagFirstButton = 0x10, FlagPrimary = 0x2000,
        FlagDown = 0x10000, FlagUpdate = 0x20000, FlagUp = 0x40000;
    private const uint ChangeFirstDown = 1, ChangeFirstUp = 2;
    private const uint PenFlagBarrel = 0x1, PenFlagInverted = 0x2, PenFlagEraser = 0x4;
    private const uint PenMaskPressure = 0x1, PenMaskTiltX = 0x4, PenMaskTiltY = 0x8;

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerInfo
    {
        public int pointerType;
        public uint pointerId, frameId, pointerFlags;
        public IntPtr sourceDevice, hwndTarget;
        public Point ptPixelLocation, ptHimetricLocation, ptPixelLocationRaw, ptHimetricLocationRaw;
        public uint dwTime, historyCount;
        public int InputData;
        public uint dwKeyStates;
        public ulong PerformanceCount;
        public uint ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointerPenInfo
    {
        public PointerInfo pointerInfo;
        public uint penFlags, penMask, pressure, rotation;
        public int tiltX, tiltY;
    }

    // POINTER_TYPE_INFO: the type, then a union of touch info (the larger, 144 bytes) and pen info.
    [StructLayout(LayoutKind.Sequential, Size = 152)]
    private struct PointerTypeInfo
    {
        public int type;
        public PointerPenInfo pen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int dx, dy, mouseData; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputEvent { public uint type; public MouseInput mi; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreateSyntheticPointerDevice(int pointerType, uint maxCount, int mode);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool InjectSyntheticPointerInput(IntPtr device, PointerTypeInfo[] info, uint count);
    [DllImport("user32.dll")] private static extern void DestroySyntheticPointerDevice(IntPtr device);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, MouseInputEvent[] inputs, int size);
}
