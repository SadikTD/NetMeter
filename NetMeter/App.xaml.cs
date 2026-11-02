using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Forms = System.Windows.Forms;

namespace NetMeter;

public partial class App : Application
{
    const string InstanceName = "NetMeter-7f3c2a";
    static Mutex? mutex;
    static EventWaitHandle? showSignal;
    static MainWindow? dashboard;
    static TaskbarWindow? meter;
    static Forms.NotifyIcon? tray;
    public static bool Exiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
#if DEBUG
        int render = Array.IndexOf(e.Args, "--render");
        if (render >= 0) { RenderPages(e.Args[render + 1]); return; }
        if (e.Args.Contains("--speedtest")) { SpeedTestCheck(); return; }
        if (e.Args.Contains("--wheeltest")) { WheelTest(); return; }
#endif
        bool fromStartup = e.Args.Contains("--startup");
        mutex = new Mutex(true, InstanceName, out bool first);
        if (!first)
        {
            // Already running: a manual launch brings up the dashboard; a duplicate autostart just quits.
            if (!fromStartup && EventWaitHandle.TryOpenExisting(InstanceName + "-show", out var h)) h.Set();
            Shutdown();
            return;
        }
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + "-show");
        new Thread(() => { while (showSignal.WaitOne()) Dispatcher.BeginInvoke(() => ShowDashboard()); }) { IsBackground = true }.Start();

        DispatcherUnhandledException += (_, ex) =>
        {
            Debug.WriteLine(ex.Exception);
            ex.Handled = true; // a monitor app should keep running rather than vanish from the taskbar
        };

        Core.S = Settings.Load();
        Core.Usage = new Usage();
        Core.Notify = Notify;
        Core.Net = new NetMonitor();
        Core.Ping = new PingMonitor();
        Core.Ping.Start();
        Core.ApplyStartup();
        Core.SettingsChanged += () => { ApplyTheme(); UpdateTray(); };

        ApplyTheme();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) => Current.Dispatcher.BeginInvoke(ApplyTheme);
        CreateTray();
        meter = new TaskbarWindow();
        if (!fromStartup || !Core.S.ShowMeter) ShowDashboard();

        StartUpdateChecks();
        base.OnStartup(e);
    }

    public static void ApplyTheme()
    {
        var theme = Core.S.Theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Light ? ApplicationTheme.Light : ApplicationTheme.Dark,
        };
        if (ApplicationThemeManager.GetAppTheme() != theme)
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica);
    }

    public static void ShowDashboard(Type? page = null)
    {
        dashboard ??= new MainWindow();
        dashboard.Show();
        if (dashboard.WindowState == WindowState.Minimized) dashboard.WindowState = WindowState.Normal;
        dashboard.Activate();
        if (page != null) dashboard.Navigate(page);
    }

    void CreateTray()
    {
        var icon = GetResourceStream(new Uri("pack://application:,,,/Assets/NetMeter.ico")).Stream;
        tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(icon), Text = "NetMeter" };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open NetMeter", null, (_, _) => ShowDashboard());
        menu.Items.Add("Run speed test", null, (_, _) => ShowDashboard(typeof(Pages.SpeedTestPage)));
        menu.Items.Add("Show / hide taskbar meter", null, (_, _) => ToggleMeter());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (_, a) => { if (a.Button == Forms.MouseButtons.Left) ShowDashboard(); };
        Core.Net.Tick += () =>
        {
            if (tray.Visible)
                tray.Text = $"NetMeter\n↓ {Logic.Speed(Core.Net.Down, Core.S.Bits)}   ↑ {Logic.Speed(Core.Net.Up, Core.S.Bits)}";
        };
        UpdateTray();
    }

    static void UpdateTray()
    {
        if (tray != null) tray.Visible = Core.S.TrayIcon;
    }

    public static void ToggleMeter()
    {
        Core.S.ShowMeter = !Core.S.ShowMeter;
        Core.SaveSettings();
    }

    /// <summary>Shows a Windows notification through the tray icon (temporarily showing it if the user hid it).</summary>
    static void Notify(string title, string text)
    {
        if (tray == null) return;
        bool wasHidden = !tray.Visible;
        tray.Visible = true;
        tray.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.None);
        if (wasHidden)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            t.Tick += (_, _) => { t.Stop(); UpdateTray(); };
            t.Start();
        }
    }

    static void StartUpdateChecks()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        timer.Tick += async (_, _) =>
        {
            timer.Interval = TimeSpan.FromHours(24);
            if (!Core.S.AutoUpdate) return;
            var r = await Updater.Check();
            if (r != null) Notify($"NetMeter {r.Tag} is available", "Open NetMeter > Settings to install it.");
        };
        timer.Start();
    }

    public static void RestartElevated()
    {
        try
        {
            ReleaseInstance();
            Process.Start(new ProcessStartInfo(Core.ExePath) { UseShellExecute = true, Verb = "runas" });
            Quit();
        }
        catch
        {
            // User declined the UAC prompt: take the instance back and carry on.
            mutex = new Mutex(true, InstanceName, out _);
        }
    }

    static void ReleaseInstance()
    {
        mutex?.ReleaseMutex();
        mutex?.Dispose();
        mutex = null;
    }

#if DEBUG
    /// <summary>Dev aid: runs one speed test and prints the result to the debug log file.</summary>
    async void SpeedTestCheck()
    {
        string result;
        try
        {
            var meta = await SpeedTest.GetMeta();
            var ping = await SpeedTest.Latency(CancellationToken.None);
            var down = await SpeedTest.Download(_ => { }, CancellationToken.None);
            var up = await SpeedTest.Upload(_ => { }, CancellationToken.None);
            result = $"meta={meta} ping={ping:0.0}ms down={down:0.0}Mbps up={up:0.0}Mbps";
        }
        catch (Exception ex) { result = "FAILED " + ex; }
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netmeter-speedtest.txt"), result);
        Shutdown();
    }

    /// <summary>Dev aid: renders every dashboard page to PNG off-screen (no mouse, no second instance).</summary>
    async void RenderPages(string dir)
    {
        Core.S = Settings.Load();
        Core.Usage = new Usage();
        Core.Net = new NetMonitor();
        Core.Ping = new PingMonitor();
        Core.Ping.Start();
        ApplyTheme();
        var w = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -6000, Top = 0, Height = 1300, ShowActivated = false };
        w.Show();
        var pages = new[] { typeof(Pages.WidgetPage), typeof(Pages.OverviewPage), typeof(Pages.UsagePage), typeof(Pages.PingPage), typeof(Pages.SpeedTestPage),
                            typeof(Pages.AppsPage), typeof(Pages.EventsPage), typeof(Pages.SettingsPage) };
        foreach (var t in pages)
        {
            w.Navigate(t);
            await Task.Delay(4000);
            Snap(w, System.IO.Path.Combine(dir, t.Name + ".png"));
            var sv = FindScroll(w);
            if (sv is { ScrollableHeight: > 0 })
            {
                sv.ScrollToEnd();
                await Task.Delay(800);
                Snap(w, System.IO.Path.Combine(dir, t.Name + "-end.png"));
                sv.ScrollToHome();
            }
        }
        Exiting = true;
        Shutdown();
    }

    /// <summary>Dev aid: sends a mouse wheel to a control inside each page and reports whether the page scrolled.</summary>
    async void WheelTest()
    {
        Core.S = Settings.Load();
        Core.Usage = new Usage();
        Core.Net = new NetMonitor();
        Core.Ping = new PingMonitor();
        var w = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -6000, Top = 0, Height = 700, ShowActivated = false };
        w.Show();
        var lines = new List<string>();
        foreach (var t in new[] { typeof(Pages.WidgetPage), typeof(Pages.OverviewPage), typeof(Pages.UsagePage), typeof(Pages.SpeedTestPage), typeof(Pages.EventsPage), typeof(Pages.SettingsPage) })
        {
            w.Navigate(t);
            await Task.Delay(1500);
            var sv = FindScroll(w);
            var target = FindDeep<System.Windows.Controls.TextBlock>(sv!) ?? (UIElement)sv!;
            target.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120) { RoutedEvent = UIElement.MouseWheelEvent });
            await Task.Delay(300);
            lines.Add($"{t.Name}: scrollable={sv!.ScrollableHeight:0} offset={sv.VerticalOffset:0}");
        }
        System.IO.File.WriteAllLines(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "netmeter-wheel.txt"), lines);
        Exiting = true;
        Shutdown();
    }

    static T? FindDeep<T>(DependencyObject d) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            if (c is T hit && FindDeep<T>(c) == null) return hit;
            if (FindDeep<T>(c) is { } found) return found;
        }
        return null;
    }

    /// <summary>The scroll viewer with the most to scroll (the page's, wherever NavigationView put it).</summary>
    static System.Windows.Controls.ScrollViewer? FindScroll(DependencyObject d)
    {
        System.Windows.Controls.ScrollViewer? best = null;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
            foreach (var sv in new[] { c as System.Windows.Controls.ScrollViewer, FindScroll(c) })
                if (sv != null && (best == null || sv.ScrollableHeight > best.ScrollableHeight)) best = sv;
        }
        return best;
    }

    static void Snap(Window w, string path)
    {
        var root = (System.Windows.Media.Visual)w.Content;
        var dv = new System.Windows.Media.DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var bg = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? "#202020" : "#F3F3F3";
            dc.DrawRectangle((System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(bg)!, null, new Rect(0, 0, w.ActualWidth, w.ActualHeight));
            dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, w.ActualWidth, w.ActualHeight));
        }
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)w.ActualWidth, (int)w.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var f = System.IO.File.Create(path);
        enc.Save(f);
    }
#endif

    public static void Quit()
    {
        Exiting = true;
        Core.Usage?.Save();
        Core.Apps.Dispose();
        if (tray != null) tray.Visible = false;
        tray?.Dispose();
        meter?.Close();
        Current.Shutdown();
    }
}
