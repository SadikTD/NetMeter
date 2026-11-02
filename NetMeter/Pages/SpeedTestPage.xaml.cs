using System.Windows;
using System.Windows.Controls;

namespace NetMeter.Pages;

public partial class SpeedTestPage : Page
{
    CancellationTokenSource? cts;

    public SpeedTestPage()
    {
        InitializeComponent();
        Loaded += (_, _) => ShowHistory();
    }

    void ShowHistory()
    {
        HistoryList.ItemsSource = Core.Usage.D.Tests.Take(50).ToList();
        EmptyText.Visibility = Core.Usage.D.Tests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (cts != null)
        {
            cts.Cancel();
            return;
        }

        cts = new CancellationTokenSource();
        StartButton.Content = "Stop";
        PingText.Text = DownText.Text = UpText.Text = "—";
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        try
        {
            StatusText.Text = "Finding server…";
            var meta = await SpeedTest.GetMeta();
            StatusText.Text = "Measuring latency…";
            double ping = await SpeedTest.Latency(cts.Token);
            PingText.Text = $"{ping:0}";
            Progress.Value = 1;

            StatusText.Text = "Testing download…";
            double down = await SpeedTest.Download(v => Dispatcher.Invoke(() => DownText.Text = $"{v:0.0}"), cts.Token);
            DownText.Text = $"{down:0.0}";
            Progress.Value = 2;

            StatusText.Text = "Testing upload…";
            double up = await SpeedTest.Upload(v => Dispatcher.Invoke(() => UpText.Text = $"{v:0.0}"), cts.Token);
            UpText.Text = $"{up:0.0}";
            Progress.Value = 3;

            var server = meta == null ? "Cloudflare" : $"Cloudflare {meta.Colo}";
            Core.Usage.D.Tests.Insert(0, new SpeedResult { Time = DateTime.Now, DownMbps = down, UpMbps = up, PingMs = ping, Server = server });
            Core.Usage.Log("Speed test", $"↓ {down:0.0} Mbps  ↑ {up:0.0} Mbps  {ping:0} ms");
            StatusText.Text = meta == null ? "Done" : $"Done · {meta.Isp}";
            ShowHistory();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Stopped";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Test failed: " + ex.Message;
        }
        finally
        {
            cts.Dispose();
            cts = null;
            StartButton.Content = "Start test";
            Progress.Visibility = Visibility.Hidden;
        }
    }
}
