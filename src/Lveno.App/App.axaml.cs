using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Lveno.App.Controls;
using Lveno.App.Services;

namespace Lveno.App;

public partial class App : Application
{
    private MainWindow? _window;
    private NativeMenu? _menu;
    private Overlay.EdgePushListener? _edgePush;
    private readonly Overlay.OverlayManager _overlays = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Lveno lives in the menu bar; closing the window doesn't quit it.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // An unexpected error is logged and shown briefly; it never closes the app.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                ErrorLog.Write(e.Exception, "UI");
                e.Handled = true;
                _ = _window?.NotifyAsync("Something went wrong. It's logged; Lveno keeps running.");
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => ErrorLog.Write(e.ExceptionObject as Exception, "background");
            TaskScheduler.UnobservedTaskException += (_, e) => { ErrorLog.Write(e.Exception, "task"); e.SetObserved(); };
            LoginItem.MoveOldItem();
            bool background = desktop.Args?.Contains("--background") == true;

            _window = new MainWindow();
            _window.SessionChanged += (_, _) => BuildMenu();
            if (!background)
                _window.Show();
            _ = _window.StartAsync();

            CreateTrayIcon(desktop);
            BuildMenuBar(desktop);

            // Push to switch screens: the driver reports pushes; overlays animate them, even with the window closed.
            _edgePush = new Overlay.EdgePushListener();
            _edgePush.Received += _overlays.Handle;
            _overlays.Switched += id => _window.OnEdgePushSwitched(id);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// The Mac menu bar: the InkDot app menu (instead of Avalonia's default) plus View, Screens,
    /// Presets, Driver and Help.
    /// </summary>
    private void BuildMenuBar(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_window is null) return;
        var w = _window;

        NativeMenuItem Item(string header, Action action, string? gesture = null)
        {
            var item = new NativeMenuItem(header);
            if (gesture != null) item.Gesture = Avalonia.Input.KeyGesture.Parse(gesture);
            item.Click += (_, _) => action();
            return item;
        }
        NativeMenuItem Submenu(string header, params NativeMenuItemBase[] items)
        {
            var menu = new NativeMenu();
            foreach (var i in items) menu.Items.Add(i);
            return new NativeMenuItem(header) { Menu = menu };
        }
        void Open(string url) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open") { ArgumentList = { url }, UseShellExecute = false });

        // The app menu ("InkDot") is declared in App.axaml; just show the login tick.
        if (NativeMenu.GetMenu(this) is { } appMenu)
            foreach (var item in appMenu.Items.OfType<NativeMenuItem>().Where(i => i.Header == "Start at Login"))
            {
                item.IsChecked = LoginItem.IsEnabled;
                item.IsEnabled = LoginItem.IsAvailable;
            }

        // Menus along the top while InkDot is in front.
        var bar = new NativeMenu();
        bar.Items.Add(Submenu("View",
            Item("Drawing Area", () => w.OpenPage(0), "Cmd+D1"),
            Item("Pressure", () => w.OpenPage(1), "Cmd+D2"),
            Item("Pen Buttons", () => w.OpenPage(2), "Cmd+D3"),
            Item("Presets", () => w.OpenPage(3), "Cmd+D4"),
            Item("About", () => w.OpenPage(4), "Cmd+D5")));

        var screens = new NativeMenu();
        screens.Opening += (_, _) =>
        {
            screens.Items.Clear();
            var names = w.ScreenNames;
            int current = w.CurrentScreen;
            for (int i = 0; i < names.Count; i++)
            {
                int index = i;
                var item = new NativeMenuItem(names[i]) { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = current == i };
                if (i < 9) item.Gesture = Avalonia.Input.KeyGesture.Parse($"Cmd+Alt+D{i + 1}");
                item.Click += async (_, _) => await w.SwitchScreenAsync(index);
                screens.Items.Add(item);
            }
            if (names.Count > 1)
            {
                var all = new NativeMenuItem("All Screens") { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = current == Controls.ScreenMap.AllScreens };
                all.Click += async (_, _) => await w.SwitchScreenAsync(Controls.ScreenMap.AllScreens);
                screens.Items.Add(all);
            }
            if (names.Count == 0)
                screens.Items.Add(new NativeMenuItem("No tablet connected") { IsEnabled = false });
        };
        bar.Items.Add(new NativeMenuItem("Screens") { Menu = screens });

        var presets = new NativeMenu();
        presets.Opening += (_, _) =>
        {
            presets.Items.Clear();
            foreach (var preset in Presets.Quick)
            {
                var item = new NativeMenuItem(preset.Name) { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = w.Config.ActivePreset == preset.Name };
                item.Click += async (_, _) => await w.UseQuickPresetAsync(preset);
                presets.Items.Add(item);
            }
            var saved = Presets.Saved();
            if (saved.Count > 0) presets.Items.Add(new NativeMenuItemSeparator());
            foreach (var name in saved)
            {
                var item = new NativeMenuItem(name) { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = w.Config.ActivePreset == name };
                item.Click += async (_, _) => await w.UseSavedPresetAsync(name);
                presets.Items.Add(item);
            }
            presets.Items.Add(new NativeMenuItemSeparator());
            presets.Items.Add(Item("Manage Presets…", () => w.OpenPage(3)));
        };
        bar.Items.Add(new NativeMenuItem("Presets") { Menu = presets });

        bar.Items.Add(Submenu("Driver",
            Item("Look for Tablets Again", async () => await w.RedetectAsync(), "Cmd+R"),
            Item("Restart Driver", async () => await w.RestartDriverAsync(), "Cmd+Shift+R"),
            new NativeMenuItemSeparator(),
            Item("Show Settings Folder", () => Open(DriverService.AppDataDirectory)),
            Item("Show Error Log", () => Open(System.IO.File.Exists(ErrorLog.FilePath) ? ErrorLog.FilePath : DriverService.AppDataDirectory))));

        bar.Items.Add(Submenu("Help",
            Item("Show Welcome Again", () => w.ShowOnboarding()),
            Item("InkDot Help", () => w.OpenPage(4)),
            Item("Lveno Website", () => Open("https://lveno.agency")),
            new NativeMenuItemSeparator(),
            Item("OpenTabletDriver Project", () => Open("https://github.com/OpenTabletDriver/OpenTabletDriver"))));

        NativeMenu.SetMenu(w, bar);
    }

    // App menu handlers (see App.axaml).
    private void OnAbout(object? sender, EventArgs e) => _window?.OpenPage(4);
    private void OnSettings(object? sender, EventArgs e) => _window?.OpenPage(0);
    private void OnCloseWindow(object? sender, EventArgs e) => _window?.Close();
    private void OnStartAtLogin(object? sender, EventArgs e)
    {
        try { LoginItem.SetEnabled(!LoginItem.IsEnabled); } catch { /* not in Applications */ }
        if (sender is NativeMenuItem item) item.IsChecked = LoginItem.IsEnabled;
        _window?.RefreshLoginToggle();
        BuildMenu();
    }

    private void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _menu = new NativeMenu();
        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Lveno/Assets/tray.png"))),
            ToolTipText = "InkDot by Lveno",
            Menu = _menu,
        };
        tray.Clicked += (_, _) => _window?.ShowFromMenu();
        TrayIcon.SetIcons(this, [tray]);
        BuildMenu();
    }

    private void BuildMenu()
    {
        if (_menu is null || _window is null)
            return;
        _menu.Items.Clear();

        var open = new NativeMenuItem("Open InkDot");
        open.Click += (_, _) => _window.ShowFromMenu();
        _menu.Items.Add(open);
        _menu.Items.Add(new NativeMenuItemSeparator());

        var screens = _window.ScreenNames;
        if (screens.Count > 0)
        {
            int current = _window.CurrentScreen;
            for (int i = 0; i < screens.Count; i++)
            {
                int index = i;
                var item = new NativeMenuItem(screens[i])
                {
                    ToggleType = NativeMenuItemToggleType.Radio,
                    IsChecked = current == i,
                };
                item.Click += async (_, _) => await _window.SwitchScreenAsync(index);
                _menu.Items.Add(item);
            }
            if (screens.Count > 1)
            {
                var all = new NativeMenuItem("All screens")
                {
                    ToggleType = NativeMenuItemToggleType.Radio,
                    IsChecked = current == ScreenMap.AllScreens,
                };
                all.Click += async (_, _) => await _window.SwitchScreenAsync(ScreenMap.AllScreens);
                _menu.Items.Add(all);
            }
            _menu.Items.Add(new NativeMenuItemSeparator());
        }

        if (_window.Session != null)
        {
            var presets = new NativeMenu();
            var active = _window.Config.ActivePreset;
            foreach (var preset in Presets.Quick)
            {
                var item = new NativeMenuItem(preset.Name) { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = active == preset.Name };
                item.Click += async (_, _) => await _window.UseQuickPresetAsync(preset);
                presets.Items.Add(item);
            }
            var saved = Presets.Saved();
            if (saved.Count > 0)
            {
                presets.Items.Add(new NativeMenuItemSeparator());
                foreach (var name in saved)
                {
                    var item = new NativeMenuItem(name) { ToggleType = NativeMenuItemToggleType.Radio, IsChecked = active == name };
                    item.Click += async (_, _) => await _window.UseSavedPresetAsync(name);
                    presets.Items.Add(item);
                }
            }
            _menu.Items.Add(new NativeMenuItem("Presets") { Menu = presets });
            _menu.Items.Add(new NativeMenuItemSeparator());
        }

        var login = new NativeMenuItem("Start at login")
        {
            ToggleType = NativeMenuItemToggleType.CheckBox,
            IsChecked = LoginItem.IsEnabled,
            IsEnabled = LoginItem.IsAvailable,
        };
        login.Click += (_, _) =>
        {
            try { LoginItem.SetEnabled(!LoginItem.IsEnabled); } catch { /* not in Applications */ }
            _window.RefreshLoginToggle();
            BuildMenu();
        };
        _menu.Items.Add(login);
        _menu.Items.Add(new NativeMenuItemSeparator());

        var quit = new NativeMenuItem("Quit InkDot");
        quit.Click += (_, _) => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        _menu.Items.Add(quit);
    }
}
