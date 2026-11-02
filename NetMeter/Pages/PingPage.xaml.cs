using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetMeter.Pages;

public partial class PingPage : Page
{
    public PingPage()
    {
        InitializeComponent();
        PingChart.Capacity = PingMonitor.HistoryLength;
        PingChart.Format = v => $"{v:0} ms";
        PingChart.MinScale = 20;
        Loaded += (_, _) =>
        {
            HostBox.Text = Core.S.PingHost;
            Core.Ping.Updated += Update;
            Update();
        };
        Unloaded += (_, _) => Core.Ping.Updated -= Update;
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        var host = HostBox.Text.Trim();
        if (host.Length == 0 || host == Core.S.PingHost) return;
        Core.S.PingHost = host;
        Core.Ping.History.Clear();
        Core.SaveSettings();
        Update();
    }

    void Update()
    {
        static string Ms(double v) => double.IsNaN(v) ? "—" : $"{v:0} ms";
        var p = Core.Ping.Stats();
        NowText.Text = double.IsNaN(Core.Ping.Last) && Core.Ping.Offline ? "Offline" : Ms(Core.Ping.Last);
        AvgText.Text = Ms(p.Avg);
        MinMaxText.Text = double.IsNaN(p.Min) ? "—" : $"{p.Min:0} / {p.Max:0}";
        JitterText.Text = double.IsNaN(p.Jitter) ? "—" : $"{p.Jitter:0.0} ms";
        LossText.Text = $"{p.Loss:P0}";
        var seconds = Core.Ping.History.Count * Core.S.PingIntervalSec;
        ChartTitle.Text = $"{Core.S.PingHost} · last {Logic.Duration(TimeSpan.FromSeconds(seconds))}";
        PingChart.Data = [new("Ping", Core.Ping.History, ((SolidColorBrush)FindResource("PingBrush")).Color)];
        PingChart.Refresh();
    }
}
