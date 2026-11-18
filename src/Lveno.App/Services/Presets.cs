using Lveno.Plugins;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection;

namespace Lveno.App.Services;

/// <summary>A one-click setup. Quick presets tweak your current settings; saved presets restore a snapshot.</summary>
/// <param name="Curve">Shown as the tile's icon; null means the preset is mouse mode.</param>
public sealed record QuickPreset(string Name, string Description, PressureCurve? Curve, Action<TabletSession> Apply);

public static class Presets
{
    /// <summary>Built-in starting points. Each keeps your screen, area and buttons, and changes how the pen behaves.</summary>
    public static readonly QuickPreset[] Quick =
    [
        new("Drawing", "Normal pressure. A good start for anything.", PressureCurve.Presets[3],
            s => Set(s, mouse: false, pressure: true, PressureCurve.Presets[3])),
        new("Sketching", "Soft pressure for light, loose lines.", PressureCurve.Presets[1],
            s => Set(s, mouse: false, pressure: true, PressureCurve.Presets[1])),
        new("Inking", "Firm pressure for clean, even lines.", PressureCurve.Presets[5],
            s => Set(s, mouse: false, pressure: true, PressureCurve.Presets[5])),
        new("Mouse", "Moves the cursor like a mouse. No pressure.", null,
            s => Set(s, mouse: true, pressure: false, PressureCurve.Linear)),
    ];

    public static DateTime SavedAt(string name) =>
        File.GetLastWriteTime(Path.Combine(Folder.FullName, name + ".json"));

    private static void Set(TabletSession s, bool mouse, bool pressure, PressureCurve curve)
    {
        s.MouseMode = mouse;
        s.Profile.BindingSettings.DisablePressure = !pressure;
        var old = s.Profile.Filters.FirstOrDefault(f => f?.Path == typeof(PressureCurveFilter).FullName);
        if (old != null) s.Profile.Filters.Remove(old);
        var store = ButtonActions.Store(typeof(PressureCurveFilter), ("Curve", curve.ToString()));
        store.Enable = true;
        s.Profile.Filters.Add(store);
    }

    private static DirectoryInfo Folder => Directory.CreateDirectory(AppInfo.Current.PresetDirectory);

    /// <summary>Your saved presets, by name.</summary>
    public static IReadOnlyList<string> Saved() =>
        Folder.EnumerateFiles("*.json").Select(f => Path.GetFileNameWithoutExtension(f.Name)).Order().ToList();

    public static void Save(string name, Settings settings)
    {
        var safe = string.Concat(name.Trim().Split(Path.GetInvalidFileNameChars()));
        if (safe.Length == 0) throw new ArgumentException("Give the preset a name.");
        settings.Serialize(new FileInfo(Path.Combine(Folder.FullName, safe + ".json")));
    }

    public static Settings? Load(string name) =>
        Settings.TryDeserialize(new FileInfo(Path.Combine(Folder.FullName, name + ".json")), out var settings) ? settings : null;

    public static void Delete(string name) => File.Delete(Path.Combine(Folder.FullName, name + ".json"));

    /// <summary>A deep copy, so a preset never shares objects with the live settings.</summary>
    public static Settings Copy(Settings settings) =>
        JsonConvert.DeserializeObject<Settings>(JsonConvert.SerializeObject(settings))!;
}
