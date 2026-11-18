using Avalonia;
using Avalonia.Controls;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Lveno.App.Services;
using OpenTabletDriver.Desktop.Reflection;

namespace Lveno.App.Pages;

/// <summary>
/// Pen buttons, for all apps or for one app. Tabs across the top pick which; a drawing of the pen
/// shows which button each row sets.
/// </summary>
public partial class ButtonsPage : UserControl
{
    private IShell? _shell;

    // Working copies; written back on Apply.
    private List<PluginSettingStore?> _all = [];
    private List<AppRule> _rules = [];
    private AppRule? _selected; // null = all apps
    private bool _penScroll;    // the main pen-scrolling switch
    private bool _rendering;

    private readonly List<ComboBox> _pickers = [];

    public ButtonsPage()
    {
        InitializeComponent();
        ScrollToggle.IsCheckedChanged += (_, _) =>
        {
            if (_rendering) return;
            bool on = ScrollToggle.IsChecked == true;
            if (_selected is null) _penScroll = on;
            else _selected.PenScroll = on;
            Render();
        };
        RevertButton.Click += (_, _) => Load(_shell!);
        ApplyButton.Click += OnApply;
        RemoveAppButton.Click += (_, _) =>
        {
            if (_selected is null) return;
            _rules.Remove(_selected);
            _selected = null;
            Render();
        };
    }

    private int ButtonCount => _shell?.Session?.PenButtonCount ?? 0;

    public void Load(IShell shell)
    {
        _shell = shell;
        if (shell.Session is not { } s) return;

        var stored = s.Profile.BindingSettings.PenButtons;
        _all = Enumerable.Range(0, ButtonCount).Select(i => i < stored.Count ? stored[i] : null).ToList();
        _rules = shell.Config.AppRules
            .Select(r => new AppRule { App = r.App, BundlePath = r.BundlePath ?? MacApps.FindBundle(r.App), Buttons = r.Buttons.ToList(), PenScroll = r.PenScroll })
            .ToList();
        _penScroll = shell.Config.PenScroll;
        if (_selected != null)
            _selected = _rules.FirstOrDefault(r => r.App == _selected.App);
        Render();
    }

    // ---- Rendering ----------------------------------------------------------------------------

    private void Render()
    {
        RenderTabs();

        Pen.ButtonCount = ButtonCount;
        Pen.Highlight = -1;
        NoButtons.IsVisible = ButtonCount == 0;

        if (_selected is null)
        {
            ScopeTitle.Text = "All apps";
            ScopeHint.Text = _rules.Count == 0
                ? "Used everywhere. Add an app above to give it its own buttons."
                : "Used everywhere except the apps with their own tab.";
            ScopeIconBox.IsVisible = false;
            RemoveAppButton.IsVisible = false;
        }
        else
        {
            ScopeTitle.Text = _selected.App;
            ScopeHint.Text = $"Only while {_selected.App} is the app in front.";
            var icon = MacApps.Icon(_selected.BundlePath);
            ScopeIcon.Source = icon;
            ScopeIconBox.IsVisible = icon != null;
            RemoveAppButton.Content = $"Remove {_selected.App}";
            RemoveAppButton.IsVisible = true;
        }

        RenderPenScroll();
        RenderRows();
    }

    private void RenderPenScroll()
    {
        _rendering = true;
        if (_selected is null)
        {
            ScrollTitle.Text = "Pen scrolling";
            ScrollToggle.IsEnabled = true;
            ScrollToggle.IsChecked = _penScroll;
            ScrollHint.Text = "Drag to scroll and flick to glide. Tap to click. Hold still, then drag, to select. Off by itself in drawing apps.";
        }
        else
        {
            bool drawing = PenScrolling.IsDrawingApp(_selected.App);
            bool on = _selected.PenScroll ?? !drawing;
            ScrollTitle.Text = $"Pen scrolling in {_selected.App}";
            ScrollToggle.IsEnabled = _penScroll;
            ScrollToggle.IsChecked = _penScroll && on;
            ScrollHint.Text = !_penScroll ? "Turn pen scrolling on in All apps first."
                : _selected.PenScroll is null && drawing ? "Off here by default, so the pen draws."
                : on ? "On here. Turn it off if you want to draw by dragging."
                : "Off here. The pen drags and draws as normal.";
        }
        _rendering = false;
    }

    private void RenderTabs()
    {
        AppTabs.Children.Clear();
        AppTabs.Children.Add(Tab("All apps", null, null, _selected is null, () => Select(null)));
        foreach (var rule in _rules)
            AppTabs.Children.Add(Tab(rule.App, rule.BundlePath, null, _selected == rule, () => Select(rule)));

        var add = new Button { Classes = { "apptab", "add" }, Margin = new Thickness(0, 0, 8, 8) };
        add.Content = new Path { Classes = { "icon" }, Data = (Avalonia.Media.Geometry)this.FindResource("Icon.Plus")!, Width = 16, Height = 16 };
        ToolTip.SetTip(add, "Give an app its own buttons");
        add.Flyout = AddAppMenu();
        AppTabs.Children.Add(add);
    }

    private Button Tab(string name, string? bundlePath, Bitmap? _, bool active, Action onClick)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (bundlePath is null && name == "All apps")
        {
            content.Children.Add(new Path { Classes = { "icon" }, Data = (Avalonia.Media.Geometry)this.FindResource("Icon.AllApps")!, Width = 16, Height = 16, Margin = new Thickness(4, 0, 0, 0) });
        }
        else if (MacApps.Icon(bundlePath) is { } icon)
        {
            content.Children.Add(new Image { Source = icon, Width = 22, Height = 22 });
        }
        content.Children.Add(new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });

        var tab = new Button { Content = content, Classes = { "apptab" }, Margin = new Thickness(0, 0, 8, 8) };
        tab.Classes.Set("active", active);
        tab.Click += (_, _) => onClick();
        return tab;
    }

    private MenuFlyout AddAppMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            var apps = MacApps.RunningApps().Where(a => _rules.All(r => !r.App.Equals(a.Name, StringComparison.OrdinalIgnoreCase))).ToList();
            if (apps.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "Open the app first, then add it here.", IsEnabled = false });
                return;
            }
            foreach (var app in apps)
            {
                var item = new MenuItem { Header = app.Name };
                if (MacApps.Icon(app.BundlePath) is { } icon)
                    item.Icon = new Image { Source = icon, Width = 18, Height = 18 };
                item.Click += (_, _) =>
                {
                    Commit();
                    // A new app starts with the buttons you use everywhere.
                    var rule = new AppRule
                    {
                        App = app.Name,
                        BundlePath = app.BundlePath,
                        Buttons = _all.Select(store => ButtonActions.All.FirstOrDefault(a => a.Matches(store))?.Label ?? "Pen default").ToList(),
                    };
                    _rules.Add(rule);
                    _selected = rule;
                    Render();
                };
                menu.Items.Add(item);
            }
        };
        return menu;
    }

    /// <summary>One row per button, upper first, as they sit on the pen.</summary>
    private void RenderRows()
    {
        Rows.Children.Clear();
        _pickers.Clear();
        for (int i = 0; i < ButtonCount; i++) _pickers.Add(null!);

        for (int i = ButtonCount - 1; i >= 0; i--)
        {
            int index = i;
            List<object> items = ButtonActions.All.Cast<object>().ToList();
            object? selected;
            if (_selected is null)
            {
                var store = _all[i];
                selected = ButtonActions.All.FirstOrDefault(a => a.Matches(store));
                if (selected is null)
                {
                    selected = $"Custom: {store?.Name ?? store?.Path ?? "unknown"}";
                    items.Add(selected);
                }
            }
            else
            {
                var label = i < _selected.Buttons.Count ? _selected.Buttons[i] : "Pen default";
                selected = ButtonActions.ByLabel(label) ?? ButtonActions.All[0];
            }

            var picker = new ComboBox { ItemsSource = items, SelectedItem = selected, HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 0 };
            _pickers[i] = picker;

            var label2 = new StackPanel { Spacing = 2 };
            label2.Children.Add(new TextBlock { Text = i == 0 ? "Lower button" : i == 1 ? "Upper button" : $"Button {i + 1}", Classes = { "h2" } });
            label2.Children.Add(new TextBlock { Text = i == 0 ? "The one nearer the tip." : "The one further up the pen.", Classes = { "body", "muted" }, FontSize = 13 });

            var row = new StackPanel { Spacing = 8, Background = Avalonia.Media.Brushes.Transparent };
            row.Children.Add(label2);
            row.Children.Add(picker);

            // Light up that button on the drawing while you're on its row.
            row.PointerEntered += (_, _) => Pen.Highlight = index;
            row.PointerExited += (_, _) => { if (!picker.IsDropDownOpen) Pen.Highlight = -1; };
            picker.DropDownOpened += (_, _) => Pen.Highlight = index;
            picker.DropDownClosed += (_, _) => Pen.Highlight = -1;

            Rows.Children.Add(row);
        }
    }

    // ---- State ----------------------------------------------------------------------------------

    private void Select(AppRule? rule)
    {
        Commit();
        _selected = rule;
        Render();
    }

    /// <summary>Copy what the pickers show back into the working copies.</summary>
    private void Commit()
    {
        for (int i = 0; i < _pickers.Count; i++)
        {
            if (_pickers[i] is not { } picker) continue;
            if (_selected is null)
            {
                if (picker.SelectedItem is ButtonAction action)
                    _all[i] = action.Create(i);
            }
            else
            {
                while (_selected.Buttons.Count <= i) _selected.Buttons.Add("Pen default");
                _selected.Buttons[i] = (picker.SelectedItem as ButtonAction)?.Label ?? "Pen default";
            }
        }
    }

    private async void OnApply(object? sender, RoutedEventArgs e) => await ApplyCoreAsync();

    /// <summary>Dev self-test: flip pen scrolling, apply, flip back, apply. Errors surface in the log.</summary>
    public async Task SelfTestPenScrollAsync()
    {
        foreach (var app in new AppRule?[] { null }.Concat(_rules).ToList())
        {
            Select(app);
            bool original = ScrollToggle.IsChecked == true;
            ScrollToggle.IsChecked = !original;
            await ApplyCoreAsync();
            ScrollToggle.IsChecked = original;
            await ApplyCoreAsync();
        }
        Select(null);
    }

    private async Task ApplyCoreAsync()
    {
        if (_shell?.Session is not { } s) return;
        Commit();

        var buttons = s.Profile.BindingSettings.PenButtons;
        buttons.SetExpectedCount(_all.Count);
        for (int i = 0; i < _all.Count; i++)
            buttons[i] = _all[i]!;

        _shell.Config.AppRules = _rules
            .Select(r => new AppRule { App = r.App, BundlePath = r.BundlePath, Buttons = r.Buttons.ToList(), PenScroll = r.PenScroll })
            .ToList();
        _shell.Config.PenScroll = _penScroll;
        _shell.Config.Save();

        // Saved settings carry the main switch; the per-app watcher handles drawing apps and overrides.
        PenScrolling.Apply(s.Profile, _penScroll);

        ApplyButton.IsEnabled = false;
        try
        {
            await _shell.ApplyAsync("Saved. Try the buttons.");
        }
        finally
        {
            ApplyButton.IsEnabled = true;
        }
    }
}
