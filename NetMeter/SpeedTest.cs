using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace NetMeter;

/// <summary>Speed test against Cloudflare's public speed.cloudflare.com endpoints.</summary>
public static class SpeedTest
{
    const string Base = "https://speed.cloudflare.com";
    const int Streams = 6;
    static readonly TimeSpan PhaseLength = TimeSpan.FromSeconds(10);
    static readonly TimeSpan Warmup = TimeSpan.FromSeconds(2);

    public sealed record Meta(string Ip, string Isp, string City, string Country, string Colo);

    public static async Task<Meta?> GetMeta()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Base + "/meta");
            req.Headers.Referrer = new Uri(Base + "/"); // the endpoint answers "{}" without it
            using var res = await Core.Http.SendAsync(req);
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            string S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            var colo = r.TryGetProperty("colo", out var c) ? $"{S(c, "city")} ({S(c, "iata")})" : "";
            return new Meta(S(r, "clientIp"), S(r, "asOrganization"), S(r, "city"), S(r, "country"), colo);
        }
        catch { return null; }
    }

    /// <summary>Median of several empty requests, in ms.</summary>
    public static async Task<double> Latency(CancellationToken ct)
    {
        var times = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            using var r = await Core.Http.GetAsync(Base + "/__down?bytes=0", HttpCompletionOption.ResponseHeadersRead, ct);
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return times[times.Count / 2];
    }

    public static Task<double> Download(Action<double> progress, CancellationToken ct) =>
        Run(progress, ct, async (count, token) =>
        {
            var buf = new byte[81920];
            using var r = await Core.Http.GetAsync(Base + "/__down?bytes=50000000", HttpCompletionOption.ResponseHeadersRead, token);
            using var s = await r.Content.ReadAsStreamAsync(token);
            int n;
            while ((n = await s.ReadAsync(buf, token)) > 0) count(n);
        });

    public static Task<double> Upload(Action<double> progress, CancellationToken ct)
    {
        var chunk = new byte[4 * 1024 * 1024];
        Random.Shared.NextBytes(chunk);
        return Run(progress, ct, async (count, token) =>
        {
            using var r = await Core.Http.PostAsync(Base + "/__up", new CountingContent(chunk, count), token);
        });
    }

    /// <summary>Runs parallel transfer loops for a fixed time; reports live Mbps and returns the average after warm-up.</summary>
    static async Task<double> Run(Action<double> progress, CancellationToken ct, Func<Action<int>, CancellationToken, Task> transfer)
    {
        long bytes = 0;
        void Count(int n) => Interlocked.Add(ref bytes, n);

        using var phase = CancellationTokenSource.CreateLinkedTokenSource(ct);
        phase.CancelAfter(PhaseLength);
        var workers = Enumerable.Range(0, Streams).Select(_ => Task.Run(async () =>
        {
            try { while (!phase.IsCancellationRequested) await transfer(Count, phase.Token); }
            catch (OperationCanceledException) { }
            catch (HttpRequestException) when (phase.IsCancellationRequested) { }
            catch (IOException) when (phase.IsCancellationRequested) { }
        })).ToArray();

        var sw = Stopwatch.StartNew();
        long lastBytes = 0, warmBytes = -1;
        double lastT = 0, live = 0;
        while (!workers.All(w => w.IsCompleted))
        {
            await Task.Delay(250, CancellationToken.None);
            long b = Interlocked.Read(ref bytes);
            double t = sw.Elapsed.TotalSeconds;
            double inst = (b - lastBytes) * 8 / 1e6 / Math.Max(0.001, t - lastT);
            live = live == 0 ? inst : 0.7 * live + 0.3 * inst;
            lastBytes = b; lastT = t;
            if (warmBytes < 0 && sw.Elapsed >= Warmup) warmBytes = b;
            progress(live);
        }
        ct.ThrowIfCancellationRequested();
        await Task.WhenAll(workers);
        double measured = sw.Elapsed.TotalSeconds - Warmup.TotalSeconds;
        return warmBytes < 0 || measured <= 0 ? live : (Interlocked.Read(ref bytes) - warmBytes) * 8 / 1e6 / measured;
    }

    sealed class CountingContent(byte[] data, Action<int> count) : HttpContent
    {
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            for (int off = 0; off < data.Length; off += 65536)
            {
                int n = Math.Min(65536, data.Length - off);
                await stream.WriteAsync(data.AsMemory(off, n));
                count(n);
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = data.Length;
            return true;
        }
    }
}
