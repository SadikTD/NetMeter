using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace NetMeter.Pages;

public partial class EventsPage : Page
{
    public EventsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { Core.Usage.EventLogged += Refresh; Refresh(); };
        Unloaded += (_, _) => Core.Usage.EventLogged -= Refresh;
    }

    string Filter => FilterBox.SelectedIndex <= 0 ? "" : (string)((ComboBoxItem)FilterBox.SelectedItem).Content;

    void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) Refresh();
    }

    void Refresh()
    {
        var all = Core.Usage.D.Events;
        var shown = all.Where(e => Filter == "" || e.Kind == Filter).Take(500).ToList();
        EventList.ItemsSource = shown;
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var month = all.Where(e => e.Kind == "Outage" && e.Time > DateTime.Now.AddDays(-30)).ToList();
        OutageCount.Text = month.Count.ToString();
        OutageTime.Text = Logic.Duration(TimeSpan.FromSeconds(month.Sum(e => e.Seconds)));
        SpikeCount.Text = all.Count(e => e.Kind == "Ping spike" && e.Time > DateTime.Now.AddDays(-7)).ToString();
    }

    void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { FileName = $"NetMeter-events-{DateTime.Now:yyyy-MM-dd}.csv", Filter = "CSV|*.csv" };
        if (dlg.ShowDialog() != true) return;
        static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("time,type,detail,duration_seconds\n");
        foreach (var ev in Core.Usage.D.Events)
            sb.Append(CultureInfo.InvariantCulture, $"{ev.Time:yyyy-MM-dd HH:mm:ss},{Q(ev.Kind)},{Q(ev.Detail)},{ev.Seconds:0}\n");
        File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
    }

    async void Clear_Click(object sender, RoutedEventArgs e)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Clear all events?",
            Content = "This removes the outage, ping spike, speed test and data limit history. It cannot be undone.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
        };
        if (await box.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary) return;
        Core.Usage.D.Events.Clear();
        Core.Usage.Save();
        Refresh();
    }
}
