using System.Collections;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Tablet;

namespace Lveno.Plugins;

/// <summary>
/// A pen or tablet button that moves the drawing area to another screen while you work.
/// The change lasts until the driver restarts or settings are applied again.
/// </summary>
[PluginName(PluginName)]
public class SwitchScreenBinding : IStateBinding
{
    public const string PluginName = "Lveno: Switch screen";

    public const string NextScreen = "Next screen";
    public const string PreviousScreen = "Previous screen";
    public const string AllScreens = "All screens";

    public static IEnumerable<string> ValidActions => [NextScreen, PreviousScreen, AllScreens];

    [Property("Action"), PropertyValidated(nameof(ValidActions)), DefaultPropertyValue(NextScreen)]
    [ToolTip("What the button does: move to the next screen, the previous screen, or cover all screens.")]
    public string Action { get; set; } = NextScreen;

    [BooleanProperty("Match screen shape", "Crop the tablet area to the screen's shape so circles stay round."), DefaultPropertyValue(true)]
    public bool MatchScreenShape { get; set; } = true;

    [Resolved]
    public IDriver? Driver { get; set; }

    [Resolved]
    public IVirtualScreen? VirtualScreen { get; set; }

    [TabletReference]
    public TabletReference? Tablet { get; set; }

    // The tablet area the user configured, remembered on first press so repeated
    // switches always fit inside it instead of shrinking a little each time.
    private Area? _configuredInput;

    public void Press(TabletReference tablet, IDeviceReport report)
    {
        try
        {
            Switch(tablet);
        }
        catch (Exception ex)
        {
            Log.Write(PluginName, $"Could not switch screens: {ex.Message}", LogLevel.Error);
        }
    }

    public void Release(TabletReference tablet, IDeviceReport report)
    {
    }

    private void Switch(TabletReference tablet)
    {
        // The screens connected now (not the driver's start-up list), left to right like Lveno numbers them.
        // Areas are in the live layout; ScreenFixFilter lines that up with the driver.
        var live = ScreenLayout.Current();
        var displays = live.Count > 0
            ? live.Cast<IDisplay>().ToList()
            : VirtualScreen?.Displays.OrderBy(d => d.Position.X).ThenBy(d => d.Position.Y).ToList();
        if (displays is not { Count: > 0 })
        {
            Log.Write(PluginName, "No screens found.", LogLevel.Warning);
            return;
        }

        foreach (var mode in FindOutputModes(tablet))
        {
            if (mode.Output is null || mode.Input is null)
                continue;

            _configuredInput ??= mode.Input;

            Area target;
            string label;
            if (Action == AllScreens)
            {
                target = ScreenMath.AreaForAllScreens(displays);
                label = "all screens";
            }
            else
            {
                int current = ScreenMath.IndexOfScreen(displays, mode.Output);
                int step = Action == PreviousScreen ? -1 : 1;
                int next = current < 0
                    ? (step > 0 ? 0 : displays.Count - 1)
                    : ((current + step) % displays.Count + displays.Count) % displays.Count;
                target = ScreenMath.AreaForScreen(displays[next]);
                label = $"screen {next + 1} of {displays.Count}";
            }

            // Output first, then input: each setter rebuilds the transform.
            mode.Output = target;
            if (MatchScreenShape)
                mode.Input = ScreenMath.FitAspect(_configuredInput, target.Width / target.Height);

            Log.Write(PluginName, $"Drawing on {label}.");
        }
    }

    /// <summary>
    /// Finds the absolute output modes driving this tablet. IDriver doesn't expose the pipeline,
    /// so this reads Driver.InputDevices[].OutputMode by name to stay compatible with the daemon build.
    /// </summary>
    private IEnumerable<AbsoluteOutputMode> FindOutputModes(TabletReference tablet)
    {
        if (Driver is null)
            yield break;

        var devices = Driver.GetType().GetProperty("InputDevices")?.GetValue(Driver) as IEnumerable;
        if (devices is null)
            yield break;

        foreach (var device in devices)
        {
            var mode = device?.GetType().GetProperty("OutputMode")?.GetValue(device);
            if (mode is AbsoluteOutputMode absolute &&
                absolute.Tablet?.Properties.Name == tablet.Properties.Name)
            {
                yield return absolute;
            }
        }
    }

    public override string ToString() => $"{PluginName}: {Action}";
}
