using Lveno.Plugins;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace Lveno.App.Services;

/// <summary>
/// Pen scrolling on/off: the main switch, per-app overrides, and drawing apps where it's off by itself
/// (in a drawing app, dragging the pen must draw, never scroll).
/// </summary>
public static class PenScrolling
{
    /// <summary>Matched against app names, ignoring case. "Photoshop" matches "Adobe Photoshop 2026".</summary>
    public static readonly string[] DrawingApps =
    [
        "Photoshop", "Illustrator", "Fresco", "Krita", "Figma", "Clip Studio", "CLIPStudio", "Affinity",
        "Procreate", "Sketch", "Pixelmator", "Blender", "Aseprite", "Rebelle", "MediBang", "FireAlpaca",
        "Paintstorm", "Painter", "Infinite Painter", "Concepts", "GIMP", "Inkscape", "Freeform",
        "Photopea", "Kleki", "Paint Tool SAI", "Moho", "Toon Boom", "OpenToonz", "Animate", "ZBrush", "Nomad",
        "InkDot", "Lveno", // our own test pads
    ];

    public static bool IsDrawingApp(string? app) =>
        app != null && DrawingApps.Any(d => app.Contains(d, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether pen scrolling should be on in this app. The main switch wins; then your choice for that app;
    /// otherwise it's off in drawing apps and on everywhere else.
    /// </summary>
    public static bool IsOnFor(LvenoConfig config, string? app)
    {
        if (!config.PenScroll) return false;
        if (app is null) return true;
        var rule = config.AppRules.FirstOrDefault(r => r.App.Equals(app, StringComparison.OrdinalIgnoreCase));
        return rule?.PenScroll ?? !IsDrawingApp(app);
    }

    /// <summary>Sets a settings profile up for pen scrolling on or off.</summary>
    public static void Apply(Profile profile, bool on)
    {
        var tip = profile.BindingSettings.TipButton;
        bool tipIsScroll = ButtonActions.Is<PenScrollTipBinding>(tip);
        if (on && !tipIsScroll)
            profile.BindingSettings.TipButton = ButtonActions.Store<PenScrollTipBinding>();
        else if (!on && tipIsScroll)
            profile.BindingSettings.TipButton = new PluginSettingStore(new AdaptiveBinding(PenAction.Tip));

        var filter = profile.Filters.FirstOrDefault(f => f?.Path == typeof(PenScrollFilter).FullName);
        if (filter != null) profile.Filters.Remove(filter);
        if (on)
        {
            var store = ButtonActions.Store<PenScrollFilter>();
            store.Enable = true;
            profile.Filters.Add(store);
        }
    }
}
