using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace NetMeter;

/// <summary>Minimal line/area or grouped-bar chart drawn directly with DrawingContext.</summary>
public sealed class Chart : FrameworkElement
{
    public sealed record Series(string Name, IReadOnlyList<double> Data, Color Color);

    public List<Series> Data { get; set; } = [];
    public bool Bars { get; set; }
    /// <summary>Line charts: number of points the width represents, so new points scroll in from the right.</summary>
    public int Capacity { get; set; }
    public IReadOnlyList<string>? Labels { get; set; }
    public Func<double, string> Format { get; set; } = v => v.ToString("0");
    public bool Axis { get; set; } = true;
    public double MinScale { get; set; } = 1;
    public double LineWidth { get; set; } = 1.6;
    public byte FillAlpha { get; set; } = 0x55;
    public Brush AxisBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x55, 0x99, 0x99, 0x99));
    public Brush TextBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0xAA, 0x99, 0x99, 0x99));

    public void Refresh() => InvalidateVisual();

    double Max()
    {
        double m = MinScale;
        foreach (var s in Data)
            foreach (var v in s.Data)
                if (double.IsFinite(v) && v > m) m = v;
        return m * 1.12;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-testable for tooltips
        double labelH = Bars && Labels != null ? 18 : 0;
        double ph = h - labelH;
        double max = Max();
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        if (Axis)
        {
            var pen = new Pen(AxisBrush, 1) { DashStyle = DashStyles.Dot };
            foreach (var f in new[] { 0.5, 1.0 })
            {
                double y = Math.Round(ph - ph * f / 1.12) + 0.5;
                dc.DrawLine(pen, new Point(0, y), new Point(w, y));
                dc.DrawText(Text(Format(max / 1.12 * f), dip), new Point(2, y - 14));
            }
            dc.DrawLine(new Pen(AxisBrush, 1), new Point(0, ph - 0.5), new Point(w, ph - 0.5));
        }

        if (Bars) DrawBars(dc, w, ph, max, dip);
        else DrawLines(dc, w, ph, max);
    }

    void DrawLines(DrawingContext dc, double w, double h, double max)
    {
        foreach (var s in Data)
        {
            int n = s.Data.Count;
            int cap = Math.Max(Capacity, n);
            if (n < 2) continue;
            double step = w / (cap - 1), x0 = w - step * (n - 1);
            var line = new StreamGeometry();
            var area = new StreamGeometry();
            using (var lc = line.Open())
            using (var ac = area.Open())
            {
                bool open = false;
                Point first = default, last = default;
                for (int i = 0; i < n; i++)
                {
                    double v = s.Data[i];
                    if (!double.IsFinite(v))
                    {
                        if (open) { ac.LineTo(new Point(last.X, h), false, false); ac.LineTo(new Point(first.X, h), false, false); }
                        open = false;
                        continue;
                    }
                    var p = new Point(x0 + i * step, h - Math.Min(v, max) / max * h);
                    if (!open) { lc.BeginFigure(p, false, false); ac.BeginFigure(p, true, true); first = p; open = true; }
                    else { lc.LineTo(p, true, true); ac.LineTo(p, false, true); }
                    last = p;
                }
                if (open) { ac.LineTo(new Point(last.X, h), false, false); ac.LineTo(new Point(first.X, h), false, false); }
            }
            var fill = new LinearGradientBrush(Color.FromArgb(FillAlpha, s.Color.R, s.Color.G, s.Color.B), Color.FromArgb(0x05, s.Color.R, s.Color.G, s.Color.B), 90);
            dc.DrawGeometry(fill, null, area);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(s.Color), LineWidth) { LineJoin = PenLineJoin.Round }, line);
        }
    }

    void DrawBars(DrawingContext dc, double w, double h, double max, double dip)
    {
        int n = Data.Count == 0 ? 0 : Data.Max(s => s.Data.Count);
        if (n == 0) return;
        double group = w / n, bw = Math.Max(1, group * 0.72 / Data.Count);
        for (int si = 0; si < Data.Count; si++)
        {
            var brush = new SolidColorBrush(Data[si].Color);
            for (int i = 0; i < Data[si].Data.Count; i++)
            {
                double v = Data[si].Data[i];
                if (!double.IsFinite(v) || v <= 0) continue;
                double bh = Math.Max(1, Math.Min(v, max) / max * h);
                double x = i * group + group * 0.14 + si * bw;
                dc.DrawRoundedRectangle(brush, null, new Rect(x, h - bh, bw - 1, bh), 2, 2);
            }
        }
        if (Labels == null) return;
        int every = Math.Max(1, (int)Math.Ceiling(n * 42 / w));
        for (int i = 0; i < Math.Min(n, Labels.Count); i += every)
        {
            var t = Text(Labels[i], dip);
            dc.DrawText(t, new Point(i * group + group / 2 - t.Width / 2, h + 3));
        }
    }

    FormattedText Text(string s, double dip) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10.5, TextBrush, dip);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int n = Data.Count == 0 ? 0 : Data.Max(s => s.Data.Count);
        if (n == 0 || ActualWidth <= 0) return;
        double x = e.GetPosition(this).X;
        int i;
        if (Bars) i = (int)(x / ActualWidth * n);
        else
        {
            int cap = Math.Max(Capacity, n);
            double step = ActualWidth / (cap - 1);
            i = (int)Math.Round((x - (ActualWidth - step * (n - 1))) / step);
        }
        if (i < 0 || i >= n) { ToolTip = null; return; }
        var lines = Data.Where(s => i < s.Data.Count)
            .Select(s => $"{s.Name}: {(double.IsFinite(s.Data[i]) ? Format(s.Data[i]) : "lost")}");
        var head = Labels != null && i < Labels.Count ? Labels[i] + "\n" : "";
        ToolTip = head + string.Join("\n", lines);
    }
}
