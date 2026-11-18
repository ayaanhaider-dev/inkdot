using Avalonia.Controls;
using Avalonia.Interactivity;
using Lveno.App.Controls;
using Lveno.App.Services;
using Lveno.Plugins;
using OpenTabletDriver.Desktop.Reflection;

namespace Lveno.App.Pages;

public partial class PressurePage : UserControl
{
    private IShell? _shell;
    private float _raw = -1; // latest raw pressure from the driver, 0–1, or −1 when the pen is away
    private readonly List<(Button Button, PressureCurve Curve)> _presets = [];

    // Calibration: learn your lightest and hardest press, then fit the curve to that range.
    private enum CalStage { Off, Light, Hard }
    private const int LightStrokes = 3, HardStrokes = 2;
    private CalStage _cal = CalStage.Off;
    private readonly List<float> _peaks = [];
    private float _lightest, _strokePeak;
    private bool _inStroke;

    public PressurePage()
    {
        InitializeComponent();

        string[] names = ["Softest", "Softer", "Soft", "Normal", "Firm", "Firmer", "Firmest"];
        for (int i = 0; i < PressureCurve.Presets.Length; i++)
        {
            var preset = PressureCurve.Presets[i];
            var glyph = new CurveGlyph { Curve = preset, Width = 24, Height = 24 };
            glyph.Bind(CurveGlyph.StrokeProperty, glyph.GetResourceObservable("Lv.Fg"));
            var button = new Button { Content = glyph, Classes = { "preset" } };
            ToolTip.SetTip(button, names[i]);
            button.Click += (_, _) => { Curve.Curve = preset; Update(); };
            _presets.Add((button, preset));
            Presets.Children.Add(button);
        }

        Curve.CurveChanged += (_, _) => Update();
        PressureOn.IsCheckedChanged += (_, _) => Update();
        MouseModeOn.IsCheckedChanged += (_, _) => Update();
        MouseSpeed.ValueChanged += (_, _) => Update();
        ClearButton.Click += (_, _) => Pad.Clear();
        CalibrateButton.Click += (_, _) =>
        {
            if (_cal == CalStage.Off) StartCalibration(); else StopCalibration();
        };
        RevertButton.Click += (_, _) => Load(_shell!);
        ApplyButton.Click += OnApply;
        Pad.PressureSource = () => _raw <= 0 ? -1 : Output(_raw);
    }

    private float MaxPressure => _shell?.Session?.Tablet.Properties.Specifications.Pen?.MaxPressure ?? 8191;

    /// <summary>What apps receive for a press, with the current (unsaved) settings.</summary>
    private float Output(float raw) => PressureOn.IsChecked == true ? Curve.Curve.Apply(raw) : raw > 0 ? 1 : 0;

    public void Load(IShell shell)
    {
        _shell = shell;
        if (shell.Session is not { } s)
            return;

        var store = FindStore(s);
        Curve.Curve = (store?.Enable == true ? PressureCurve.Parse(ButtonActions.Value(store, "Curve")) : null)
                      ?? PressureCurve.Linear;
        _loading = true;
        PressureOn.IsChecked = !s.Profile.BindingSettings.DisablePressure;
        MouseModeOn.IsChecked = s.MouseMode;
        MouseSpeed.Value = Math.Clamp(s.Relative.XSensitivity, 2, 40);
        _loading = false;
        Update();
    }

    private bool _loading;
    private CancellationTokenSource? _modeCts;

    /// <summary>
    /// The Pressure and Mouse mode switches (and mouse speed) take effect right away, no Apply needed.
    /// The curve still waits for Apply, so you can try it on the pad first.
    /// </summary>
    private void ApplySwitchesIfChanged()
    {
        if (_loading || _shell?.Session is not { } s)
            return;

        bool mouse = MouseModeOn.IsChecked == true, pressure = PressureOn.IsChecked == true;
        float speed = (float)MouseSpeed.Value;
        bool mouseChanged = s.MouseMode != mouse;
        bool pressureChanged = s.Profile.BindingSettings.DisablePressure == pressure;
        bool speedChanged = Math.Abs(s.Relative.XSensitivity - speed) > 0.01f;
        if (!mouseChanged && !pressureChanged && !speedChanged)
            return;

        if (mouseChanged) s.MouseMode = mouse;
        s.Profile.BindingSettings.DisablePressure = !pressure;
        s.Relative.XSensitivity = s.Relative.YSensitivity = speed;

        string? message = mouseChanged ? (mouse ? "Mouse mode on." : "Mouse mode off. Back to drawing.")
                        : pressureChanged ? (pressure ? "Pressure on." : "Pressure off.")
                        : null; // speed: stay quiet while dragging

        // Wait a moment so dragging the speed slider sends one update, not dozens.
        _modeCts?.Cancel();
        var cts = _modeCts = new CancellationTokenSource();
        _ = Task.Delay(speedChanged && !mouseChanged && !pressureChanged ? 300 : 0, cts.Token)
            .ContinueWith(async t =>
            {
                if (!t.IsCanceled) await _shell.ApplyAsync(message);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>Called with each pen report while this page is open. Raw pressure 0–1, or −1 when the pen is away.</summary>
    public void OnRawPressure(float raw)
    {
        _raw = raw;
        float output = raw > 0 ? Output(raw) : 0;
        Curve.SetLive(raw > 0 ? raw : -1);
        Gauge.Set(output * MaxPressure, MaxPressure);
        LiveState.Text = raw < 0 ? "PEN AWAY" : output > 0 ? $"PRESSING · {output * 100:0}%" : raw > 0 ? "BELOW THRESHOLD" : "HOVERING";

        if (_cal != CalStage.Off)
            TrackCalibration(raw);
    }

    // ---- Calibration ---------------------------------------------------------------------------

    private void StartCalibration()
    {
        _cal = CalStage.Light;
        _peaks.Clear();
        _inStroke = false;
        Pad.Clear();
        CalibrateButton.Content = "Cancel";
        CalOverlay.IsVisible = true;
        ShowCalStep();
    }

    private void StopCalibration()
    {
        _cal = CalStage.Off;
        CalibrateButton.Content = "Calibrate";
        CalOverlay.IsVisible = false;
    }

    private void ShowCalStep()
    {
        if (_cal == CalStage.Light)
        {
            CalStep.Text = $"STEP 1 OF 2 · {_peaks.Count}/{LightStrokes}";
            CalTitle.Text = "Draw a few lines as lightly as you can.";
            CalHint.Text = "Just enough to touch. Lift the pen between lines.";
        }
        else
        {
            CalStep.Text = $"STEP 2 OF 2 · {_peaks.Count}/{HardStrokes}";
            CalTitle.Text = "Now press as hard as you'd ever draw.";
            CalHint.Text = "Firm, but comfortable. Lift between lines.";
        }
    }

    /// <summary>Each stroke runs from touch-down to lift; its peak pressure is what we learn from.</summary>
    private void TrackCalibration(float raw)
    {
        if (raw > 0)
        {
            if (!_inStroke) { _inStroke = true; _strokePeak = 0; }
            _strokePeak = Math.Max(_strokePeak, raw);
            return;
        }
        if (!_inStroke) return;

        _inStroke = false;
        if (_strokePeak < 0.005f) return; // a bump, not a stroke
        _peaks.Add(_strokePeak);

        if (_cal == CalStage.Light && _peaks.Count >= LightStrokes)
        {
            _lightest = _peaks.Min();
            _peaks.Clear();
            _cal = CalStage.Hard;
        }
        else if (_cal == CalStage.Hard && _peaks.Count >= HardStrokes)
        {
            FinishCalibration(_lightest, _peaks.Max());
            return;
        }
        ShowCalStep();
    }

    private async void FinishCalibration(float lightest, float hardest)
    {
        StopCalibration();

        // Start just under your lightest touch, so it still draws but stray contact doesn't.
        // End at your hard press, so you reach a full line without straining. Keep the curve's bend.
        var old = Curve.Curve;
        float start = Math.Clamp(lightest * 0.5f, 0, 0.3f);
        float end = Math.Clamp(hardest * 0.95f, start + 0.2f, 1);
        float t = (old.ControlX - old.StartX) / Math.Max(0.01f, old.EndX - old.StartX);
        Curve.Curve = new PressureCurve(start, 0, start + t * (end - start), old.ControlY, end, 1);
        Update();

        if (_shell != null)
            await _shell.NotifyAsync($"Calibrated: {lightest * 100:0}% to {hardest * 100:0}%. Click Apply to keep it.");
    }

    private void Update()
    {
        foreach (var (button, preset) in _presets)
            button.Classes.Set("active", Curve.Curve.IsClose(preset));
        ApplySwitchesIfChanged();
        MouseSpeedPanel.IsVisible = MouseModeOn.IsChecked == true;
        MouseSpeedValue.Text = $"{MouseSpeed.Value:0}×";
        Curve.Opacity = PressureOn.IsChecked == true ? 1 : 0.4;
        Curve.IsHitTestVisible = PressureOn.IsChecked == true;
        if (_raw >= 0) OnRawPressure(_raw);
    }

    private static PluginSettingStore? FindStore(TabletSession s) =>
        s.Profile.Filters.FirstOrDefault(f => f?.Path == typeof(PressureCurveFilter).FullName);

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_shell?.Session is not { } s)
            return;

        var old = FindStore(s);
        if (old != null)
            s.Profile.Filters.Remove(old);
        var store = ButtonActions.Store(typeof(PressureCurveFilter), ("Curve", Curve.Curve.ToString()));
        store.Enable = true;
        s.Profile.Filters.Add(store);
        s.Profile.BindingSettings.DisablePressure = PressureOn.IsChecked != true;
        if (s.MouseMode != (MouseModeOn.IsChecked == true))
            s.MouseMode = MouseModeOn.IsChecked == true;
        s.Relative.XSensitivity = s.Relative.YSensitivity = (float)MouseSpeed.Value;

        ApplyButton.IsEnabled = false;
        try
        {
            await _shell.ApplyAsync("Saved. Feel the difference.");
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }
}
