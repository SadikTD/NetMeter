using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace NetMeter.Pages;

public partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        // Every {local:Set ...} binding raises SourceUpdated; save once per change.
        AddHandler(Binding.SourceUpdatedEvent, new EventHandler<DataTransferEventArgs>((_, _) => Core.SaveSettings()));
        Loaded += (_, _) => Load();
    }

    void Load()
    {
        var adapters = new List<KeyValuePair<string, string>> { new("All connected adapters", "") };
        adapters.AddRange(NetMonitor.AllUp().Select(n => new KeyValuePair<string, string>($"{n.Name} ({n.Description})", n.Id)));
        AdapterBox.ItemsSource = adapters;

        DataContext = null;
        DataContext = Core.S;
        HotkeyBox.Text = Core.S.Hotkey;

        ElevatedToggle.IsEnabled = Core.IsElevated;
        ElevatedHint.Text = Core.IsElevated
            ? "Starts at logon with admin rights (no prompt) so the Apps page works right away."
            : "Needed for the Apps page. Restart NetMeter as administrator (Apps page) to change this.";

        VersionText.Text = $"NetMeter {Updater.Current.ToString(3)}";
        ShowUpdate();
    }

    void Widget_Click(object sender, RoutedEventArgs e) => App.ShowDashboard(typeof(WidgetPage));

    void Hotkey_KeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Back or Key.Delete or Key.Escape)
        {
            SetHotkey("");
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0) return; // a bare key would steal normal typing
        parts.Add(key.ToString());
        SetHotkey(string.Join("+", parts));
    }

    void SetHotkey(string hotkey)
    {
        Core.S.Hotkey = hotkey;
        HotkeyBox.Text = hotkey;
        Core.SaveSettings();
    }

    void ShowUpdate()
    {
        var r = Updater.Available;
        InstallButton.Visibility = r == null ? Visibility.Collapsed : Visibility.Visible;
        if (r != null) UpdateText.Text = $"Version {r.Tag} is available.";
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        UpdateText.Text = "Checking…";
        var r = await Updater.Check();
        UpdateText.Text = r == null ? "You have the latest version." : "";
        ShowUpdate();
        CheckButton.IsEnabled = true;
    }

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (Updater.Available is not { } r) return;
        InstallButton.IsEnabled = false;
        UpdateText.Text = "Downloading update…";
        try { await Updater.Install(r); }
        catch (Exception ex) { UpdateText.Text = "Update failed: " + ex.Message; }
        InstallButton.IsEnabled = true;
    }

    void GitHub_Click(object sender, RoutedEventArgs e) => Core.OpenUrl(Updater.RepoUrl);

    void DataFolder_Click(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(Core.DataDir);
        Process.Start(new ProcessStartInfo(Core.DataDir) { UseShellExecute = true });
    }
}
