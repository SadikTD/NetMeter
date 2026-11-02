using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows.Threading;

namespace NetMeter;

/// <summary>Samples adapter byte counters on a timer and turns them into speeds.</summary>
public sealed class NetMonitor
{
    public const int HistoryLength = 300;

    public double Down, Up;          // bytes/s, smoothed if enabled
    public double PeakDown, PeakUp;  // since app start
    public long SessionDown, SessionUp;
    public readonly List<double> HistoryDown = [], HistoryUp = [];
    public IReadOnlyList<NetworkInterface> Adapters => adapters;

    public event Action? Tick;

    readonly DispatcherTimer timer = new(DispatcherPriority.Background);
    readonly Stopwatch clock = new();
    List<NetworkInterface> adapters = [];
    bool adaptersDirty = true;
    string adapterKey = "";
    long lastRx, lastTx;
    bool primed;

    public NetMonitor()
    {
        NetworkChange.NetworkAddressChanged += (_, _) => adaptersDirty = true;
        NetworkChange.NetworkAvailabilityChanged += (_, _) => adaptersDirty = true;
        Core.SettingsChanged += () => { adaptersDirty = true; timer.Interval = Interval; };
        timer.Interval = Interval;
        timer.Tick += (_, _) => Sample();
        timer.Start();
        clock.Start();
    }

    static TimeSpan Interval => TimeSpan.FromSeconds(Math.Clamp(Core.S.IntervalSec, 0.25, 5));

    /// <summary>Every adapter that is up, for the settings picker.</summary>
    public static IEnumerable<NetworkInterface> AllUp() =>
        NetworkInterface.GetAllNetworkInterfaces().Where(n =>
            n.OperationalStatus == OperationalStatus.Up &&
            n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel));

    // ponytail: "auto" counts every adapter with a gateway, so a VPN can double count; users pick one adapter if that bites.
    static List<NetworkInterface> Pick(string id) =>
        id.Length > 0
            ? AllUp().Where(n => n.Id == id).ToList()
            : AllUp().Where(n => n.GetIPProperties().GatewayAddresses
                  .Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any))).ToList();

    void Sample()
    {
        if (adaptersDirty)
        {
            adaptersDirty = false;
            try { adapters = Pick(Core.S.Adapter); } catch { adapters = []; }
        }

        long rx = 0, tx = 0;
        foreach (var n in adapters)
        {
            try
            {
                var st = n.GetIPStatistics();
                rx += st.BytesReceived;
                tx += st.BytesSent;
            }
            catch { adaptersDirty = true; }
        }

        double dt = clock.Elapsed.TotalSeconds;
        clock.Restart();

        // A changed adapter set makes the counter jump; skip that one sample instead of showing a fake spike.
        var key = string.Join(",", adapters.Select(a => a.Id));
        long dRx = 0, dTx = 0;
        if (primed && key == adapterKey && dt > 0)
        {
            dRx = Math.Max(0, rx - lastRx);
            dTx = Math.Max(0, tx - lastTx);
        }
        primed = true;
        adapterKey = key;
        lastRx = rx;
        lastTx = tx;

        double rDown = dt > 0 ? dRx / dt : 0, rUp = dt > 0 ? dTx / dt : 0;
        const double a = 0.6;
        Down = Core.S.Smooth ? a * rDown + (1 - a) * Down : rDown;
        Up = Core.S.Smooth ? a * rUp + (1 - a) * Up : rUp;
        if (Down < 1) Down = 0;
        if (Up < 1) Up = 0;
        PeakDown = Math.Max(PeakDown, rDown);
        PeakUp = Math.Max(PeakUp, rUp);
        SessionDown += dRx;
        SessionUp += dTx;

        Push(HistoryDown, rDown);
        Push(HistoryUp, rUp);
        Core.Usage.Add(dRx, dTx);
        Tick?.Invoke();
    }

    static void Push(List<double> list, double v)
    {
        list.Add(v);
        if (list.Count > HistoryLength) list.RemoveAt(0);
    }
}
