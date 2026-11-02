using System.Diagnostics;
using System.Net;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

namespace NetMeter;

/// <summary>Per-app traffic from the kernel's TCP/UDP ETW events. Needs administrator rights.</summary>
public sealed class AppUsage : IDisposable
{
    const string SessionName = "NetMeter-AppUsage";
    readonly object gate = new();
    readonly Dictionary<int, long[]> byPid = [];
    readonly Dictionary<int, string> names = [];
    TraceEventSession? session;

    public bool Running => session != null;
    public string? Error { get; private set; }

    public void Start()
    {
        if (session != null || !Core.IsElevated) return;
        try
        {
            session = new TraceEventSession(SessionName) { StopOnDispose = true };
            session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
        }
        catch (Exception e)
        {
            Error = e.Message;
            session?.Dispose();
            session = null;
            return;
        }

        var k = session.Source.Kernel;
        k.TcpIpRecv += d => Add(d.ProcessID, d.saddr, d.size, 0);
        k.TcpIpSend += d => Add(d.ProcessID, d.daddr, 0, d.size);
        k.TcpIpRecvIPV6 += d => Add(d.ProcessID, d.saddr, d.size, 0);
        k.TcpIpSendIPV6 += d => Add(d.ProcessID, d.daddr, 0, d.size);
        k.UdpIpRecv += d => Add(d.ProcessID, d.saddr, d.size, 0);
        k.UdpIpSend += d => Add(d.ProcessID, d.daddr, 0, d.size);
        k.UdpIpRecvIPV6 += d => Add(d.ProcessID, d.saddr, d.size, 0);
        k.UdpIpSendIPV6 += d => Add(d.ProcessID, d.daddr, 0, d.size);
        new Thread(() => { try { session?.Source.Process(); } catch { } }) { IsBackground = true, Name = "ETW" }.Start();
    }

    void Add(int pid, IPAddress remote, int rx, int tx)
    {
        if (IPAddress.IsLoopback(remote)) return; // local IPC is not internet traffic
        lock (gate)
        {
            if (!byPid.TryGetValue(pid, out var a)) byPid[pid] = a = new long[2];
            a[0] += rx;
            a[1] += tx;
        }
    }

    /// <summary>Total bytes per app name since tracking started.</summary>
    public Dictionary<string, (long Down, long Up)> Snapshot()
    {
        KeyValuePair<int, long[]>[] copy;
        lock (gate) copy = byPid.Select(p => KeyValuePair.Create(p.Key, (long[])p.Value.Clone())).ToArray();

        var result = new Dictionary<string, (long, long)>();
        foreach (var (pid, v) in copy)
        {
            var name = NameOf(pid);
            var cur = result.GetValueOrDefault(name);
            result[name] = (cur.Item1 + v[0], cur.Item2 + v[1]);
        }
        return result;
    }

    // ponytail: names are cached per PID forever; a reused PID shows the old name until restart.
    string NameOf(int pid)
    {
        if (names.TryGetValue(pid, out var n)) return n;
        try { n = pid switch { 0 => "Idle", 4 => "System", _ => Process.GetProcessById(pid).ProcessName } ; }
        catch { n = $"PID {pid}"; }
        return names[pid] = n;
    }

    public void Dispose()
    {
        session?.Dispose();
        session = null;
    }
}
