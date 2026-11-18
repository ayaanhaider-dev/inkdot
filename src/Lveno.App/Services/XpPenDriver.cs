using System.Diagnostics;

namespace Lveno.App.Services;

/// <summary>
/// XP-Pen's own driver reads the same tablet. If both run, they fight over the cursor.
/// </summary>
public static class XpPenDriver
{
    // macOS and Windows names of XP-Pen's driver and its tray/service helpers.
    private static readonly string[] ProcessNames = ["XPPenDriver", "XPPenTablet", "PenTablet", "PenDriver", "PentabletService", "PenTabletService"];

    public static IReadOnlyList<Process> Running() =>
        Process.GetProcesses()
            .Where(p =>
            {
                try { return ProcessNames.Any(n => p.ProcessName.Equals(n, StringComparison.OrdinalIgnoreCase)); }
                catch { return false; }
            })
            .ToList();

    public static bool IsRunning => Running().Count > 0;

    /// <summary>Quits XP-Pen's driver for this session. It starts again at login unless you remove it.</summary>
    public static int Quit()
    {
        int count = 0;
        foreach (var process in Running())
        {
            try
            {
                process.Kill();
                count++;
            }
            catch
            {
                // Not ours to stop (another user, or already gone).
            }
        }
        return count;
    }
}
