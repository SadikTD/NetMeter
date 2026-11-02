using System.IO;
using System.Text.Json;
using System.Windows.Data;

namespace NetMeter;

public sealed class MeterItem
{
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; }

    public string Label => Kind switch
    {
        "Down" => "Download speed",
        "Up" => "Upload speed",
        "Ping" => "Ping (ms)",
        "Today" => "Data used today",
        "Graph" => "Mini graph",
        _ => Kind,
    };
}

public sealed class Settings
{
    // Taskbar meter
    public bool ShowMeter { get; set; } = true;
    public List<MeterItem> Items { get; set; } =
    [
        new() { Kind = "Down", Enabled = true },
        new() { Kind = "Up", Enabled = true },
        new() { Kind = "Ping" },
        new() { Kind = "Today" },
        new() { Kind = "Graph" },
    ];
    public bool Stacked { get; set; } = true;
    public string FontFamily { get; set; } = "Segoe UI Variable Small";
    public double FontSize { get; set; } = 12;
    public string FontWeight { get; set; } = "Normal";   // Light, Normal, Medium, SemiBold, Bold
    public string Icons { get; set; } = "Arrows";        // Arrows, Triangles, Fluent, Letters, None
    public string ColorMode { get; set; } = "Mono";      // Mono, Icons, Text
    public string Decimals { get; set; } = "One";        // Auto, One, None
    public string UnitStyle { get; set; } = "Full";      // Full (MB/s), Short (MB), Compact (M)
    public bool DimUnits { get; set; } = true;
    public string TextColor { get; set; } = "";          // "" = white or black to match the taskbar
    public string DownColor { get; set; } = "";          // "" = soft default that suits the taskbar theme
    public string UpColor { get; set; } = "";
    public double OffsetX { get; set; }
    public bool HideInFullscreen { get; set; } = true;

    // Measurement
    public double IntervalSec { get; set; } = 1;
    public bool Bits { get; set; }
    public bool Smooth { get; set; } = true;
    public string Adapter { get; set; } = ""; // "" = all active adapters with a gateway

    // Ping and outages
    public string PingHost { get; set; } = "1.1.1.1";
    public double PingIntervalSec { get; set; } = 2;
    public double PingAlertMs { get; set; } = 100;
    public bool PingAlerts { get; set; } = true;
    public bool OutageAlerts { get; set; } = true;

    // Data cap (0 = off)
    public double CapGB { get; set; }
    public double CapResetDay { get; set; } = 1;

    // App
    public string Theme { get; set; } = "System";
    public bool StartWithWindows { get; set; } = true;
    public bool StartElevated { get; set; }
    public bool TrayIcon { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+N";
    public bool AutoUpdate { get; set; } = true;

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Core.SettingsPath)) ?? new();
            // Keep every meter item present even if an older file lacks newer ones.
            foreach (var d in new Settings().Items)
                if (!s.Items.Any(i => i.Kind == d.Kind)) s.Items.Add(d);
            return s;
        }
        catch { return new(); }
    }

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this))!;

    public void Save() => Core.WriteAtomic(Core.SettingsPath, JsonSerializer.Serialize(this, Json));
}

/// <summary>Two-way binding that writes back immediately and reports the change, so settings pages save without code per control.</summary>
public sealed class SetExtension : Binding
{
    public SetExtension(string path) : base(path)
    {
        Mode = BindingMode.TwoWay;
        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
        NotifyOnSourceUpdated = true;
    }
}
