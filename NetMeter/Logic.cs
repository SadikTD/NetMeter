using System.Globalization;

namespace NetMeter;

/// <summary>Pure helpers with no UI dependencies (unit-tested by NetMeter.Tests).</summary>
public static class Logic
{
    public static string Speed(double bytesPerSec, bool bits)
    {
        var (n, u) = SpeedParts(bytesPerSec, bits);
        return n + " " + u;
    }

    /// <summary>
    /// Splits a speed into number and unit. Bytes scale by 1024, bits by 1000.
    /// decimals: "Auto" (one decimal under 10), "One", "None". unitStyle: "Full" (MB/s), "Short" (MB), "Compact" (M).
    /// </summary>
    public static (string Num, string Unit) SpeedParts(double bytesPerSec, bool bits, string decimals = "Auto", string unitStyle = "Full")
    {
        string[] prefixes = ["K", "M", "G"];
        double k = bits ? 1000 : 1024;
        double v = Math.Max(0, bits ? bytesPerSec * 8 : bytesPerSec) / k;
        int i = 0;
        while (v >= 1000 && i < prefixes.Length - 1) { v /= k; i++; }
        string b = bits ? "b" : "B";
        string unit = unitStyle switch
        {
            "Compact" => prefixes[i],
            "Short" => prefixes[i] + b,
            _ => prefixes[i] + b + "/s",
        };
        string num = decimals switch
        {
            "One" => v.ToString("0.0"),
            "None" => v.ToString("0"),
            _ => v < 9.95 ? v.ToString("0.0") : v.ToString("0"),
        };
        return (num, unit);
    }

    public static string Bytes(double b)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int i = 0;
        while (b >= 1024 && i < units.Length - 1) { b /= 1024; i++; }
        string n = i == 0 ? b.ToString("0") : b < 9.995 ? b.ToString("0.00") : b < 99.95 ? b.ToString("0.0") : b.ToString("0");
        return n + " " + units[i];
    }

    public static (string Num, string Unit) BytesParts(double b)
    {
        var parts = Bytes(b).Split(' ');
        return (parts[0], parts[1]);
    }

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m"
        : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s"
        : $"{Math.Max(0, (int)t.TotalSeconds)}s";

    public static string HourKey(DateTime t) => t.ToString("yyyy-MM-dd HH", CultureInfo.InvariantCulture);
    public static string DayKey(DateTime t) => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string MonthKey(DateTime t) => t.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>First day of the current billing cycle for a monthly data cap that resets on <paramref name="resetDay"/>.</summary>
    public static DateTime CycleStart(DateTime today, int resetDay)
    {
        resetDay = Math.Clamp(resetDay, 1, 31);
        var thisMonth = new DateTime(today.Year, today.Month, Math.Min(resetDay, DateTime.DaysInMonth(today.Year, today.Month)));
        if (today.Date >= thisMonth) return thisMonth;
        var prev = today.AddMonths(-1);
        return new DateTime(prev.Year, prev.Month, Math.Min(resetDay, DateTime.DaysInMonth(prev.Year, prev.Month)));
    }

    /// <summary>Mean absolute difference between consecutive successful pings (RFC 3550 style, unsmoothed).</summary>
    public static double Jitter(IReadOnlyList<double> ms)
    {
        double sum = 0; int n = 0; double? prev = null;
        foreach (var v in ms)
        {
            if (double.IsNaN(v)) continue;
            if (prev is double p) { sum += Math.Abs(v - p); n++; }
            prev = v;
        }
        return n == 0 ? 0 : sum / n;
    }

    public static bool IsNewer(string tag, Version current)
    {
        if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var v)) return false;
        static Version Norm(Version x) => new(x.Major, x.Minor, Math.Max(0, x.Build));
        return Norm(v) > Norm(current);
    }

    public const uint ModAlt = 1, ModCtrl = 2, ModShift = 4, ModWin = 8;

    /// <summary>Parses "Ctrl+Alt+N" into modifier flags and the key name ("N"). Returns false if there is no key or no modifier.</summary>
    public static bool ParseHotkey(string text, out uint mods, out string key)
    {
        mods = 0; key = "";
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= ModCtrl; break;
                case "alt": mods |= ModAlt; break;
                case "shift": mods |= ModShift; break;
                case "win": mods |= ModWin; break;
                default: key = raw; break;
            }
        }
        return mods != 0 && key.Length > 0;
    }
}
