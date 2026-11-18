using Lveno.App.Services;

namespace Lveno.App.Pages;

/// <summary>What a page can ask of the main window.</summary>
public interface IShell
{
    TabletSession? Session { get; }

    /// <summary>Lveno's own settings: per-app buttons.</summary>
    LvenoConfig Config { get; }

    /// <summary>Applies and saves the session's settings. Pass a message to tell the user, or null to stay quiet.</summary>
    Task ApplyAsync(string? doneMessage = "Saved.");

    /// <summary>Shows a short message.</summary>
    Task NotifyAsync(string message);

    /// <summary>Applies a built-in quick preset on top of the current settings, and saves.</summary>
    Task UseQuickPresetAsync(QuickPreset preset);

    /// <summary>Replaces all settings with a saved preset, and saves.</summary>
    Task UseSavedPresetAsync(string name);

    /// <summary>Restarts the driver and reconnects.</summary>
    Task RestartDriverAsync();

    /// <summary>Asks the driver to look for tablets again.</summary>
    Task RedetectAsync();
}
