using Avalonia.Controls;
using Avalonia.Interactivity;
using Lveno.App.Controls;
using Lveno.App.Services;
using Lveno.Plugins;
using OpenTabletDriver.Plugin;

namespace Lveno.App.Pages;

public partial class AreaPage : UserControl
{
    private IShell? _shell;
    private int _screen = ScreenMap.Custom;
    private bool _loading;

    public AreaPage()
    {
        InitializeComponent();

        Screens.SelectionChanged += (_, index) => { _screen = index; Update(); };
        AllScreensButton.Click += (_, _) => { _screen = ScreenMap.AllScreens; Update(); };
        MatchShape.IsCheckedChanged += (_, _) => Update();
        LeftHanded.IsCheckedChanged += (_, _) => Update();
        EdgePushToggle.IsCheckedChanged += async (_, _) =>
        {
            if (_loading || _shell?.Session is not { } session) return;
            bool on = EdgePushToggle.IsChecked == true;
            _shell.Config.EdgePush = on;
            _shell.Config.Save();
            session.SetEdgePush(on);
            await _shell.ApplyAsync(on ? "Push to switch screens is on." : "Push to switch screens is off.");
        };
        SizeSlider.ValueChanged += (_, _) => Update();
        RevertButton.Click += (_, _) => Load(_shell!);
        ApplyButton.Click += OnApply;
    }

    private TabletSession? Session => _shell?.Session;

    public void Load(IShell shell)
    {
        _shell = shell;
        if (Session is not { } s)
            return;

        _loading = true;
        MouseModeNote.IsVisible = s.MouseMode;
        EdgePushToggle.IsChecked = shell.Config.EdgePush;
        EdgePushToggle.IsEnabled = s.Displays.Count > 1;
        Screens.Displays = s.Displays;
        Screens.Names = s.ScreenNames;
        Screens.Wallpapers = MacApps.Wallpapers();
        Screens.CursorIndex = ScreenLayout.CursorScreen(s.Displays);
        _screen = s.CurrentScreenIndex();
        MatchShape.IsChecked = s.Absolute.LockAspectRatio;
        LeftHanded.IsChecked = Math.Abs(Math.Abs(s.Absolute.Tablet.Rotation) - 180) < 1;
        Preview.SetTablet(s.Digitizer.Width, s.Digitizer.Height);

        // Express the saved tablet area as a percentage of the largest area with the same shape.
        var current = s.Absolute.Tablet.Area;
        var largest = ScreenMath.FitAspect(s.FullTablet, current.Width / current.Height);
        SizeSlider.Value = Math.Clamp(Math.Round(current.Width / largest.Width * 100 / 5) * 5, 30, 100);
        _loading = false;

        Update();
    }

    /// <summary>Called every second: marks the screen your pointer is on.</summary>
    public void RefreshCursor()
    {
        if (Session is not { } s) return;
        int cursor = ScreenLayout.CursorScreen(s.Displays);
        if (cursor == Screens.CursorIndex) return;
        Screens.CursorIndex = cursor;
        Update();
    }

    /// <summary>The screen area the pen maps to, from the current choice.</summary>
    private Area TargetScreenArea(TabletSession s) => _screen switch
    {
        ScreenMap.AllScreens => ScreenMath.AreaForAllScreens(s.Displays),
        >= 0 when _screen < s.Displays.Count => ScreenMath.AreaForScreen(s.Displays[_screen]),
        _ => s.Absolute.Display.Area,
    };

    private Area TargetTabletArea(TabletSession s, Area screen)
    {
        var full = s.FullTablet;
        float aspect = MatchShape.IsChecked == true ? screen.Width / screen.Height : full.Width / full.Height;
        var area = ScreenMath.FitAspect(full, aspect, (float)SizeSlider.Value / 100f);
        area.Rotation = LeftHanded.IsChecked == true ? 180 : 0;
        return area;
    }

    private void Update()
    {
        if (_loading || Session is not { } s)
            return;

        Screens.SelectedIndex = _screen;
        var screen = TargetScreenArea(s);
        var tablet = TargetTabletArea(s, screen);

        ScreenTitle.Text = _screen switch
        {
            ScreenMap.AllScreens => s.Displays.Count == 1 ? "Your screen" : $"All {s.Displays.Count} screens",
            ScreenMap.Custom => "Custom area",
            _ => s.Displays.Count == 1 ? s.ScreenName(_screen) : $"{_screen + 1}  ·  {s.ScreenName(_screen)}",
        };
        var tags = new List<string> { $"{screen.Width:0} × {screen.Height:0} pt" };
        if (_screen >= 0 && _screen < s.Displays.Count)
        {
            if (s.Displays[_screen].IsMain) tags.Add("main");
            if (_screen == Screens.CursorIndex) tags.Add("pointer here");
        }
        ScreenDetail.Text = string.Join("  ·  ", tags);
        AllScreensButton.IsVisible = s.Displays.Count > 1;

        SizeValue.Text = $"{SizeSlider.Value:0}%";
        Preview.ActiveArea = tablet;
    }

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_shell is null || Session is not { } s)
            return;

        var screen = TargetScreenArea(s);
        s.Absolute.Display.Area = screen;
        s.Absolute.Tablet.Area = TargetTabletArea(s, screen);
        s.Absolute.LockAspectRatio = MatchShape.IsChecked == true;

        ApplyButton.IsEnabled = false;
        try
        {
            await _shell.ApplyAsync("Saved. Draw away.");
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }
}
