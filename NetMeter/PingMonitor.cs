using System.Net.NetworkInformation;

namespace NetMeter;

/// <summary>Pings the chosen host on a loop, tracks latency/jitter/loss, and logs spikes and internet outages.</summary>
public sealed class PingMonitor
{
    public const int HistoryLength = 300;
    const int StatsWindow = 60;
    static readonly string[] Fallbacks = ["1.1.1.1", "8.8.8.8"];

    /// <summary>Round-trip times in ms; NaN marks a lost ping.</summary>
    public readonly List<double> History = [];
    public double Last = double.NaN;
    public bool Offline { get; private set; }
    public event Action? Updated;

    int failStreak, highStreak;
    DateTime firstFail, lastSpikeAlert = DateTime.MinValue;

    public async void Start()
    {
        using var ping = new Ping();
        while (true)
        {
            var host = Core.S.PingHost.Trim();
            double ms = await Send(ping, host);
            bool online = !double.IsNaN(ms);
            if (!online)
                foreach (var f in Fallbacks)
                    if (f != host && !double.IsNaN(await Send(ping, f))) { online = true; break; }
            Handle(host, ms, online);
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(Core.S.PingIntervalSec, 1, 60)));
        }
    }

    static async Task<double> Send(Ping p, string host)
    {
        if (host.Length == 0) return double.NaN;
        try
        {
            var r = await p.SendPingAsync(host, 1500);
            return r.Status == IPStatus.Success ? r.RoundtripTime : double.NaN;
        }
        catch { return double.NaN; }
    }

    void Handle(string host, double ms, bool online)
    {
        Last = ms;
        History.Add(ms);
        if (History.Count > HistoryLength) History.RemoveAt(0);

        if (!online)
        {
            if (failStreak++ == 0) firstFail = DateTime.Now;
            if (failStreak == 3 && !Offline)
            {
                Offline = true;
                if (Core.S.OutageAlerts) Core.Notify("Internet connection lost", "NetMeter will log how long the outage lasts.");
            }
        }
        else
        {
            if (Offline)
            {
                var length = DateTime.Now - firstFail;
                Core.Usage.Log("Outage", $"No internet for {Logic.Duration(length)}", length.TotalSeconds, firstFail);
                if (Core.S.OutageAlerts) Core.Notify("Back online", $"The connection was down for {Logic.Duration(length)}.");
            }
            Offline = false;
            failStreak = 0;
        }

        // Three high pings in a row counts as a spike; alert at most every 5 minutes.
        highStreak = ms > Core.S.PingAlertMs ? highStreak + 1 : 0;
        if (highStreak == 3 && DateTime.Now - lastSpikeAlert > TimeSpan.FromMinutes(5))
        {
            lastSpikeAlert = DateTime.Now;
            var msg = $"{host}: {ms:0} ms (limit {Core.S.PingAlertMs:0} ms)";
            Core.Usage.Log("Ping spike", msg);
            if (Core.S.PingAlerts) Core.Notify("High ping", msg);
        }

        Updated?.Invoke();
    }

    public (double Avg, double Min, double Max, double Jitter, double Loss) Stats()
    {
        var w = History.Skip(Math.Max(0, History.Count - StatsWindow)).ToList();
        var ok = w.Where(v => !double.IsNaN(v)).ToList();
        if (w.Count == 0) return (double.NaN, double.NaN, double.NaN, double.NaN, 0);
        double loss = 1 - ok.Count / (double)w.Count;
        return ok.Count == 0
            ? (double.NaN, double.NaN, double.NaN, double.NaN, loss)
            : (ok.Average(), ok.Min(), ok.Max(), Logic.Jitter(w), loss);
    }
}
