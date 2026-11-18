using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Tablet;

namespace Lveno.Plugins;

/// <summary>
/// Keeps the pen on the right screen when screens are plugged in, unplugged or rearranged,
/// without restarting the driver.
///
/// The driver reads the screen layout once, when it starts, and turns pen positions into macOS
/// coordinates with that old layout. Lveno sets areas using the live layout. When the desktop's
/// top-left corner moves (say the screen on the far left is unplugged), the two disagree by exactly
/// that move; this filter adds it back.
/// </summary>
[PluginName(PluginName)]
public class ScreenFixFilter : IPositionedPipelineElement<IDeviceReport>
{
    public const string PluginName = "Lveno: Screen layout fix";

    [Resolved]
    public IVirtualScreen? VirtualScreen { get; set; }

    public PipelinePosition Position => PipelinePosition.PostTransform;

    public event Action<IDeviceReport?>? Emit;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static long _checkedAt = -10_000;
    private static Vector2 _correction;
    private static readonly object Gate = new();

    public void Consume(IDeviceReport? report)
    {
        if (report is IAbsolutePositionReport absolute)
            absolute.Position += Correction();
        Emit?.Invoke(report);
    }

    /// <summary>Live desktop corner minus the corner the driver started with. Re-read twice a second.</summary>
    private Vector2 Correction()
    {
        lock (Gate)
        {
            if (Clock.ElapsedMilliseconds - _checkedAt < 500)
                return _correction;
            _checkedAt = Clock.ElapsedMilliseconds;

            // The driver's pointer subtracts its first (main) screen's position, which equals
            // minus the desktop corner it saw at start-up.
            var startMain = VirtualScreen?.Displays.FirstOrDefault()?.Position;
            var live = ScreenLayout.DesktopOrigin();
            if (startMain is { } main && live is { } origin)
            {
                var correction = origin + main;
                if (correction != _correction)
                    Log.Write(PluginName, $"Screen layout changed; shifting the pen by {correction}.");
                _correction = correction;
            }
            return _correction;
        }
    }
}
