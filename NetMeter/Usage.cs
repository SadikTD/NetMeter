using System.IO;
using System.Text.Json;

namespace NetMeter;

public sealed class NetEvent
{
    public DateTime Time { get; set; }
    public string Kind { get; set; } = "";
    public string Detail { get; set; } = "";
    public double Seconds { get; set; }
    public string DurationText => Seconds > 0 ? Logic.Duration(TimeSpan.FromSeconds(Seconds)) : "";
}

public sealed class SpeedResult
{
    public DateTime Time { get; set; }
    public double DownMbps { get; set; }
    public double UpMbps { get; set; }
    public double PingMs { get; set; }
    public string Server { get; set; } = "";
}

public sealed class UsageData
{
    /// <summary>"yyyy-MM-dd HH" -> [bytes down, bytes up].</summary>
    public Dictionary<string, long[]> Hours { get; set; } = [];
    public List<NetEvent> Events { get; set; } = [];
    public List<SpeedResult> Tests { get; set; } = [];
    public string CapWarned { get; set; } = "";
}

/// <summary>Hourly traffic totals, the event log and speed test history, persisted as one JSON file.</summary>
public sealed class Usage
{
    public UsageData D;
    public event Action? EventLogged;
    DateTime lastSave = DateTime.Now;

    public Usage()
    {
        try { D = JsonSerializer.Deserialize<UsageData>(File.ReadAllText(Core.DataPath)) ?? new(); }
        catch { D = new(); }
    }

    public void Add(long rx, long tx)
    {
        if (rx == 0 && tx == 0 && DateTime.Now - lastSave < TimeSpan.FromMinutes(1)) return;
        var key = Logic.HourKey(DateTime.Now);
        if (!D.Hours.TryGetValue(key, out var a)) D.Hours[key] = a = new long[2];
        a[0] += rx;
        a[1] += tx;
        if (DateTime.Now - lastSave >= TimeSpan.FromMinutes(1))
        {
            CheckCap();
            Save();
        }
    }

    /// <summary>Sum of all hours whose key starts with the prefix (a day "yyyy-MM-dd", a month "yyyy-MM" or an hour).</summary>
    public (long Down, long Up) Sum(string prefix)
    {
        long d = 0, u = 0;
        foreach (var (k, v) in D.Hours)
            if (k.StartsWith(prefix, StringComparison.Ordinal)) { d += v[0]; u += v[1]; }
        return (d, u);
    }

    public (long Down, long Up) Today => Sum(Logic.DayKey(DateTime.Now));

    public (long Down, long Up) Since(DateTime start)
    {
        long d = 0, u = 0;
        var from = Logic.HourKey(start.Date);
        foreach (var (k, v) in D.Hours)
            if (string.CompareOrdinal(k, from) >= 0) { d += v[0]; u += v[1]; }
        return (d, u);
    }

    public DateTime CycleStart => Logic.CycleStart(DateTime.Today, (int)Core.S.CapResetDay);
    public long CapBytes => (long)(Core.S.CapGB * 1024 * 1024 * 1024);
    public long CycleUsed { get { var (d, u) = Since(CycleStart); return d + u; } }

    void CheckCap()
    {
        if (CapBytes <= 0) return;
        var cycle = Logic.DayKey(CycleStart);
        double used = CycleUsed / (double)CapBytes;
        string level = used >= 1 ? "100" : used >= 0.8 ? "80" : "";
        if (level == "" || D.CapWarned == $"{cycle}:100" || D.CapWarned == $"{cycle}:{level}") return;
        D.CapWarned = $"{cycle}:{level}";
        var msg = $"{Logic.Bytes(CycleUsed)} of {Core.S.CapGB:0.#} GB used this cycle ({used:P0}).";
        Log("Data cap", msg);
        Core.Notify(level == "100" ? "Data limit reached" : "80% of data limit used", msg);
    }

    public void Log(string kind, string detail, double seconds = 0, DateTime? time = null)
    {
        D.Events.Insert(0, new NetEvent { Time = time ?? DateTime.Now, Kind = kind, Detail = detail, Seconds = seconds });
        if (D.Events.Count > 2000) D.Events.RemoveRange(2000, D.Events.Count - 2000);
        Save();
        EventLogged?.Invoke();
    }

    public void Save()
    {
        lastSave = DateTime.Now;
        try { Core.WriteAtomic(Core.DataPath, JsonSerializer.Serialize(D)); } catch { }
    }
}
