using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NetMeter.Pages;

public partial class AppsPage : Page
{
    public sealed record Row(string Name, string DownNow, string UpNow, string DownTotal, string UpTotal);

    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Stopwatch clock = new();
    Dictionary<string, (long Down, long Up)> last = [];

    public AppsPage()
    {
        InitializeComponent();
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            AdminCard.Visibility = Core.IsElevated ? Visibility.Collapsed : Visibility.Visible;
            Core.Apps.Start(); // no-op unless elevated
            if (Core.Apps.Error is string err)
            {
                ErrorText.Text = "Could not start per-app tracking: " + err;
                ErrorText.Visibility = Visibility.Visible;
            }
            clock.Restart();
            Refresh();
            timer.Start();
        };
        Unloaded += (_, _) => timer.Stop();
    }

    void Refresh()
    {
        if (!Core.Apps.Running)
        {
            EmptyText.Text = Core.IsElevated ? "Per-app tracking is not available." : "Restart as administrator to see which apps use your internet.";
            return;
        }
        double dt = Math.Max(0.1, clock.Elapsed.TotalSeconds);
        clock.Restart();
        var now = Core.Apps.Snapshot();
        bool bits = Core.S.Bits;
        AppList.ItemsSource = now
            .Select(p =>
            {
                var prev = last.GetValueOrDefault(p.Key);
                double dn = (p.Value.Down - prev.Down) / dt, up = (p.Value.Up - prev.Up) / dt;
                return (p.Key, dn, up, p.Value.Down, p.Value.Up);
            })
            .OrderByDescending(r => r.dn + r.up).ThenByDescending(r => r.Item4 + r.Item5)
            .Take(40)
            .Select(r => new Row(r.Key, Logic.Speed(r.dn, bits), Logic.Speed(r.up, bits), Logic.Bytes(r.Item4), Logic.Bytes(r.Item5)))
            .ToList();
        last = now;
        EmptyText.Text = now.Count == 0 ? "Waiting for traffic…" : "Totals count from when tracking started.";
    }

    void Elevate_Click(object sender, RoutedEventArgs e) => App.RestartElevated();
}
