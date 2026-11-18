using Avalonia.Controls;
using Avalonia.Threading;
using Lveno.App.Controls;
using Lveno.App.Pages;
using Lveno.App.Services;
using Lveno.Plugins;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Plugin.Tablet;

namespace Lveno.App;

public partial class MainWindow : Window, IShell
{
    private const int AreaTab = 0, PressureTab = 1, ButtonsTab = 2, PresetsTab = 3, AboutTab = 4;

    private readonly DriverService _driver = new();
    private readonly DispatcherTimer _watch;
    private Func<Task>? _primaryAction, _secondaryAction;
    private CancellationTokenSource? _toastCts;
    private bool _starting, _restarting, _permissionsShown, _debugOn;
    private int _page;
    private string? _appRuleActive; // the app whose own settings are applied now; null = your normal setup
    private string? _appKey;        // what the per-app watcher applied last; "" = normal setup, null = unknown

    public TabletSession? Session { get; private set; }
    public LvenoConfig Config { get; } = LvenoConfig.Load();

    /// <summary>Raised when the tablet or its settings change, so the menu bar can refresh.</summary>
    public event EventHandler? SessionChanged;

    public MainWindow()
    {
        InitializeComponent();

        NavArea.Click += (_, _) => ShowPage(AreaTab);
        NavPressure.Click += (_, _) => ShowPage(PressureTab);
        NavButtons.Click += (_, _) => ShowPage(ButtonsTab);
        NavPresets.Click += (_, _) => ShowPage(PresetsTab);
        NavAbout.Click += (_, _) => ShowPage(AboutTab);

        StatePrimary.Click += async (_, _) => { if (_primaryAction != null) await _primaryAction(); };
        StateSecondary.Click += async (_, _) => { if (_secondaryAction != null) await _secondaryAction(); };
        InputMonitoringButton.Click += (_, _) => MacPermissions.RequestInputMonitoring();
        AccessibilityButton.Click += (_, _) => MacPermissions.RequestAccessibility();

        QuitXpPenButton.Click += (_, _) =>
        {
            XpPenDriver.Quit();
            XpPenWarning.IsVisible = XpPenDriver.IsRunning;
            ShowToast("XP-Pen's driver is closed for now.");
        };

        DragStrip.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        // The dot's eyes follow your pen (or mouse) across the window.
        PointerMoved += (_, e) =>
        {
            var dot = StateView.IsVisible ? StateDot : Logo.Dot;
            dot.LookAt(e.GetPosition(dot));
        };

        // Closing the window keeps Lveno in the menu bar. Quit from the menu bar or with ⌘Q.
        Closing += (_, e) =>
        {
            if (e.CloseReason is not WindowCloseReason.ApplicationShutdown and not WindowCloseReason.OSShutdown)
            {
                e.Cancel = true;
                Hide();
                _ = SetDebugAsync(false);
            }
        };

        _driver.Disconnected += (_, _) => Dispatcher.UIThread.Post(async () => await OnDisconnectedAsync());
        _driver.TabletsChanged += (_, tablets) => Dispatcher.UIThread.Post(async () => await OnTabletsChangedAsync(tablets.ToList()));
        _driver.DeviceReport += (_, report) =>
        {
            // Reports without pressure mean the pen left the tablet.
            float max = Session?.Tablet.Properties.Specifications.Pen?.MaxPressure ?? 8191;
            float value = report.Data["Pressure"]?.ToObject<float>() is { } raw ? raw / max : -1;
            var digitizer = Session?.Digitizer;
            float px = report.Data["Position"]?["X"]?.ToObject<float>() / Math.Max(1f, digitizer?.MaxX ?? 1f) ?? 0.5f;
            float py = report.Data["Position"]?["Y"]?.ToObject<float>() / Math.Max(1f, digitizer?.MaxY ?? 1f) ?? 0.5f;
            Dispatcher.UIThread.Post(() =>
            {
                PressurePageView.OnRawPressure(value);
                if (Onboarding.IsVisible) Onboarding.OnPen(value, px, py);
            });
        };

        // Background checks: per-app buttons, XP-Pen's driver, and permissions while we wait for them.
        _watch = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) => await WatchAsync());
        _watch.Start();
    }

    // ---- Startup -------------------------------------------------------------------------------

    public async Task StartAsync()
    {
        if (_starting)
            return;
        _starting = true;
        try
        {
            // If the driver is already running it has its permissions; this window only talks to it.
            // That also lets a development copy of the app attach to the installed driver.
            bool driverRunning = OpenTabletDriver.Instance.Exists(DriverService.DaemonName)
                                 || System.Diagnostics.Process.GetProcessesByName(DriverService.DaemonName).Length > 0;
            bool devMode = Environment.GetEnvironmentVariable("LVENO_DEV") == "1";
            if (!driverRunning && !devMode && !MacPermissions.HasAll)
            {
                // Permissions are a step of the welcome setup. The background check starts the driver
                // the moment both are allowed, while the setup carries on.
                _permissionsShown = true;
                if (!Onboarding.IsVisible)
                    ShowOnboarding();
                return;
            }
            if (!Config.OnboardingDone && IsVisible && !Onboarding.IsVisible)
                ShowOnboarding();

            ShowState(DotExpression.Focused, "Starting the driver.", "One moment.");
            try
            {
                await _driver.ConnectAsync(TimeSpan.FromSeconds(20));
            }
            catch (Exception ex)
            {
                ShowState(DotExpression.Sleep, "The driver didn't start.",
                    "Try again. If it keeps happening, restart your Mac.",
                    detail: ex.Message,
                    primary: ("Try again", StartAsync));
                return;
            }

            await LoadTabletAsync();
        }
        finally
        {
            _starting = false;
        }
    }

    private async Task LoadTabletAsync(IReadOnlyList<TabletReference>? tablets = null)
    {
        tablets ??= (await _driver.Daemon.GetTablets()).ToList();
        var tablet = tablets.FirstOrDefault();
        _appRuleActive = null; _appKey = null;

        if (tablet is null)
        {
            Session = null;
            UpdateSidebar();
            SessionChanged?.Invoke(this, EventArgs.Empty);
            ShowState(DotExpression.Sleep, "Plug in your tablet.",
                "Lveno finds it on its own. Already plugged in? Try another cable or port.",
                primary: ("Look again", RedetectAsync));
            return;
        }

        Session = await TabletSession.LoadAsync(_driver, tablet);
        ReloadPages();
        HideState();
        await SetDebugAsync(WantsPenData);

        // Screens may have changed while Lveno was closed: keep drawing on the same physical screen.
        var signature = ScreenLayout.Signature(Session.Displays);
        bool changedWhileAway = !_handlingScreens && Config.LayoutSignature != null && Config.LayoutSignature != signature;
        _layoutSig = signature;
        bool update = Session.EnsureScreenFix();          // older settings: add the screen fix
        if (Session.EdgePushNeedsUpdate) { Session.SetEdgePush(true); update = true; } // older push time
        if (update)
            Dispatcher.UIThread.Post(async () => await ApplyAsync(null));
        if (changedWhileAway)
            Dispatcher.UIThread.Post(async () => await PutAreaOnRememberedScreenAsync());
        else
            RememberAreaScreen();
    }

    // ---- Screens -------------------------------------------------------------------------------

    private string? _layoutSig;
    private bool _handlingScreens;

    /// <summary>
    /// The driver moved the drawing area to another screen (push to switch). Mirror it here so the
    /// next save keeps it, and update the sidebar, Area page and menu bar.
    /// </summary>
    public void OnEdgePushSwitched(uint screenId)
    {
        if (Session is not { } s) return;
        int index = s.Displays.ToList().FindIndex(d => d.Id == screenId);
        if (index < 0) return;

        var target = ScreenMath.AreaForScreen(s.Displays[index]);
        s.Absolute.Display.Area = target;
        if (s.Absolute.LockAspectRatio)
        {
            var current = s.Absolute.Tablet.Area;
            var largest = ScreenMath.FitAspect(s.FullTablet, current.Width / current.Height);
            var area = ScreenMath.FitAspect(s.FullTablet, target.Width / target.Height, current.Width / largest.Width);
            area.Rotation = current.Rotation;
            s.Absolute.Tablet.Area = area;
        }
        try { s.Settings.Serialize(new FileInfo(AppInfo.Current.SettingsFile)); } catch { /* next save will catch up */ }
        _appKey = null; // per-app copies must carry the new area too
        RememberAreaScreen();
        UpdateSidebar();
        AreaPageView.Load(this);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Every second: notice screens being plugged in, unplugged or rearranged.</summary>
    private async Task CheckScreensAsync()
    {
        if (Session is not { } s || _handlingScreens || _starting) return;
        AreaPageView.RefreshCursor();

        var layout = ScreenLayout.Current();
        if (layout.Count == 0) return;
        var signature = ScreenLayout.Signature(layout);
        _layoutSig ??= signature;
        if (signature == _layoutSig) return;
        _layoutSig = signature;

        string message = layout.Count > s.Displays.Count ? "Screen connected."
            : layout.Count < s.Displays.Count ? "Screen disconnected."
            : "Screens rearranged.";
        await ScreensChangedAsync(s.Displays.Count, message);
    }

    /// <summary>
    /// Put the drawing area back on the same physical screen, or the main screen if that one is gone.
    /// No driver restart: ScreenFixFilter lines the driver up with the new layout on its own.
    /// </summary>
    private async Task ScreensChangedAsync(int previousCount, string message)
    {
        _handlingScreens = true;
        try
        {
            RememberAreaScreen(saveLayout: false); // which screen, from the layout we had
            await LoadTabletAsync();              // fresh screen list
            await PutAreaOnRememberedScreenAsync(message);
        }
        finally
        {
            _handlingScreens = false;
        }
    }

    /// <summary>Notes which physical screen the area is on, so it can follow that screen.</summary>
    private void RememberAreaScreen(bool saveLayout = true)
    {
        if (Session is not { } s) return;
        int index = s.CurrentScreenIndex();
        if (index == ScreenMap.AllScreens)
        {
            Config.AreaAllScreens = true;
            Config.AreaScreenId = null;
        }
        else if (index >= 0)
        {
            Config.AreaAllScreens = false;
            Config.AreaScreenId = s.Displays[index].Id;
        }
        if (saveLayout)
            Config.LayoutSignature = ScreenLayout.Signature(s.Displays);
        Config.Save();
    }

    private async Task PutAreaOnRememberedScreenAsync(string? message = null)
    {
        if (Session is not { } s) return;
        var screens = s.Displays.ToList();
        int target;
        string? note = message;
        if (Config.AreaAllScreens)
        {
            target = ScreenMap.AllScreens;
        }
        else
        {
            target = screens.FindIndex(d => d.Id == Config.AreaScreenId);
            if (target < 0)
            {
                target = Math.Max(0, screens.FindIndex(d => d.IsMain));
                if (Config.AreaScreenId != null)
                    note = $"{message ?? "That screen is gone."} Drawing on {s.ScreenName(target)} now.";
            }
        }

        if (!s.MouseMode && s.CurrentScreenIndex() != target)
            await SwitchScreenAsync(target, announce: false);
        RememberAreaScreen();
        if (note != null) ShowToast(note);
    }

    private void ReloadPages()
    {
        AreaPageView.Load(this);
        PressurePageView.Load(this);
        ButtonsPageView.Load(this);
        PresetsPageView.Load(this);
        AboutPageView.Load(this);
        UpdateSidebar();
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task OnDisconnectedAsync()
    {
        if (_restarting)
            return;
        Session = null;
        _debugOn = false;
        ShowState(DotExpression.Focused, "Reconnecting.", "The driver stopped. Starting it again.");
        await Task.Delay(800);
        await StartAsync();
    }

    private async Task OnTabletsChangedAsync(IReadOnlyList<TabletReference> tablets)
    {
        var name = tablets.FirstOrDefault()?.Properties.Name;
        if (name == Session?.TabletName)
            return;
        await LoadTabletAsync(tablets);
        if (Session != null)
            ShowToast($"{Session.TabletName} connected.");
    }

    /// <summary>
    /// Dev only: when "snapshot.request" appears in the app data folder, save a picture of every page
    /// to Snapshots/, so the design can be reviewed without clicking through the app.
    /// </summary>
    private async Task SnapshotIfRequestedAsync()
    {
        var request = Path.Combine(DriverService.AppDataDirectory, "snapshot.request");
        if (!File.Exists(request)) return;
        File.Delete(request);

        var folder = Directory.CreateDirectory(Path.Combine(DriverService.AppDataDirectory, "Snapshots")).FullName;
        int current = _page;
        string[] names = ["1-area", "2-pressure", "3-buttons", "4-presets", "5-about"];
        for (int i = 0; i < names.Length; i++)
        {
            ShowPage(i);
            await Task.Delay(400);
            var size = new Avalonia.PixelSize((int)Bounds.Width * 2, (int)Bounds.Height * 2);
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(192, 192));
            bitmap.Render(this);
            bitmap.Save(Path.Combine(folder, names[i] + ".png"));
        }
        ShowPage(current);

        // The push-to-switch overlay, drawn on a stand-in screen.
        void Frame(string file, Action<Overlay.OverlayCanvas> setup)
        {
            var canvas = new Overlay.OverlayCanvas();
            var host = new Border { Background = Avalonia.Media.Brushes.DimGray, Child = canvas, Width = 900, Height = 520 };
            setup(canvas);
            host.Measure(new Avalonia.Size(900, 520));
            host.Arrange(new Avalonia.Rect(0, 0, 900, 520));
            using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(1800, 1040), new Avalonia.Vector(192, 192));
            bmp.Render(host);
            bmp.Save(Path.Combine(folder, file));
        }
        Frame("6-push-40.png", c => c.PreviewPush(Overlay.ScreenEdge.Bottom, 450, 0.4));
        Frame("7-push-90.png", c => c.PreviewPush(Overlay.ScreenEdge.Bottom, 450, 0.9));
        Frame("8-arrive.png", c => c.PreviewArrive(Overlay.ScreenEdge.Top, new Avalonia.Point(420, 2), "Screen 1  ·  PL2280H", 200));

        // The welcome setup, one picture per step.
        bool wasVisible = Onboarding.IsVisible;
        for (int step = 0; step < 6; step++)
        {
            Onboarding.PreviewStep(this, step);
            await Task.Delay(450);
            var size = new Avalonia.PixelSize((int)Bounds.Width * 2, (int)Bounds.Height * 2);
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Avalonia.Vector(192, 192));
            bitmap.Render(this);
            bitmap.Save(Path.Combine(folder, $"9-welcome-{step}.png"));
        }
        Onboarding.IsVisible = wasVisible;

        File.WriteAllText(Path.Combine(folder, "done"), DateTime.Now.ToString("O"));
    }

    private async Task SelfTestIfRequestedAsync()
    {
        var request = Path.Combine(DriverService.AppDataDirectory, "selftest.request");
        if (!File.Exists(request)) return;
        File.Delete(request);
        var result = Path.Combine(DriverService.AppDataDirectory, "selftest.result");
        try
        {
            ShowPage(ButtonsTab);
            await ButtonsPageView.SelfTestPenScrollAsync();
            File.WriteAllText(result, "ok");
        }
        catch (Exception ex)
        {
            File.WriteAllText(result, ex.ToString());
        }
    }

    private async Task WatchAsync()
    {
        var welcome = Path.Combine(DriverService.AppDataDirectory, "welcome.request");
        if (File.Exists(welcome)) { File.Delete(welcome); Show(); Activate(); ShowOnboarding(); }
        try { await SelfTestIfRequestedAsync(); } catch (Exception ex) { ErrorLog.Write(ex, "selftest"); }
        try { await SnapshotIfRequestedAsync(); } catch (Exception ex) { Console.Error.WriteLine("Snapshot failed: " + ex.Message); }
        XpPenWarning.IsVisible = Session != null && XpPenDriver.IsRunning;
        try { await CheckScreensAsync(); } catch (Exception ex) { Console.Error.WriteLine("Screen check failed: " + ex.Message); }

        if (_permissionsShown)
        {
            UpdatePermissionRows();
            if (MacPermissions.HasAll)
            {
                _permissionsShown = false;
                await StartAsync();
            }
            return;
        }

        await ApplyAppRuleAsync();
    }

    // ---- Per-app buttons -----------------------------------------------------------------------

    private async Task ApplyAppRuleAsync()
    {
        if (Session is not { } s || !_driver.IsConnected)
            return;
        if (Config.AppRules.Count == 0 && !Config.PenScroll && _appKey == "")
            return; // nothing per-app to do, and the normal settings are already on

        var front = Apps.Frontmost();
        if (front is null)
            return;
        // InkDot itself counts as a drawing app: its test pads need the pen to draw, not scroll.

        var rule = Config.AppRules.FirstOrDefault(r => string.Equals(r.App, front, StringComparison.OrdinalIgnoreCase));
        bool scroll = PenScrolling.IsOnFor(Config, front);

        // Your saved settings already have the main pen-scrolling switch; only differences need a copy.
        bool needsCopy = rule != null || scroll != Config.PenScroll;
        string key = needsCopy ? $"{front}|{scroll}" : "";
        if (key == _appKey)
            return;

        try
        {
            if (!needsCopy)
            {
                await _driver.Daemon.SetSettings(s.Settings); // back to your normal setup
            }
            else
            {
                // Apply a copy so per-app changes never overwrite your saved settings.
                var copy = JsonConvert.DeserializeObject<Settings>(JsonConvert.SerializeObject(s.Settings))!;
                var profile = copy.Profiles.GetProfile(s.Tablet);
                if (rule != null)
                {
                    var buttons = profile.BindingSettings.PenButtons;
                    buttons.SetExpectedCount(s.PenButtonCount);
                    for (int i = 0; i < s.PenButtonCount; i++)
                    {
                        var action = ButtonActions.ByLabel(i < rule.Buttons.Count ? rule.Buttons[i] : null);
                        if (action != null)
                            buttons[i] = action.Create(i)!;
                    }
                }
                PenScrolling.Apply(profile, scroll);
                await _driver.Daemon.SetSettings(copy);
            }
            _appKey = key;
            _appRuleActive = needsCopy ? front : null;
            UpdateSidebar();
        }
        catch
        {
            // Try again on the next tick.
        }
    }

    // ---- IShell --------------------------------------------------------------------------------

    public async Task ApplyAsync(string? doneMessage = "Saved.")
    {
        if (Session is null)
            return;
        try
        {
            await Session.ApplyAsync();
            _appRuleActive = null; _appKey = null; // re-check per-app settings on the next tick
            AreaPageView.Load(this); // e.g. shows or hides the mouse mode note
            UpdateSidebar();
            SessionChanged?.Invoke(this, EventArgs.Empty);
            RememberAreaScreen();
            if (doneMessage != null)
                Celebrate(doneMessage);
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't save. {ex.Message}");
        }
    }

    public Task NotifyAsync(string message)
    {
        ShowToast(message);
        return Task.CompletedTask;
    }

    public Task UseQuickPresetAsync(QuickPreset preset) => UseQuickPresetAsync(preset, announce: true);

    public async Task UseQuickPresetAsync(QuickPreset preset, bool announce)
    {
        if (Session is null) return;
        preset.Apply(Session);
        Config.ActivePreset = preset.Name;
        Config.Save();
        await ApplyAsync(announce ? $"{preset.Name} preset on." : null);
        ReloadPages();
    }

    public async Task UseSavedPresetAsync(string name)
    {
        if (Presets.Load(name) is not { } settings || !_driver.IsConnected)
        {
            ShowToast("Couldn't open that preset.");
            return;
        }
        try
        {
            await _driver.Daemon.SetSettings(settings);
            settings.Serialize(new FileInfo(AppInfo.Current.SettingsFile));
            Config.ActivePreset = name;
            Config.Save();
            await LoadTabletAsync();
            Celebrate($"{name} preset on.");
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't use that preset. {ex.Message}");
        }
    }

    public async Task RedetectAsync()
    {
        if (!_driver.IsConnected)
        {
            await StartAsync();
            return;
        }

        ShowState(DotExpression.Focused, "Looking for your tablet.", "One moment.");
        var tablets = (await _driver.Daemon.DetectTablets()).ToList();
        await _driver.Daemon.SetSettings(await _driver.Daemon.GetSettings()); // rebuild the pen pipeline
        await LoadTabletAsync(tablets);
    }

    public async Task RestartDriverAsync()
    {
        _restarting = true;
        ShowState(DotExpression.Focused, "Restarting the driver.", "One moment.");
        _driver.StopDaemon();
        await Task.Delay(800);
        _restarting = false;
        await StartAsync();
    }

    // ---- Menu bar helpers ----------------------------------------------------------------------

    public IReadOnlyList<string> ScreenNames =>
        Session is { } s
            ? s.Displays.Select((d, i) => $"{i + 1}  ·  {s.ScreenName(i)}  ·  {d.Width:0} × {d.Height:0}").ToList()
            : [];

    public int CurrentScreen => Session?.CurrentScreenIndex() ?? ScreenMap.Custom;

    /// <summary>Draw on one screen (index) or on all of them (<see cref="ScreenMap.AllScreens"/>).</summary>
    public async Task SwitchScreenAsync(int index, bool announce = true)
    {
        if (Session is not { } s)
            return;

        var target = index == ScreenMap.AllScreens
            ? ScreenMath.AreaForAllScreens(s.Displays)
            : ScreenMath.AreaForScreen(s.Displays[index]);
        s.Absolute.Display.Area = target;

        if (s.Absolute.LockAspectRatio)
        {
            var current = s.Absolute.Tablet.Area;
            var largest = ScreenMath.FitAspect(s.FullTablet, current.Width / current.Height);
            var area = ScreenMath.FitAspect(s.FullTablet, target.Width / target.Height, current.Width / largest.Width);
            area.Rotation = current.Rotation;
            s.Absolute.Tablet.Area = area;
        }

        await ApplyAsync(!announce ? null
            : index == ScreenMap.AllScreens ? "Drawing on all screens." : $"Drawing on {s.ScreenName(index)}.");
        AreaPageView.Load(this);
    }

    public void ShowFromMenu()
    {
        Show();
        Activate();
        _ = SetDebugAsync(WantsPenData);
    }

    public void RefreshLoginToggle() => AboutPageView.RefreshLogin();

    /// <summary>The welcome setup. Runs on first launch; Help → Show Welcome Again replays it.</summary>
    public void ShowOnboarding(int step = OnboardingView.Welcome)
    {
        if (!IsVisible) ShowFromMenu();
        Onboarding.Finished -= OnOnboardingFinished;
        Onboarding.Finished += OnOnboardingFinished;
        Onboarding.Start(this, step);
    }

    private void OnOnboardingFinished()
    {
        if (_permissionsShown && !MacPermissions.HasAll)
        {
            // Skipped before allowing: keep asking, since nothing works without them.
            ShowPermissions();
            return;
        }
        Config.OnboardingDone = true;
        Config.Save();
        ReloadPages();
        ShowPage(AreaTab);
        Celebrate("Welcome to InkDot.");
    }

    /// <summary>Live pen data while the welcome setup needs it.</summary>
    public Task SetOnboardingDebugAsync(bool on)
    {
        _onboardingWantsPen = on;
        return SetDebugAsync(WantsPenData);
    }

    /// <summary>Live pen reports are on while the Pressure page or a pen step of the welcome setup shows.
    /// Asked again once the driver connects, since the setup can reach the pen step before that.</summary>
    private bool WantsPenData => IsVisible && (_page == PressureTab && !Onboarding.IsVisible || _onboardingWantsPen && Onboarding.IsVisible);
    private bool _onboardingWantsPen;

    /// <summary>For the menu bar: open the window on a page (0 Area, 1 Pressure, 2 Buttons, 3 Presets, 4 About).</summary>
    public void OpenPage(int index)
    {
        ShowFromMenu();
        ShowPage(index);
    }

    // ---- Views ---------------------------------------------------------------------------------

    private void ShowPage(int index)
    {
        _page = index;
        AreaPageView.IsVisible = index == AreaTab;
        PressurePageView.IsVisible = index == PressureTab;
        ButtonsPageView.IsVisible = index == ButtonsTab;
        PresetsPageView.IsVisible = index == PresetsTab;
        AboutPageView.IsVisible = index == AboutTab;
        NavArea.Classes.Set("active", index == AreaTab);
        NavPressure.Classes.Set("active", index == PressureTab);
        NavButtons.Classes.Set("active", index == ButtonsTab);
        NavPresets.Classes.Set("active", index == PresetsTab);
        NavAbout.Classes.Set("active", index == AboutTab);
        Scroller.IsVisible = index != PressureTab; // the Pressure page has no scrolling, so drawing can't move it
        Scroller.Offset = default;
        _ = SetDebugAsync(WantsPenData);
    }

    /// <summary>Live pen reports are only streamed while the Pressure page is open.</summary>
    private async Task SetDebugAsync(bool on)
    {
        if (on == _debugOn || !_driver.IsConnected)
            return;
        _debugOn = on;
        try { await _driver.Daemon.SetTabletDebug(on); } catch { _debugOn = false; }
    }

    private void UpdateSidebar()
    {
        if (Session is not { } s)
        {
            SideTablet.Text = "No tablet";
            SideStatus.Text = _driver.IsConnected ? "Driver running" : "Driver stopped";
            StatusLight.Fill = BrandBrush("Lv.Muted");
            return;
        }

        StatusLight.Fill = BrandBrush("Lv.Ember");
        SideTablet.Text = s.TabletName;
        int screen = s.CurrentScreenIndex();
        var where = screen switch
        {
            ScreenMap.AllScreens => s.Displays.Count > 1 ? "all screens" : null,
            ScreenMap.Custom => "custom area",
            _ => s.Displays.Count > 1 ? $"screen {screen + 1}" : null,
        };
        SideStatus.Text = _appRuleActive != null
            ? $"Settings for {_appRuleActive}"
            : s.MouseMode ? "Ready · mouse mode"
            : where is null ? "Ready" : $"Ready · {where}";
    }

    private Avalonia.Media.IBrush? BrandBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as Avalonia.Media.IBrush : null;

    private void ShowState(DotExpression mood, string title, string body,
        string? detail = null, (string Label, Func<Task> Action)? primary = null, (string Label, Func<Task> Action)? secondary = null)
    {
        _permissionsShown = false;
        StateView.IsVisible = true;
        StateDot.Expression = mood;
        StateTitle.Text = title;
        StateBody.Text = body;
        PermissionList.IsVisible = false;

        StateDetail.IsVisible = detail != null;
        StateDetail.Text = detail;

        StatePrimary.IsVisible = primary != null;
        StatePrimary.Content = primary?.Label;
        _primaryAction = primary?.Action;

        StateSecondary.IsVisible = secondary != null;
        StateSecondary.Content = secondary?.Label;
        _secondaryAction = secondary?.Action;
    }

    private void HideState()
    {
        _permissionsShown = false;
        StateView.IsVisible = false;
        Logo.Dot.Expression = DotExpression.Look;
    }

    private void ShowPermissions()
    {
        ShowState(DotExpression.Look, "Two quick permissions.",
            "macOS asks before any app reads your pen or moves your cursor. Allow both for Lveno.",
            detail: "Already ticked in System Settings? Remove Lveno with −, then add it again.",
            primary: ("Continue", StartAsync),
            secondary: ("Open System Settings", () =>
            {
                MacPermissions.OpenSettings(MacPermissions.HasInputMonitoring ? "Privacy_Accessibility" : "Privacy_ListenEvent");
                return Task.CompletedTask;
            }));
        PermissionList.IsVisible = true;
        _permissionsShown = true;
        UpdatePermissionRows();
    }

    private void UpdatePermissionRows()
    {
        bool input = MacPermissions.HasInputMonitoring, access = MacPermissions.HasAccessibility;
        InputMonitoringOk.IsVisible = input;
        InputMonitoringButton.IsVisible = !input;
        AccessibilityOk.IsVisible = access;
        AccessibilityButton.IsVisible = !access;
    }

    private void Celebrate(string message)
    {
        Logo.Dot.Expression = DotExpression.Happy;
        ShowToast(message, () => Logo.Dot.Expression = DotExpression.Look);
    }

    private async void ShowToast(string message, Action? after = null)
    {
        _toastCts?.Cancel();
        var cts = _toastCts = new CancellationTokenSource();
        ToastText.Text = message;
        Toast.Opacity = 1;
        try
        {
            await Task.Delay(2200, cts.Token);
            Toast.Opacity = 0;
            after?.Invoke();
        }
        catch (TaskCanceledException)
        {
            // A newer message replaced this one.
        }
    }
}
