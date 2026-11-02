using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Windows.Networking.Connectivity;

namespace NetMeter.Pages;

public partial class OverviewPage : Page
{
    SpeedTest.Meta? meta;

    public OverviewPage()
    {
        InitializeComponent();
        LiveChart.Capacity = NetMonitor.HistoryLength;
        LiveChart.Format = v => Logic.Speed(v, Core.S.Bits);
        LiveChart.MinScale = 64 * 1024;
        Loaded += async (_, _) =>
        {
            Core.Net.Tick += Update;
            Update();
            ShowInfo();
            meta ??= await SpeedTest.GetMeta();
            ShowInfo();
        };
        Unloaded += (_, _) => Core.Net.Tick -= Update;
    }

    void Update()
    {
        var s = Core.S;
        var n = Core.Net;
        DownText.Text = Logic.Speed(n.Down, s.Bits);
        UpText.Text = Logic.Speed(n.Up, s.Bits);
        DownPeak.Text = $"Peak {Logic.Speed(n.PeakDown, s.Bits)} · {Logic.Bytes(n.SessionDown)} this session";
        UpPeak.Text = $"Peak {Logic.Speed(n.PeakUp, s.Bits)} · {Logic.Bytes(n.SessionUp)} this session";

        var p = Core.Ping.Stats();
        PingLabel.Text = $"  Ping · {s.PingHost}";
        PingText.Text = double.IsNaN(Core.Ping.Last) ? (Core.Ping.Offline ? "Offline" : "—") : $"{Core.Ping.Last:0} ms";
        PingSub.Text = double.IsNaN(p.Avg) ? "No replies yet" : $"Avg {p.Avg:0} ms · jitter {p.Jitter:0.0} · loss {p.Loss:P0}";

        var (d, u) = Core.Usage.Today;
        TodayText.Text = Logic.Bytes(d + u);
        TodaySub.Text = $"↓ {Logic.Bytes(d)}   ↑ {Logic.Bytes(u)}";

        LiveChart.Data =
        [
            new("Download", n.HistoryDown, ((SolidColorBrush)FindResource("DownBrush")).Color),
            new("Upload", n.HistoryUp, ((SolidColorBrush)FindResource("UpBrush")).Color),
        ];
        LiveChart.Refresh();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        meta = null;
        ShowInfo();
        meta = await SpeedTest.GetMeta();
        ShowInfo();
    }

    void ShowInfo()
    {
        var rows = new List<KeyValuePair<string, string>>();
        void Row(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) rows.Add(new(k, v)); }

        var a = Core.Net.Adapters.FirstOrDefault();
        if (a == null) Row("Adapter", "No active connection");
        else
        {
            Row("Adapter", a.Description);
            Row("Type", a.NetworkInterfaceType.ToString());
            Row("Link speed", a.Speed > 0 ? $"{a.Speed / 1e6:0} Mbps" : null);
            try
            {
                var ip = a.GetIPProperties();
                Row("Local IPv4", string.Join(", ", ip.UnicastAddresses.Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork).Select(x => x.Address)));
                Row("Gateway", string.Join(", ", ip.GatewayAddresses.Select(g => g.Address).Where(g => g.AddressFamily == AddressFamily.InterNetwork)));
                Row("DNS", string.Join(", ", ip.DnsAddresses.Take(3)));
            }
            catch { }
            Row("MAC", string.Join(":", a.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2"))));
        }

        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile?.IsWlanConnectionProfile == true)
            {
                Row("Wi-Fi", profile.ProfileName);
                if (profile.GetSignalBars() is byte bars)
                    Row("Signal", new string('▮', bars) + new string('▯', 5 - bars) + $"  {bars} of 5");
            }
        }
        catch { }

        Row("Public IP", meta?.Ip ?? "Loading…");
        Row("ISP", meta?.Isp);
        Row("Location", meta == null ? null : $"{meta.City}, {meta.Country}");
        Row("Cloudflare edge", meta?.Colo);
        InfoList.ItemsSource = rows;
    }
}
