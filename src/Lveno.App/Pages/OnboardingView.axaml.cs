using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Lveno.App.Controls;
using Lveno.App.Services;
using Lveno.Plugins;

namespace Lveno.App.Pages;

/// <summary>
/// First-run setup, led by the Lveno dot: welcome, permissions, say hi with the pen, pick a screen,
/// pick a feel, done. Each step reacts live to what you do.
/// </summary>
public partial class OnboardingView : UserControl
{
    public const int Welcome = 0, Permissions = 1, Pen = 2, Screen = 3, Feel = 4, Done = 5;
    private static readonly string[] NextLabels = ["Let's go", "Continue", "Continue", "Continue", "Continue", "Start drawing"];

    private MainWindow? _window;
    private int _step;
    private readonly StackPanel[] _steps;
    private readonly DispatcherTimer _tick;
    private bool _hovered, _tapped, _pressedHard, _penCelebrated;
    private float _lastPressure = -1;
    private int _feel = 1; // 0 soft, 1 normal, 2 firm
    private readonly List<Button> _feelButtons = [];

    /// <summary>Raised when you finish or skip.</summary>
    public event Action? Finished;

    public OnboardingView()
    {
        InitializeComponent();
        _steps = [StepWelcome, StepPermissions, StepPen, StepScreen, StepFeel, StepDone];
        for (int i = 0; i < _steps.Length; i++)
            Progress.Children.Add(new Border { Classes = { "progress" } });

        NextButton.Click += async (_, _) => await NextAsync();
        BackButton.Click += (_, _) => Go(Math.Max(0, _step - 1));
        SkipButton.Click += (_, _) => Finish();
        InputAllow.Click += (_, _) => MacPermissions.RequestInputMonitoring();
        AccessAllow.Click += (_, _) => MacPermissions.RequestAccessibility();

        // The dot's eyes follow your mouse too, on every step.
        PointerMoved += (_, e) =>
        {
            foreach (var dot in new[] { WelcomeDot, PermDot, PenDot, DoneDot })
                if (dot.IsEffectivelyVisible) dot.LookAt(e.GetPosition(dot));
        };

        Screens.SelectionChanged += async (_, index) =>
        {
            if (_window is null) return;
            await _window.SwitchScreenAsync(index, announce: false);
            UpdateScreenText();
        };

        string[] feelNames = ["Soft", "Normal", "Firm"];
        string[] feelHints = ["Light touch, bold lines", "Balanced", "Press to get bold"];
        var curves = new[] { PressureCurve.Presets[1], PressureCurve.Presets[3], PressureCurve.Presets[5] };
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            var glyph = new CurveGlyph { Curve = curves[i], Width = 34, Height = 34 };
            glyph.Bind(CurveGlyph.StrokeProperty, glyph.GetResourceObservable("Lv.Fg"));
            var content = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(glyph);
            content.Children.Add(new TextBlock { Text = feelNames[i], Classes = { "h2" }, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = feelHints[i], Classes = { "body", "muted" }, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button { Content = content, Classes = { "feel" } };
            button.Click += async (_, _) => await PickFeelAsync(index);
            _feelButtons.Add(button);
            FeelChoices.Children.Add(button);
        }
        Pad.PressureSource = () => _lastPressure <= 0 ? -1 : FeelCurve().Apply(_lastPressure);

        EdgePushSwitch.IsCheckedChanged += async (_, _) => await SetExtraAsync(edge: EdgePushSwitch.IsChecked == true, scroll: null);
        ScrollSwitch.IsCheckedChanged += async (_, _) => await SetExtraAsync(edge: null, scroll: ScrollSwitch.IsChecked == true);

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Poll());
    }

    /// <summary>Dev only: show one step, as is, for a screenshot.</summary>
    public void PreviewStep(MainWindow window, int step)
    {
        _window = window;
        IsVisible = true;
        _step = step;
        Go(step, animate: false);
        if (_step != step) { _step = step; for (int i = 0; i < _steps.Length; i++) _steps[i].IsVisible = i == step; }
    }

    public void Start(MainWindow window, int step = Welcome)
    {
        _window = window;
        _hovered = _tapped = _pressedHard = _penCelebrated = false;
        IsVisible = true;
        _tick.Start();
        _step = Welcome;
        Go(step, animate: false);
    }

    // ---- Steps ----------------------------------------------------------------------------------

    private void Go(int step, bool animate = true)
    {
        // Skip steps that have nothing to do.
        if (step == Permissions && MacPermissions.HasAll && _step < step) step = Pen;
        if (step == Screen && (_window?.Session?.Displays.Count ?? 0) <= 1)
            step = _step < step ? Feel : Pen;

        int previous = _step;
        _step = step;
        for (int i = 0; i < _steps.Length; i++)
        {
            var panel = _steps[i];
            if (i != step) { panel.IsVisible = false; continue; }

            panel.IsVisible = true;
            if (animate)
            {
                // Start a little to the side and faded, then settle: slides forward, or back when going back.
                panel.Transitions?.Clear();
                panel.Opacity = 0;
                panel.RenderTransform = TransformOperations.Parse(step >= previous ? "translateX(28px)" : "translateX(-28px)");
                Dispatcher.UIThread.Post(() =>
                {
                    RestoreTransitions(panel);
                    panel.Opacity = 1;
                    panel.RenderTransform = TransformOperations.Parse("translateX(0px)");
                }, DispatcherPriority.Background);
            }
            else
            {
                panel.Opacity = 1;
                panel.RenderTransform = null;
            }
        }

        for (int i = 0; i < Progress.Children.Count; i++)
        {
            var bar = (Border)Progress.Children[i];
            bar.Classes.Set("done", i < step);
            bar.Classes.Set("now", i == step);
        }
        StepCounter.Text = $"STEP {step + 1} OF {_steps.Length}";
        BackButton.IsVisible = step > Welcome && step != Done;
        NextButton.Content = NextLabels[step];

        _ = _window?.SetOnboardingDebugAsync(step is Pen or Feel);
        if (step == Screen) LoadScreens();
        if (step == Feel) HighlightFeel();
        if (step == Done)
        {
            EdgePushSwitch.IsChecked = _window?.Config.EdgePush == true;
            ScrollSwitch.IsChecked = _window?.Config.PenScroll == true;
        }
        Poll();
    }

    private static void RestoreTransitions(StackPanel panel)
    {
        panel.Transitions =
        [
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(280), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
            new Avalonia.Animation.TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(320), Easing = new Avalonia.Animation.Easings.CubicEaseOut() },
        ];
    }

    private async Task NextAsync()
    {
        if (_step == Done) { Finish(); return; }
        if (_step == Feel) await ApplyFeelAsync();
        Go(_step + 1);
    }

    private void Finish()
    {
        _tick.Stop();
        _ = _window?.SetOnboardingDebugAsync(false);
        IsVisible = false;
        Finished?.Invoke();
    }

    /// <summary>Twice a second: live state for the current step.</summary>
    private void Poll()
    {
        switch (_step)
        {
            case Permissions:
            {
                bool input = MacPermissions.HasInputMonitoring, access = MacPermissions.HasAccessibility;
                InputCheck.Classes.Set("on", input);
                AccessCheck.Classes.Set("on", access);
                InputAllow.IsVisible = !input;
                AccessAllow.IsVisible = !access;
                bool all = input && access;
                PermDot.Expression = all ? DotExpression.Happy : DotExpression.Focused;
                NextButton.IsEnabled = all;
                PermHint.IsVisible = !all;
                if (all && _step == Permissions)
                    DispatcherTimer.RunOnce(() => { if (_step == Permissions) Go(Pen); }, TimeSpan.FromMilliseconds(900));
                break;
            }
            case Pen:
            {
                bool connected = _window?.Session != null;
                if (!connected)
                {
                    PenTitle.Text = "Plug in your tablet.";
                    PenBody.Text = "I'll notice it on my own. Already plugged in? Give it a second while the driver starts.";
                    PenDot.Expression = DotExpression.Sleep;
                }
                else if (!_penCelebrated)
                {
                    PenTitle.Text = "Say hi with your pen.";
                    PenBody.Text = "Hover the pen just above the tablet. I'll follow it. Then tap, and press hard once.";
                    if (PenDot.Expression == DotExpression.Sleep) PenDot.Expression = DotExpression.Look;
                }
                NextButton.IsEnabled = true; // never trap anyone here
                break;
            }
            default:
                NextButton.IsEnabled = true;
                break;
        }
    }

    // ---- Pen -----------------------------------------------------------------------------------

    /// <summary>Raw pen data from the driver: pressure 0–1 (or −1 when away), and position 0–1 across the tablet.</summary>
    public void OnPen(float pressure, float x, float y)
    {
        _lastPressure = pressure;
        if (_step != Pen && _step != Feel) return;
        if (_step != Pen || pressure < 0) { PenBar.Width = 0; return; }

        // The dot watches the pen: map tablet position to a point far out in that direction.
        double reach = Math.Max(120, PenDot.Bounds.Width * 3);
        PenDot.LookAt(new Point(PenDot.Bounds.Width / 2 + (x - 0.5) * 2 * reach, PenDot.Bounds.Height / 2 + (y - 0.5) * 2 * reach));
        PenBar.Width = 260 * Math.Clamp(pressure, 0, 1);

        if (!_hovered) { _hovered = true; HoverCheck.Classes.Set("on", true); PenDot.Blink(); }
        if (pressure > 0.02f && !_tapped) { _tapped = true; TapCheck.Classes.Set("on", true); }
        if (pressure > 0.7f && !_pressedHard) { _pressedHard = true; PressCheck.Classes.Set("on", true); }

        PenDot.Expression = pressure > 0.02f ? DotExpression.Focused : DotExpression.Look;
        if (_hovered && _tapped && _pressedHard && !_penCelebrated)
        {
            _penCelebrated = true;
            PenTitle.Text = "Hello to you too.";
            PenBody.Text = "Your pen and tablet are working. Pressure too.";
            PenDot.Expression = DotExpression.Happy;
        }
        else if (_penCelebrated && pressure <= 0.02f)
        {
            PenDot.Expression = DotExpression.Happy;
        }
    }

    // ---- Screen --------------------------------------------------------------------------------

    private void LoadScreens()
    {
        if (_window?.Session is not { } s) return;
        Screens.Displays = s.Displays;
        Screens.Names = s.ScreenNames;
        Screens.Wallpapers = Apps.Wallpapers();
        Screens.CursorIndex = ScreenLayout.CursorScreen(s.Displays);
        Screens.SelectedIndex = s.CurrentScreenIndex();
        UpdateScreenText();
    }

    private void UpdateScreenText()
    {
        if (_window?.Session is not { } s) return;
        int i = s.CurrentScreenIndex();
        Screens.SelectedIndex = i;
        ScreenPicked.Text = i >= 0 ? $"Drawing on {i + 1} · {s.ScreenName(i)}" : i == ScreenMap.AllScreens ? "Drawing on all screens" : "";
    }

    // ---- Feel ----------------------------------------------------------------------------------

    private PressureCurve FeelCurve() => PressureCurve.Presets[_feel switch { 0 => 1, 2 => 5, _ => 3 }];

    private async Task PickFeelAsync(int feel)
    {
        _feel = feel;
        HighlightFeel();
        Pad.Clear();
        await ApplyFeelAsync();
    }

    private void HighlightFeel()
    {
        for (int i = 0; i < _feelButtons.Count; i++)
            _feelButtons[i].Classes.Set("active", i == _feel);
    }

    private async Task ApplyFeelAsync()
    {
        if (_window is null) return;
        var preset = Presets.Quick.First(p => p.Name == (_feel switch { 0 => "Sketching", 2 => "Inking", _ => "Drawing" }));
        await _window.UseQuickPresetAsync(preset, announce: false);
    }

    // ---- Extras --------------------------------------------------------------------------------

    private async Task SetExtraAsync(bool? edge, bool? scroll)
    {
        if (_window?.Session is not { } s) return;
        if (edge is { } e && e != _window.Config.EdgePush)
        {
            _window.Config.EdgePush = e;
            s.SetEdgePush(e);
        }
        else if (scroll is { } sc && sc != _window.Config.PenScroll)
        {
            _window.Config.PenScroll = sc;
            PenScrolling.Apply(s.Profile, sc);
        }
        else return;
        _window.Config.Save();
        DoneDot.Blink();
        await _window.ApplyAsync(null);
    }
}
