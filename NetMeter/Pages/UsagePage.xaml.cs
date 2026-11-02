using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace NetMeter.Pages;

public partial class UsagePage : Page
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };

    public UsagePage()
    {
        InitializeComponent();
        BarChart.Format = v => Logic.Bytes(v);
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { Refresh(); timer.Start(); };
        Unloaded += (_, _) => timer.Stop();
    }

    void Range_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) Refresh();
    }

    void Refresh()
    {
        var u = Core.Usage;
        var now = DateTime.Now;
        static string T((long D, long U) x) => Logic.Bytes(x.D + x.U);
        TodayText.Text = T(u.Today);
        WeekText.Text = T(u.Since(now.Date.AddDays(-6)));
        MonthText.Text = T(u.Sum(Logic.MonthKey(now)));
        AllText.Text = T(u.Since(DateTime.MinValue));

        // Buckets for the selected range: (label, key prefix)
        var buckets = RangeBox.SelectedIndex switch
        {
            0 => Enumerable.Range(0, 24).Select(h => (h.ToString("00"), Logic.HourKey(now.Date.AddHours(h)))),
            2 => Enumerable.Range(0, 30).Select(i => now.Date.AddDays(i - 29)).Select(d => (d.ToString("d MMM"), Logic.DayKey(d))),
            3 => Enumerable.Range(0, 12).Select(i => now.AddMonths(i - 11)).Select(m => (m.ToString("MMM yy"), Logic.MonthKey(m))),
            _ => Enumerable.Range(0, 7).Select(i => now.Date.AddDays(i - 6)).Select(d => (d.ToString("ddd d"), Logic.DayKey(d))),
        };
        var list = buckets.ToList();
        var sums = list.Select(b => u.Sum(b.Item2)).ToList();
        BarChart.Labels = list.Select(b => b.Item1).ToList();
        BarChart.Data =
        [
            new("Download", sums.Select(s => (double)s.Down).ToList(), ((SolidColorBrush)FindResource("DownBrush")).Color),
            new("Upload", sums.Select(s => (double)s.Up).ToList(), ((SolidColorBrush)FindResource("UpBrush")).Color),
        ];
        BarChart.Refresh();
        RangeTotal.Text = $"Total ↓ {Logic.Bytes(sums.Sum(s => s.Down))}   ↑ {Logic.Bytes(sums.Sum(s => s.Up))}";

        if (u.CapBytes <= 0)
        {
            CapText.Text = "No limit set";
            CapBar.Value = 0;
            CapSub.Text = "Set a monthly limit in Settings to get warnings at 80% and 100%.";
        }
        else
        {
            double used = u.CycleUsed / (double)u.CapBytes;
            var next = u.CycleStart.AddMonths(1);
            CapText.Text = $"{Logic.Bytes(u.CycleUsed)} of {Core.S.CapGB:0.#} GB used ({used:P0})";
            CapBar.Value = Math.Min(1, used);
            CapSub.Text = $"Cycle started {u.CycleStart:d MMM}. Resets in {(next - now.Date).Days} days. " +
                          $"{Logic.Bytes(Math.Max(0, u.CapBytes - u.CycleUsed))} left.";
        }
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { FileName = $"NetMeter-usage-{DateTime.Now:yyyy-MM-dd}.csv", Filter = "CSV|*.csv" };
        if (dlg.ShowDialog() != true) return;
        var sb = new StringBuilder("hour,download_bytes,upload_bytes\n");
        foreach (var (k, v) in Core.Usage.D.Hours.OrderBy(h => h.Key, StringComparer.Ordinal))
            sb.Append(CultureInfo.InvariantCulture, $"{k}:00,{v[0]},{v[1]}\n");
        File.WriteAllText(dlg.FileName, sb.ToString());
    }
}
