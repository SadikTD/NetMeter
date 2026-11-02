using System.Diagnostics;
using System.IO;
using System.Net.Http;
using Microsoft.Win32;

namespace NetMeter;

/// <summary>App-wide services. Everything here is touched from the UI thread only.</summary>
public static class Core
{
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetMeter");
    public static string SettingsPath => Path.Combine(DataDir, "settings.json");
    public static string DataPath => Path.Combine(DataDir, "data.json");

    public static Settings S = new();
    public static Usage Usage = null!;
    public static NetMonitor Net = null!;
    public static PingMonitor Ping = null!;
    public static readonly AppUsage Apps = new();

    public static readonly HttpClient Http = CreateHttp();

    /// <summary>Raised after any setting changes and has been saved.</summary>
    public static event Action? SettingsChanged;

    public static Action<string, string> Notify = (_, _) => { };

    public static bool IsElevated => Environment.IsPrivilegedProcess;
    public static string ExePath => Environment.ProcessPath!;

    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"NetMeter/{Updater.Current.ToString(3)}");
        return h;
    }

    public static void SaveSettings()
    {
        S.Save();
        ApplyStartup();
        SettingsChanged?.Invoke();
    }

    public static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    static string? appliedStartup;

    /// <summary>Starts with Windows via the Run key, or via an elevated logon task when per-app tracking needs admin.</summary>
    public static void ApplyStartup()
    {
        string want = !S.StartWithWindows ? "off" : S.StartElevated && IsElevated ? "task" : "run";
        if (want == appliedStartup) return;
        appliedStartup = want;

        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
        {
            if (want == "run") key?.SetValue("NetMeter", $"\"{ExePath}\" --startup");
            else key?.DeleteValue("NetMeter", false);
        }

        // The logon task can only be created or removed while elevated.
        if (!IsElevated) return;
        if (want == "task")
            Schtasks($"/Create /TN NetMeter /TR \"\\\"{ExePath}\\\" --startup\" /SC ONLOGON /RL HIGHEST /F");
        else
            Schtasks("/Delete /TN NetMeter /F");
    }

    static void Schtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false });
            p?.WaitForExit(5000);
        }
        catch { }
    }

    /// <summary>True when Windows uses a light taskbar (the "SystemUsesLightTheme" switch).</summary>
    public static bool LightTaskbar()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
