using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace NetMeter;

/// <summary>
/// The meter on the taskbar. Windows 11 has no taskbar toolbar API, so this is a small transparent
/// always-on-top window kept just left of the tray icons and re-raised whenever the taskbar comes forward.
/// </summary>
public partial class TaskbarWindow : Window
{
    const int HotkeyId = 1;
    IntPtr hwnd;
        readonly TextBlock tip = new();
    Native.WinEventProc? winEvent;
    IntPtr winEventHook;
    double builtForHeight;

    public TaskbarWindow()
    {
        InitializeComponent();
        ToolTip = tip;
        ToolTipService.SetInitialShowDelay(this, 400);
        ContextMenu = BuildMenu();
        MouseLeftButtonUp += (_, _) => App.ShowDashboard();

        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE,
                Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            winEvent = (_, _, _, _, _, _, _) => Place();
            winEventHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, winEvent, 0, 0, Native.WINEVENT_OUTOFCONTEXT);
            RegisterHotkey();
        };
        new WindowInteropHelper(this).EnsureHandle();

        Core.Net.Tick += Update;
        Core.Ping.Updated += Update;
        Core.SettingsChanged += () => { RegisterHotkey(); Rebuild(); };
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        DpiChanged += (_, _) => Rebuild();
        Closed += (_, _) =>
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (winEventHook != IntPtr.Zero) Native.UnhookWinEvent(winEventHook);
            Native.UnregisterHotKey(hwnd, HotkeyId);
        };
        Rebuild();
    }

    void OnUserPreferenceChanged(object? s, UserPreferenceChangedEventArgs e) => Dispatcher.BeginInvoke(Rebuild);

    ContextMenu BuildMenu()
    {
        var m = new ContextMenu();
        void Add(string text, Action a)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => a();
            m.Items.Add(item);
        }
        Add("Open dashboard", () => App.ShowDashboard());
        Add("Run speed test", () => App.ShowDashboard(typeof(Pages.SpeedTestPage)));
        Add("Data usage", () => App.ShowDashboard(typeof(Pages.UsagePage)));
        Add("Customize widget", () => App.ShowDashboard(typeof(Pages.WidgetPage)));
        Add("Settings", () => App.ShowDashboard(typeof(Pages.SettingsPage)));
        m.Items.Add(new Separator());
        Add("Hide meter", App.ToggleMeter);
        Add("Exit NetMeter", App.Quit);
        m.Opened += (_, _) => Native.SetForegroundWindow(hwnd); // lets a click elsewhere close the menu
        return m;
    }

    // ---- Content ----------------------------------------------------------------

    void Rebuild()
    {
        builtForHeight = Height;
        View.Build(Core.S, Core.LightTaskbar());
        Update();
    }

    void Update()
    {
        View.Update();
        if (IsVisible && IsMouseOver) UpdateTip();
        Place();
    }

    void UpdateTip()
    {
        var s = Core.S;
        var (d, u) = Core.Usage.Today;
        var p = Core.Ping.Last;
        tip.Text =
            $"Download  {Logic.Speed(Core.Net.Down, s.Bits)}\n" +
            $"Upload      {Logic.Speed(Core.Net.Up, s.Bits)}\n" +
            $"Ping          {(double.IsNaN(p) ? "no reply" : $"{p:0} ms")}  ({s.PingHost})\n" +
            $"Today        ↓ {Logic.Bytes(d)}   ↑ {Logic.Bytes(u)}\n" +
            "Click for dashboard, right-click for menu";
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        UpdateTip();
        base.OnMouseEnter(e);
    }

    // ---- Placement ---------------------------------------------------------------

    void Place()
    {
        if (hwnd == IntPtr.Zero) return;
        var taskbar = Native.FindWindow("Shell_TrayWnd", null);
        Native.RECT r = default;
        int monitorBottom = 0;
        bool show = Core.S.ShowMeter && !View.IsEmpty && taskbar != IntPtr.Zero
                    && Native.GetWindowRect(taskbar, out r)
                    && TaskbarOnScreen(taskbar, r, out monitorBottom)
                    && !(Core.S.HideInFullscreen && Native.FullscreenOnPrimary(hwnd));
        if (!show)
        {
            if (IsVisible) Hide();
            return;
        }

        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int tbHeight = Math.Min(r.Bottom, monitorBottom) - r.Top;
        double h = tbHeight / scale;
        if (Math.Abs(Height - h) > 0.5)
        {
            Height = h;
            if (Math.Abs(builtForHeight - h) > 0.5) Rebuild(); // row heights depend on the taskbar height
            return;
        }

        int right = r.Right;
        var trayWnd = Native.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (trayWnd != IntPtr.Zero && Native.GetWindowRect(trayWnd, out var tr) && tr.Right > tr.Left && tr.Left > r.Left)
            right = tr.Left;

        if (!IsVisible) Show();
        int w = (int)Math.Ceiling(ActualWidth * scale);
        int x = right - w - (int)(4 * scale) + (int)(Core.S.OffsetX * scale);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, r.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }

    /// <summary>False when an auto-hidden taskbar has slid off screen.</summary>
    static bool TaskbarOnScreen(IntPtr taskbar, Native.RECT r, out int monitorBottom)
    {
        var area = System.Windows.Forms.Screen.FromHandle(taskbar).Bounds;
        monitorBottom = area.Bottom;
        int visible = Math.Min(r.Bottom, area.Bottom) - Math.Max(r.Top, area.Top);
        return visible > (r.Bottom - r.Top) / 2;
    }

    // ---- Hotkey ------------------------------------------------------------------

    void RegisterHotkey()
    {
        if (hwnd == IntPtr.Zero) return;
        Native.UnregisterHotKey(hwnd, HotkeyId);
        if (!Logic.ParseHotkey(Core.S.Hotkey, out var mods, out var keyName)) return;
        if (!Enum.TryParse<Key>(keyName, true, out var key)) return;
        Native.RegisterHotKey(hwnd, HotkeyId, mods | 0x4000 /* MOD_NOREPEAT */, (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && w.ToInt32() == HotkeyId)
        {
            App.ToggleMeter();
            handled = true;
        }
        return IntPtr.Zero;
    }
}
