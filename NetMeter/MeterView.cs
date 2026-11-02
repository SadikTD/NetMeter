using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace NetMeter;

/// <summary>
/// The meter's content: rows of [icon][number][unit] stacked two per column, numbers right-aligned in
/// tabular figures so nothing jitters. Used on the taskbar and for the live previews in the dashboard.
/// </summary>
public sealed class MeterView : StackPanel
{
    const string IconFont = "Segoe Fluent Icons";
    readonly List<Action> updaters = [];

    public bool IsEmpty => updaters.Count == 0;

    public MeterView()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular);
    }

    static Color Hex(string s, string fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(s) ? fallback : s.Trim()); }
        catch { return (Color)ColorConverter.ConvertFromString(fallback); }
    }

    public static FontWeight Weight(string w) => w switch
    {
        "Light" => FontWeights.Light,
        "Medium" => FontWeights.Medium,
        "SemiBold" => FontWeights.SemiBold,
        "Bold" => FontWeights.Bold,
        _ => FontWeights.Normal,
    };

    /// <summary>Icon glyphs per style: down, up, ping, today. Null font means the text font.</summary>
    /// <summary>Icon glyphs per style: down, up, ping, today; Fluent ones use the Segoe Fluent Icons font.</summary>
    static (string Down, string Up, string Ping, string Today, bool Fluent)? Glyphs(string style) => style switch
    {
        "Chevrons" => ("\uE70D", "\uE70E", "\uE9D9", "\uEB05", true),
        "Tray" => ("\uE896", "\uE898", "\uE9D9", "\uEB05", true),
        "Letters" => ("D", "U", "P", "T", false),
        "None" => null,
        _ => ("\uE74B", "\uE74A", "\uE9D9", "\uEB05", true), // Arrows
    };

    public void Build(Settings s, bool lightTaskbar)
    {
        Children.Clear();
        updaters.Clear();

        FontFamily family;
        try { family = new FontFamily(string.IsNullOrWhiteSpace(s.FontFamily) ? "Segoe UI Variable Small" : s.FontFamily); }
        catch { family = new FontFamily("Segoe UI"); }
        double size = Math.Clamp(s.FontSize, 8, 24);
        var weight = Weight(s.FontWeight);
        TextElement.SetFontFamily(this, family);
        TextElement.SetFontSize(this, size);
        TextElement.SetFontWeight(this, weight);

        var text = Hex(s.TextColor, lightTaskbar ? "#1A1A1A" : "#FFFFFF");
        var down = Hex(s.DownColor, lightTaskbar ? "#0F7B5A" : "#7FE0BC");
        var up = Hex(s.UpColor, lightTaskbar ? "#B0541E" : "#F7BE94");
        var warn = Hex("", lightTaskbar ? "#9A6B00" : "#F2D07A");
        var bad = Hex("", lightTaskbar ? "#C42B1C" : "#FF9A8F");
        bool mono = s.ColorMode == "Mono", colorText = s.ColorMode == "Text";
        Brush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        var textB = B(text);

        // Measure with a real TextBlock so tabular figures and Display-mode rounding match what gets drawn.
        double W(string str, FontFamily f, double sz)
        {
            var t = new TextBlock { Text = str, FontFamily = f, FontSize = sz, FontWeight = weight };
            Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
            TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display);
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Ceiling(t.DesiredSize.Width) + 1;
        }

        var glyphs = Glyphs(s.Icons);
        var iconFamily = glyphs is { Fluent: true } ? new FontFamily(IconFont) : family;
        double iconSize = glyphs is { Fluent: true } ? Math.Round(size * 0.82) : size;
        double iconW = glyphs == null ? 0 : new[] { glyphs.Value.Down, glyphs.Value.Up, glyphs.Value.Ping, glyphs.Value.Today }.Max(g => W(g, iconFamily, iconSize));
        string sample = s.Decimals == "None" ? "8888" : "888.8";
        double numW = Math.Max(W(sample, family, size), W("8.88", family, size));
        var units = new List<string> { Logic.SpeedParts(1e9, s.Bits, s.Decimals, s.UnitStyle).Unit, Logic.SpeedParts(1e6, s.Bits, s.Decimals, s.UnitStyle).Unit, Logic.SpeedParts(1e3, s.Bits, s.Decimals, s.UnitStyle).Unit };
        if (s.Items.Any(i => i.Enabled && i.Kind == "Ping")) units.Add("ms");
        if (s.Items.Any(i => i.Enabled && i.Kind == "Today")) units.AddRange(["MB", "GB"]);
        double unitW = units.Max(u => W(u, family, size));
        double gap = Math.Round(size * 0.3);
        double rowH = Math.Round(size * 1.4);
        double colGap = Math.Round(size * 0.9);
        double iconOpacity = mono ? 0.6 : 1, unitOpacity = s.DimUnits ? 0.6 : 1;

        StackPanel? column = null;
        void Place(FrameworkElement row, bool fullHeight)
        {
            if (fullHeight || !s.Stacked || column == null || column.Children.Count == 2)
            {
                column = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Children.Count == 0 ? 0 : colGap, 0, 0, 0) };
                Children.Add(column);
            }
            column.Children.Add(row);
            if (fullHeight) column = null;
        }

        (TextBlock? Icon, TextBlock Num, TextBlock Unit) Row(string? glyph, Color accent)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Height = rowH };
            TextBlock? icon = null;
            if (glyph != null)
            {
                icon = new TextBlock
                {
                    Text = glyph, Width = iconW, FontFamily = iconFamily, FontSize = iconSize,
                    Foreground = mono ? textB : B(accent), Opacity = iconOpacity,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, gap, 0),
                };
                row.Children.Add(icon);
            }
            var num = new TextBlock { MinWidth = numW, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Foreground = colorText ? B(accent) : textB };
            var unit = new TextBlock { MinWidth = unitW, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(gap, 0, 0, 0), Foreground = colorText ? B(accent) : textB, Opacity = unitOpacity };
            row.Children.Add(num);
            row.Children.Add(unit);
            Place(row, false);
            return (icon, num, unit);
        }

        foreach (var item in s.Items.Where(i => i.Enabled))
        {
            switch (item.Kind)
            {
                case "Down":
                {
                    var r = Row(glyphs?.Down, down);
                    updaters.Add(() => (r.Num.Text, r.Unit.Text) = Logic.SpeedParts(Core.Net.Down, s.Bits, s.Decimals, s.UnitStyle));
                    break;
                }
                case "Up":
                {
                    var r = Row(glyphs?.Up, up);
                    updaters.Add(() => (r.Num.Text, r.Unit.Text) = Logic.SpeedParts(Core.Net.Up, s.Bits, s.Decimals, s.UnitStyle));
                    break;
                }
                case "Ping":
                {
                    var r = Row(glyphs?.Ping, down);
                    var good = mono ? textB : B(down);
                    updaters.Add(() =>
                    {
                        double ms = Core.Ping.Last;
                        r.Num.Text = double.IsNaN(ms) ? "–" : ms.ToString("0");
                        r.Unit.Text = "ms";
                        // Mono stays neutral until the ping is actually bad.
                        Brush state = double.IsNaN(ms) || ms > s.PingAlertMs ? B(bad) : ms > s.PingAlertMs * 0.6 && !mono ? B(warn) : good;
                        if (r.Icon != null) { r.Icon.Foreground = state; r.Icon.Opacity = ReferenceEquals(state, good) ? iconOpacity : 1; }
                        if (colorText || r.Icon == null) r.Num.Foreground = ReferenceEquals(state, good) ? (colorText ? B(down) : textB) : state;
                    });
                    break;
                }
                case "Today":
                {
                    var r = Row(glyphs?.Today, text);
                    updaters.Add(() => { var (d, u) = Core.Usage.Today; (r.Num.Text, r.Unit.Text) = Logic.BytesParts(d + u); });
                    break;
                }
                case "Graph":
                {
                    var chart = new Chart
                    {
                        Axis = false,
                        Capacity = 40,
                        Width = Math.Round(size * 4.5),
                        Height = s.Stacked ? rowH * 2 - 4 : rowH,
                        VerticalAlignment = VerticalAlignment.Center,
                        IsHitTestVisible = false,
                        MinScale = 16 * 1024,
                        LineWidth = 1.1,
                        FillAlpha = 0x30,
                    };
                    var dc = mono ? text : down;
                    var uc = mono ? Color.FromArgb(0x80, text.R, text.G, text.B) : up;
                    updaters.Add(() =>
                    {
                        chart.Data = [new("Down", Tail(Core.Net.HistoryDown, 40), dc), new("Up", Tail(Core.Net.HistoryUp, 40), uc)];
                        chart.Refresh();
                    });
                    Place(chart, true);
                    break;
                }
            }
        }
        Update();
    }

    static List<double> Tail(List<double> l, int n) => l.Skip(Math.Max(0, l.Count - n)).ToList();

    public void Update()
    {
        foreach (var u in updaters) u();
    }
}
