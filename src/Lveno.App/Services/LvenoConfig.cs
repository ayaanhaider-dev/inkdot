using System.Text.Json;

namespace Lveno.App.Services;

/// <summary>Pen buttons for one app. Labels come from <see cref="ButtonActions.All"/>.</summary>
public sealed class AppRule
{
    public string App { get; set; } = "";

    /// <summary>Where the app lives, for its icon.</summary>
    public string? BundlePath { get; set; }

    /// <summary>Pen scrolling in this app: on, off, or null to follow the default (off in drawing apps).</summary>
    public bool? PenScroll { get; set; }
    public List<string> Buttons { get; set; } = [];
}

/// <summary>Lveno's own settings (driver settings live in OpenTabletDriver's settings.json).</summary>
public sealed class LvenoConfig
{
    public List<AppRule> AppRules { get; set; } = [];

    /// <summary>The physical screen the drawing area is on (macOS display ID), so it can follow that screen.</summary>
    public uint? AreaScreenId { get; set; }

    /// <summary>True when the drawing area covers every screen.</summary>
    public bool AreaAllScreens { get; set; }

    /// <summary>The screen layout last seen, to notice changes made while Lveno was closed.</summary>
    public string? LayoutSignature { get; set; }

    /// <summary>Push the pen against a tablet edge to move to the screen beyond it.</summary>
    public bool EdgePush { get; set; }

    /// <summary>True once the welcome setup has been finished or skipped.</summary>
    public bool OnboardingDone { get; set; }

    /// <summary>Pen scrolling main switch.</summary>
    public bool PenScroll { get; set; }

    /// <summary>Name of the preset last used, for the tick in the menu bar.</summary>
    public string? ActivePreset { get; set; }

    private static string FilePath => Path.Combine(DriverService.AppDataDirectory, "lveno.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static LvenoConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<LvenoConfig>(File.ReadAllText(FilePath)) ?? new();
        }
        catch
        {
            // A broken file shouldn't stop the app; start fresh.
        }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(DriverService.AppDataDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
