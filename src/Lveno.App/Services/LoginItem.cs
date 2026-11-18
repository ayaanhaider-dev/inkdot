namespace Lveno.App.Services;

/// <summary>
/// Starts Lveno quietly (menu bar only) when you log in, using a per-user LaunchAgent.
/// </summary>
public static class LoginItem
{
    private const string Label = "agency.lveno.inkdot";
    private const string OldLabel = "agency.lveno.tablet"; // before the rename to InkDot

    private static string PlistPath => PathFor(Label);

    private static string PathFor(string label) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", label + ".plist");

    /// <summary>Replaces the old "Lveno Tablet" login item with InkDot's, keeping your choice.</summary>
    public static void MoveOldItem()
    {
        try
        {
            var old = PathFor(OldLabel);
            if (!File.Exists(old)) return;
            File.Delete(old);
            if (IsAvailable && !IsEnabled) SetEnabled(true);
        }
        catch
        {
            // Not worth stopping the app for.
        }
    }

    /// <summary>The .app bundle this copy runs from, or null when running from a build folder.</summary>
    public static string? BundlePath
    {
        get
        {
            // …/InkDot.app/Contents/MacOS/
            var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('/'));
            var bundle = dir.Parent?.Parent;
            return bundle != null && bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? bundle.FullName : null;
        }
    }

    public static bool IsAvailable => OperatingSystem.IsMacOS() && BundlePath != null;

    public static bool IsEnabled => File.Exists(PlistPath);

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(PlistPath);
            return;
        }

        var bundle = BundlePath ?? throw new InvalidOperationException("Install InkDot in Applications first.");
        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        File.WriteAllText(PlistPath, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{Label}</string>
              <key>ProgramArguments</key>
              <array>
                <string>/usr/bin/open</string>
                <string>-a</string>
                <string>{System.Security.SecurityElement.Escape(bundle)}</string>
                <string>--args</string>
                <string>--background</string>
              </array>
              <key>RunAtLoad</key><true/>
            </dict>
            </plist>
            """);
    }
}
