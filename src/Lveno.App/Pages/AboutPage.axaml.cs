using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using OpenTabletDriver.Desktop;

namespace Lveno.App.Pages;

public partial class AboutPage : UserControl
{
    private IShell? _shell;

    public AboutPage()
    {
        InitializeComponent();

        var lveno = Assembly.GetEntryAssembly()?.GetName().Version;
        var otd = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? typeof(AppInfo).Assembly.GetName().Version?.ToString();
        Version.Text = $"Version {lveno?.ToString(3)} · OpenTabletDriver {otd}";

        OpenFolderButton.Click += (_, _) => Open(Path.GetDirectoryName(AppInfo.Current.SettingsFile)!);
        SourceLink.Click += (_, _) => Open("https://github.com/OpenTabletDriver/OpenTabletDriver");
        RedetectButton.Click += async (_, _) => { if (_shell != null) await _shell.RedetectAsync(); };
        RestartButton.Click += async (_, _) => { if (_shell != null) await _shell.RestartDriverAsync(); };

        StartAtLogin.IsEnabled = Services.LoginItem.IsAvailable;
        StartAtLogin.IsChecked = Services.LoginItem.IsEnabled;
        if (!Services.LoginItem.IsAvailable)
            LoginHint.Text = "Available when InkDot runs from Applications.";
        StartAtLogin.IsCheckedChanged += (_, _) =>
        {
            try { Services.LoginItem.SetEnabled(StartAtLogin.IsChecked == true); }
            catch { StartAtLogin.IsChecked = Services.LoginItem.IsEnabled; }
        };
    }

    /// <summary>Keeps the switch in step when login is changed from the menu bar.</summary>
    public void RefreshLogin() => StartAtLogin.IsChecked = Services.LoginItem.IsEnabled;

    public void Load(IShell shell)
    {
        _shell = shell;
        if (shell.Session is not { } s)
            return;

        TabletName.Text = s.TabletName;
        Surface.Text = $"{s.Digitizer.Width:0.0} × {s.Digitizer.Height:0.0} mm · {s.Tablet.Properties.Specifications.Pen?.MaxPressure + 1} pressure levels";
        ScreenCount.Text = s.Displays.Count == 1 ? "1 screen" : $"{s.Displays.Count} screens";
        SettingsPath.Text = AppInfo.Current.SettingsFile;
    }

    private static void Open(string target) => Services.Shell.Open(target);
}
