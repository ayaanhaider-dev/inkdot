using Lveno.Plugins;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Platform.Pointer;

namespace Lveno.App.Services;

/// <summary>One thing a pen button can do, and how to store it in OpenTabletDriver settings.</summary>
public sealed record ButtonAction(string Label, Func<int, PluginSettingStore?> Create, Func<PluginSettingStore?, bool> Matches)
{
    public override string ToString() => Label;
}

public static class ButtonActions
{
    public static readonly ButtonAction[] All =
    [
        new("Pen default", i => new PluginSettingStore(new AdaptiveBinding(Enum.Parse<PenAction>($"BarrelButton{i + 1}"))),
            s => Is<AdaptiveBinding>(s)),
        new("Right click", _ => Store<MouseBinding>(("Button", "Right")),
            s => Is<MouseBinding>(s) && Value(s, "Button") == "Right"),
        new("Middle click", _ => Store<MouseBinding>(("Button", "Middle")),
            s => Is<MouseBinding>(s) && Value(s, "Button") == "Middle"),
        new("Next screen", _ => Store<SwitchScreenBinding>(("Action", SwitchScreenBinding.NextScreen), ("MatchScreenShape", true)),
            s => Is<SwitchScreenBinding>(s) && Value(s, "Action") == SwitchScreenBinding.NextScreen),
        new("Previous screen", _ => Store<SwitchScreenBinding>(("Action", SwitchScreenBinding.PreviousScreen), ("MatchScreenShape", true)),
            s => Is<SwitchScreenBinding>(s) && Value(s, "Action") == SwitchScreenBinding.PreviousScreen),
        new("All screens", _ => Store<SwitchScreenBinding>(("Action", SwitchScreenBinding.AllScreens), ("MatchScreenShape", true)),
            s => Is<SwitchScreenBinding>(s) && Value(s, "Action") == SwitchScreenBinding.AllScreens),
        new("Undo  ⌘Z", _ => Store<MultiKeyBinding>(("Keys", "Application+Z")),
            s => Is<MultiKeyBinding>(s) && Value(s, "Keys") == "Application+Z"),
        new("Redo  ⇧⌘Z", _ => Store<MultiKeyBinding>(("Keys", "Application+Shift+Z")),
            s => Is<MultiKeyBinding>(s) && Value(s, "Keys") == "Application+Shift+Z"),
        new("Eyedropper  I", _ => Store<MultiKeyBinding>(("Keys", "I")),
            s => Is<MultiKeyBinding>(s) && Value(s, "Keys") == "I"),
        new("Eraser  E", _ => Store<MultiKeyBinding>(("Keys", "E")),
            s => Is<MultiKeyBinding>(s) && Value(s, "Keys") == "E"),
        new("Nothing", _ => null,
            s => s?.Path is null),
    ];

    public static ButtonAction? ByLabel(string? label) => All.FirstOrDefault(a => a.Label == label);

    /// <summary>Builds a settings entry without creating the binding, so nothing driver-side runs in the app.</summary>
    public static PluginSettingStore Store<T>(params (string Property, object Value)[] values) =>
        Store(typeof(T), values);

    public static PluginSettingStore Store(Type type, params (string Property, object Value)[] values)
    {
        var store = new PluginSettingStore(type);
        foreach (var (property, value) in values)
        {
            var setting = store.Settings.FirstOrDefault(x => x.Property == property);
            if (setting is null)
                store.Settings.Add(new PluginSetting(property, value));
            else
                setting.SetValue(value);
        }
        return store;
    }

    public static bool Is<T>(PluginSettingStore? store) => store?.Path == typeof(T).FullName;

    public static string? Value(PluginSettingStore? store, string property)
    {
        var setting = store?.Settings.FirstOrDefault(x => x.Property == property);
        return setting?.HasValue == true ? setting.Value!.ToString() : null;
    }
}
