using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lveno.App.Services;

/// <summary>
/// The two macOS permissions a tablet driver needs:
/// Input Monitoring to read the pen, and Accessibility to move the cursor.
/// macOS grants them to the app that launched the driver, which is Lveno.
/// </summary>
public static class MacPermissions
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightListenEventAccess();

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRequestListenEventAccess();

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRequestPostEventAccess();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    public static bool IsSupported => OperatingSystem.IsMacOS();

    public static bool HasInputMonitoring => !IsSupported || CGPreflightListenEventAccess();

    public static bool HasAccessibility => !IsSupported || AXIsProcessTrusted();

    public static bool HasAll => HasInputMonitoring && HasAccessibility;

    public static void RequestInputMonitoring()
    {
        if (!IsSupported) return;
        if (!CGRequestListenEventAccess())
            OpenSettings("Privacy_ListenEvent");
    }

    public static void RequestAccessibility()
    {
        if (!IsSupported) return;
        if (!CGRequestPostEventAccess())
            OpenSettings("Privacy_Accessibility");
    }

    public static void OpenSettings(string pane) =>
        Process.Start(new ProcessStartInfo("open", $"x-apple.systempreferences:com.apple.preference.security?{pane}")
        {
            UseShellExecute = false,
        });
}
