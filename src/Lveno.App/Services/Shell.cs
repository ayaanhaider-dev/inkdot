using System.Diagnostics;

namespace Lveno.App.Services;

/// <summary>Opens a link, file or folder with whatever the system uses for it.</summary>
public static class Shell
{
    public static void Open(string target)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { target }, UseShellExecute = false })?.Dispose();
            else
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch
        {
            // Nothing to open with; not worth an error.
        }
    }
}
