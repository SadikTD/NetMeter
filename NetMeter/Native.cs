using System.Runtime.InteropServices;
using System.Text;

namespace NetMeter;

static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? name);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int index, int value);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    public const int WM_HOTKEY = 0x0312;
    public const uint EVENT_SYSTEM_FOREGROUND = 3, WINEVENT_OUTOFCONTEXT = 0;
    const uint MONITOR_DEFAULTTONEAREST = 2, MONITORINFOF_PRIMARY = 1;

    public static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>True when the foreground window covers the whole primary monitor (games, videos, presentations).</summary>
    public static bool FullscreenOnPrimary(IntPtr own)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == own) return false;
        if (ClassOf(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetWindowRect(fg, out var r) || !GetMonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST), ref mi)) return false;
        var m = mi.rcMonitor;
        return (mi.dwFlags & MONITORINFOF_PRIMARY) != 0
            && r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }
}
