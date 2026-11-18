using Lveno.Plugins;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Output;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Tablet;

namespace Lveno.App.Services;

/// <summary>
/// The tablet Lveno is configuring, with its settings profile and the screens it can draw on.
/// Pages change the profile, then call <see cref="ApplyAsync"/>.
/// </summary>
public sealed class TabletSession
{
    private TabletSession(DriverService driver, TabletReference tablet, Settings settings, Profile profile, IReadOnlyList<ScreenInfo> displays)
    {
        Driver = driver;
        Tablet = tablet;
        Settings = settings;
        Profile = profile;
        Displays = displays;
    }

    public DriverService Driver { get; }
    public TabletReference Tablet { get; }
    public Settings Settings { get; }
    public Profile Profile { get; }
    /// <summary>Screens connected now, left to right. Read fresh from macOS, not the driver's start-up list.</summary>
    public IReadOnlyList<ScreenInfo> Displays { get; }

    /// <summary>macOS's name for each screen, by display ID (e.g. "Built-in Retina Display").</summary>
    public IReadOnlyDictionary<uint, string> ScreenNames { get; } = MacApps.ScreenNames();

    /// <summary>"Screen 2 · DELL U2720Q" or just "Screen 2".</summary>
    public string ScreenName(int index) =>
        index >= 0 && index < Displays.Count && ScreenNames.TryGetValue(Displays[index].Id, out var name)
            ? name : $"Screen {index + 1}";

    public string TabletName => Tablet.Properties.Name;
    public DigitizerSpecifications Digitizer => Tablet.Properties.Specifications.Digitizer;
    public int PenButtonCount => (int)(Tablet.Properties.Specifications.Pen?.ButtonCount ?? 0);

    /// <summary>The whole drawing surface of the tablet, in millimetres.</summary>
    public Area FullTablet => new(Digitizer.Width, Digitizer.Height,
        new System.Numerics.Vector2(Digitizer.Width / 2, Digitizer.Height / 2));

    public AbsoluteModeSettings Absolute
    {
        get
        {
            Profile.AbsoluteModeSettings ??= AbsoluteModeSettings.GetDefaults(Digitizer);
            return Profile.AbsoluteModeSettings;
        }
    }

    /// <summary>
    /// Mouse mode: the pen moves the cursor like a mouse (relative), instead of each spot on the
    /// tablet matching a spot on screen (absolute, the default for drawing).
    /// </summary>
    public bool MouseMode
    {
        get => Profile.OutputMode.Path == typeof(RelativeMode).FullName;
        set => Profile.OutputMode = new PluginSettingStore(value ? typeof(RelativeMode) : typeof(AbsoluteMode));
    }

    public RelativeModeSettings Relative
    {
        get
        {
            Profile.RelativeModeSettings ??= RelativeModeSettings.GetDefaults();
            return Profile.RelativeModeSettings;
        }
    }

    public static async Task<TabletSession> LoadAsync(DriverService driver, TabletReference tablet)
    {
        var settings = await driver.Daemon.GetSettings();
        var profile = settings.Profiles.GetProfile(tablet); // creates a default profile if there isn't one
        var displays = ScreenLayout.Current();
        return new TabletSession(driver, tablet, settings, profile, displays);
    }

    /// <summary>How long the pen must rest against an edge before switching: long enough to reach a menu at the edge without switching.</summary>
    public const float EdgePushHoldMs = 1000f;

    /// <summary>True when push-to-switch is on but saved with an older push time.</summary>
    public bool EdgePushNeedsUpdate =>
        Profile.Filters.FirstOrDefault(f => f?.Path == typeof(EdgePushFilter).FullName) is { } f
        && ButtonActions.Value(f, "HoldMs") is { } v
        && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ms)
        && Math.Abs(ms - EdgePushHoldMs) > 1;

    /// <summary>Turns push-to-switch on or off. It runs before the screen fix, so it sees the live layout.</summary>
    public void SetEdgePush(bool on)
    {
        var old = Profile.Filters.FirstOrDefault(f => f?.Path == typeof(EdgePushFilter).FullName);
        if (old != null) Profile.Filters.Remove(old);
        if (!on) return;
        var store = ButtonActions.Store<EdgePushFilter>(("MatchScreenShape", Absolute.LockAspectRatio), ("HoldMs", EdgePushHoldMs));
        store.Enable = true;
        Profile.Filters.Insert(0, store);
    }

    /// <summary>
    /// Keeps the pen lined up when screens change while the driver runs (see ScreenFixFilter).
    /// Returns true if it had to be added.
    /// </summary>
    public bool EnsureScreenFix()
    {
        var store = Profile.Filters.FirstOrDefault(f => f?.Path == typeof(ScreenFixFilter).FullName);
        if (store is { Enable: true }) return false;
        if (store != null) Profile.Filters.Remove(store);
        var fix = ButtonActions.Store<ScreenFixFilter>();
        fix.Enable = true;
        Profile.Filters.Add(fix);
        return true;
    }

    /// <summary>Sends the settings to the driver and saves them so they survive a restart.</summary>
    public async Task ApplyAsync()
    {
        // Only two modes are offered: absolute (drawing) or relative (mouse mode).
        if (!MouseMode && Profile.OutputMode.Path != typeof(AbsoluteMode).FullName)
            Profile.OutputMode = new PluginSettingStore(typeof(AbsoluteMode));

        EnsureScreenFix();
        await Driver.Daemon.SetSettings(Settings);
        Settings.Serialize(new FileInfo(AppInfo.Current.SettingsFile));
    }

    /// <summary>The screen the drawing area covers now, <see cref="Controls.ScreenMap.AllScreens"/>, or Custom.</summary>
    public int CurrentScreenIndex()
    {
        var display = Absolute.Display.Area;
        int index = ScreenMath.IndexOfScreen(Displays, display);
        if (index >= 0)
            return index;
        return ScreenMath.SameArea(ScreenMath.AreaForAllScreens(Displays), display)
            ? Controls.ScreenMap.AllScreens
            : Controls.ScreenMap.Custom;
    }
}
