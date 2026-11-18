using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using Lveno.App.Controls;
using Lveno.App.Services;

namespace Lveno.App.Pages;

public partial class PresetsPage : UserControl
{
    private IShell? _shell;

    public PresetsPage()
    {
        InitializeComponent();
        SaveButton.Click += async (_, _) => await SaveAsync();
        NameBox.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await SaveAsync(); };
    }

    private async Task SaveAsync()
    {
        if (_shell?.Session is not { } s) return;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            NameBox.Focus();
            await _shell.NotifyAsync("Give it a name first.");
            return;
        }
        try
        {
            Presets.Save(NameBox.Text, Presets.Copy(s.Settings));
            _shell.Config.ActivePreset = NameBox.Text.Trim();
            _shell.Config.Save();
            NameBox.Text = "";
            Load(_shell);
            await _shell.NotifyAsync("Saved.");
        }
        catch (Exception ex)
        {
            await _shell.NotifyAsync(ex.Message);
        }
    }

    public void Load(IShell shell)
    {
        _shell = shell;
        var active = shell.Config.ActivePreset;

        QuickGrid.Children.Clear();
        for (int i = 0; i < Presets.Quick.Length; i++)
        {
            var preset = Presets.Quick[i];
            var tile = QuickTile(preset, active == preset.Name);
            tile.Margin = new Thickness(i % 2 == 0 ? 0 : 6, i < 2 ? 0 : 12, i % 2 == 0 ? 6 : 0, 0);
            tile.Click += async (_, _) => await shell.UseQuickPresetAsync(preset);
            QuickGrid.Children.Add(tile);
        }

        SavedRows.Children.Clear();
        var saved = Presets.Saved();
        NoSaved.IsVisible = saved.Count == 0;
        foreach (var name in saved)
        {
            if (SavedRows.Children.Count > 0) SavedRows.Children.Add(new Border { Classes = { "divider" } });
            SavedRows.Children.Add(SavedRow(shell, name, active == name, () => Load(shell)));
        }
    }

    /// <summary>A quick preset: icon, name, one line of text. Ember outline when it's in use.</summary>
    private static Button QuickTile(QuickPreset preset, bool inUse)
    {
        Control glyph = preset.Curve is { } curve
            ? new CurveGlyph { Curve = curve, Width = 26, Height = 26 }
            : new ShapePath
            {
                // A cursor arrow for mouse mode.
                Data = StreamGeometry.Parse("M0,0 L0,17 L4.6,12.6 L7.6,19.6 L10.4,18.4 L7.4,11.6 L13.6,11.6 Z"),
                Width = 14, Height = 20, Stretch = Stretch.Uniform,
            };
        if (glyph is CurveGlyph g) g.Bind(CurveGlyph.StrokeProperty, g.GetResourceObservable("Lv.Fg"));
        if (glyph is ShapePath p) p.Bind(Shape.FillProperty, p.GetResourceObservable("Lv.Fg"));

        var icon = new Border
        {
            Width = 48, Height = 48, CornerRadius = new CornerRadius(12),
            Child = glyph, VerticalAlignment = VerticalAlignment.Top,
        };
        icon.Bind(Border.BackgroundProperty, icon.GetResourceObservable("Lv.Bg"));
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = preset.Name, Classes = { "h2" }, VerticalAlignment = VerticalAlignment.Center });
        if (inUse)
            title.Children.Add(new TextBlock { Text = "IN USE", Classes = { "label", "accent" }, VerticalAlignment = VerticalAlignment.Center });

        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Text = preset.Description, Classes = { "body", "muted" }, FontSize = 13, LineHeight = 18 });

        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        icon.Margin = new Thickness(0, 0, 14, 0);
        row.Children.Add(icon);
        row.Children.Add(text);

        var tile = new Button { Content = row, Classes = { "presetcard" } };
        tile.Classes.Set("active", inUse);
        return tile;
    }

    private static Grid SavedRow(IShell shell, string name, bool inUse, Action reload)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(20, 12, 16, 12) };

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = name, Classes = { "h2" } });
        if (inUse)
            title.Children.Add(new TextBlock { Text = "IN USE", Classes = { "label", "accent" }, VerticalAlignment = VerticalAlignment.Center });

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Text = $"Saved {Presets.SavedAt(name):d MMM, HH:mm}", Classes = { "mono", "muted" }, FontSize = 11 });
        row.Children.Add(text);

        var delete = new Button { Content = "Delete", Classes = { "link" }, FontSize = 13, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        delete.Click += (_, _) =>
        {
            Presets.Delete(name);
            if (shell.Config.ActivePreset == name) { shell.Config.ActivePreset = null; shell.Config.Save(); }
            reload();
        };
        Grid.SetColumn(delete, 1);
        row.Children.Add(delete);

        var use = new Button { Content = inUse ? "In use" : "Use", Classes = { "ghost" }, IsEnabled = !inUse, VerticalAlignment = VerticalAlignment.Center };
        use.Click += async (_, _) => await shell.UseSavedPresetAsync(name);
        Grid.SetColumn(use, 2);
        row.Children.Add(use);

        return row;
    }
}
