using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TextBlock = System.Windows.Controls.TextBlock;
using TextBox = Wpf.Ui.Controls.TextBox;

namespace NetMeter.Pages;

/// <summary>Customizes the taskbar meter with a live preview and one-click styles.</summary>
public partial class WidgetPage : Page
{
    static readonly (string Name, Action<Settings> Apply)[] Presets =
    [
        ("Minimal", s => Preset(s, "Segoe UI Variable Small", "Normal", "Arrows", "Mono", "One", "Full", true)),
        ("Soft color", s => Preset(s, "Segoe UI Variable Small", "Normal", "Arrows", "Icons", "One", "Full", true)),
        ("Colorful", s => Preset(s, "Segoe UI Variable Small", "SemiBold", "Chevrons", "Text", "One", "Full", true)),
        ("Bold", s => Preset(s, "Segoe UI Variable Display", "Bold", "Tray", "Mono", "One", "Full", true)),
        ("Compact line", s => Preset(s, "Segoe UI Variable Small", "Normal", "Arrows", "Icons", "Auto", "Short", false)),
        ("Terminal", s => Preset(s, "Cascadia Mono", "Normal", "Letters", "Icons", "One", "Short", true)),
    ];

    static readonly string[] Swatches = ["#FFFFFF", "#C8C8C8", "#7FE0BC", "#8AB4F8", "#C3A6FF", "#F7BE94", "#FDD663", "#FF9A8F"];

    static void Preset(Settings s, string font, string weight, string icons, string colors, string decimals, string units, bool stacked)
    {
        s.FontFamily = font;
        s.FontWeight = weight;
        s.Icons = icons;
        s.ColorMode = colors;
        s.Decimals = decimals;
        s.UnitStyle = units;
        s.Stacked = stacked;
        s.DimUnits = true;
        s.FontSize = 12;
        s.TextColor = s.DownColor = s.UpColor = "";
    }

    bool previewLight;
    readonly List<MeterView> presetViews = [];

    public WidgetPage()
    {
        InitializeComponent();
        FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).Order().ToList();
        AddHandler(Binding.SourceUpdatedEvent, new EventHandler<DataTransferEventArgs>((_, _) => Core.SaveSettings()));
        BuildPresets();
        BuildColorRows();
        Loaded += (_, _) =>
        {
            previewLight = Core.LightTaskbar();
            DarkPreview.IsChecked = !previewLight;
            Reload();
            Core.SettingsChanged += Rebuild;
            Core.Net.Tick += Tick;
        };
        Unloaded += (_, _) =>
        {
            Core.SettingsChanged -= Rebuild;
            Core.Net.Tick -= Tick;
        };
    }

    /// <summary>Re-reads every control from the settings (after a preset or reset changes many at once).</summary>
    void Reload()
    {
        DataContext = null;
        DataContext = Core.S;
        ItemsList.ItemsSource = Core.S.Items.ToList();
        Rebuild();
    }

    void Rebuild()
    {
        var bg = previewLight ? Color.FromRgb(0xEE, 0xEE, 0xEE) : Color.FromRgb(0x1C, 0x1C, 0x1C);
        var fg = previewLight ? Brushes.Black : Brushes.White;
        Strip.Background = new SolidColorBrush(bg);
        FakeClock.Foreground = fg;
        FakeClock.Text = $"{DateTime.Now:h:mm tt}\n{DateTime.Now:M/d/yyyy}";
        FakeTray.Children.Clear();
        foreach (var g in new[] { "\uE70E", "\uE839", "\uE767" }) // chevron, network, volume
            FakeTray.Children.Add(new TextBlock { Text = g, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14, Foreground = fg, Opacity = 0.85, Margin = new Thickness(8, 0, 0, 0) });

        Preview.Build(Core.S, previewLight);
        for (int i = 0; i < Presets.Length; i++)
        {
            var s = Core.S.Clone();
            Presets[i].Apply(s);
            presetViews[i].Build(s, previewLight);
            ((Border)presetViews[i].Parent).Background = new SolidColorBrush(bg);
        }
        HotkeyHint.Text = Core.S.Hotkey.Length > 0 ? $"Show or hide anytime with {Core.S.Hotkey} (change it in Settings)." : "";
    }

    void Tick()
    {
        Preview.Update();
        foreach (var v in presetViews) v.Update();
    }

    void PreviewTheme_Changed(object sender, RoutedEventArgs e)
    {
        previewLight = DarkPreview.IsChecked != true;
        if (IsLoaded) Rebuild();
    }

    void BuildPresets()
    {
        var tiles = new List<UIElement>();
        foreach (var (name, apply) in Presets)
        {
            var view = new MeterView();
            presetViews.Add(view);
            var tile = new Wpf.Ui.Controls.Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(10),
                Margin = new Thickness(4),
                Content = new StackPanel
                {
                    Children =
                    {
                        new Border { Height = 44, CornerRadius = new CornerRadius(4), Child = view, Padding = new Thickness(10, 0, 10, 0), ClipToBounds = true },
                        new System.Windows.Controls.TextBlock { Text = name, Margin = new Thickness(2, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left },
                    },
                },
            };
            view.HorizontalAlignment = HorizontalAlignment.Center;
            tile.Click += (_, _) =>
            {
                apply(Core.S);
                Core.SaveSettings();
                Reload();
            };
            tiles.Add(tile);
        }
        PresetList.ItemsSource = tiles;
    }

    void BuildColorRows()
    {
        var rows = new List<UIElement>();
        foreach (var (label, prop) in new[] { ("Text", nameof(Settings.TextColor)), ("Download", nameof(Settings.DownColor)), ("Upload", nameof(Settings.UpColor)) })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(new System.Windows.Controls.TextBlock { Text = label, Width = 72, VerticalAlignment = VerticalAlignment.Center, Style = (Style)FindResource("RowTitle") });
            foreach (var hex in Swatches)
            {
                var chip = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Margin = new Thickness(0, 0, 5, 0), Cursor = Cursors.Hand,
                    Background = (Brush)new BrushConverter().ConvertFromString(hex)!, ToolTip = hex,
                    BorderThickness = new Thickness(1), BorderBrush = (Brush)FindResource("ControlStrokeColorDefaultBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                chip.MouseLeftButtonUp += (_, _) => SetColor(prop, hex);
                row.Children.Add(chip);
            }
            var box = new TextBox { Width = 104, PlaceholderText = "Automatic", Margin = new Thickness(6, 0, 0, 0), ClearButtonEnabled = true };
            box.SetBinding(System.Windows.Controls.TextBox.TextProperty, new SetExtension(prop));
            row.Children.Add(box);
            rows.Add(row);
        }
        ColorRows.ItemsSource = rows;
    }

    void SetColor(string prop, string hex)
    {
        typeof(Settings).GetProperty(prop)!.SetValue(Core.S, hex);
        Core.SaveSettings();
        Reload();
    }

    void Move(object sender, int delta)
    {
        var item = (MeterItem)((FrameworkElement)sender).Tag;
        var list = Core.S.Items;
        int i = list.IndexOf(item), j = i + delta;
        if (i < 0 || j < 0 || j >= list.Count) return;
        list.RemoveAt(i);
        list.Insert(j, item);
        ItemsList.ItemsSource = list.ToList();
        Core.SaveSettings();
    }

    void MoveUp_Click(object sender, RoutedEventArgs e) => Move(sender, -1);
    void MoveDown_Click(object sender, RoutedEventArgs e) => Move(sender, 1);

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        var d = new Settings();
        var s = Core.S;
        (s.Items, s.Stacked, s.FontFamily, s.FontSize, s.FontWeight, s.Icons, s.ColorMode, s.Decimals, s.UnitStyle, s.DimUnits,
         s.TextColor, s.DownColor, s.UpColor, s.OffsetX, s.HideInFullscreen, s.Bits) =
        (d.Items, d.Stacked, d.FontFamily, d.FontSize, d.FontWeight, d.Icons, d.ColorMode, d.Decimals, d.UnitStyle, d.DimUnits,
         d.TextColor, d.DownColor, d.UpColor, d.OffsetX, d.HideInFullscreen, d.Bits);
        Core.SaveSettings();
        Reload();
    }
}
