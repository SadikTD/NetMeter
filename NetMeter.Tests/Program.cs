using System.Globalization;
using NetMeter;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
int failed = 0;
void Eq<T>(T actual, T expected, string what)
{
    if (EqualityComparer<T>.Default.Equals(actual, expected)) return;
    failed++;
    Console.WriteLine($"FAIL {what}: expected <{expected}> got <{actual}>");
}

// Speeds: bytes use 1024, bits use 1000; one decimal under 10.
Eq(Logic.Speed(0, false), "0.0 KB/s", "zero");
Eq(Logic.Speed(512, false), "0.5 KB/s", "half KB");
Eq(Logic.Speed(50 * 1024, false), "50 KB/s", "50 KB");
Eq(Logic.Speed(1000 * 1024, false), "1.0 MB/s", "1000 KB rolls to MB");
Eq(Logic.Speed(5.5 * 1024 * 1024, false), "5.5 MB/s", "5.5 MB");
Eq(Logic.Speed(125_000, true), "1.0 Mb/s", "1 Mbit");
Eq(Logic.Speed(-5, false), "0.0 KB/s", "negative clamps");

Eq(Logic.SpeedParts(19.24 * 1024 * 1024, false, "One", "Full"), ("19.2", "MB/s"), "parts one decimal");
Eq(Logic.SpeedParts(19.24 * 1024 * 1024, false, "Auto", "Short"), ("19", "MB"), "parts auto short");
Eq(Logic.SpeedParts(2.5 * 1024, false, "None", "Compact"), ("3", "K"), "parts none compact");
Eq(Logic.SpeedParts(12_500_000, true, "One", "Full"), ("100.0", "Mb/s"), "parts bits");
Eq(Logic.BytesParts(1536), ("1.50", "KB"), "bytes parts");

Eq(Logic.Bytes(0), "0 B", "0 B");
Eq(Logic.Bytes(1536), "1.50 KB", "1.5 KB");
Eq(Logic.Bytes(1.75 * 1024 * 1024 * 1024), "1.75 GB", "GB");
Eq(Logic.Bytes(250L * 1024 * 1024), "250 MB", "250 MB");

Eq(Logic.Duration(TimeSpan.FromSeconds(45)), "45s", "seconds");
Eq(Logic.Duration(TimeSpan.FromSeconds(133)), "2m 13s", "minutes");
Eq(Logic.Duration(TimeSpan.FromMinutes(65)), "1h 05m", "hours");

// Billing cycle: reset day clamps to month length and rolls back a month before the reset day.
Eq(Logic.CycleStart(new DateTime(2026, 11, 2), 1), new DateTime(2026, 11, 1), "cycle day 1");
Eq(Logic.CycleStart(new DateTime(2026, 11, 2), 15), new DateTime(2026, 10, 15), "cycle before reset");
Eq(Logic.CycleStart(new DateTime(2026, 3, 1), 31), new DateTime(2026, 2, 28), "cycle clamps Feb");
Eq(Logic.CycleStart(new DateTime(2026, 2, 28), 31), new DateTime(2026, 2, 28), "cycle on clamped day");

Eq(Logic.Jitter([10, 20, 10, 20]), 10.0, "jitter");
Eq(Logic.Jitter([10, double.NaN, 14]), 4.0, "jitter skips loss");
Eq(Logic.Jitter([]), 0.0, "jitter empty");

Eq(Logic.IsNewer("v1.2.0", new Version(1, 1, 9)), true, "newer");
Eq(Logic.IsNewer("v1.0.0", new Version(1, 0, 0, 0)), false, "same version with revision");
Eq(Logic.IsNewer("1.0", new Version(1, 0, 0)), false, "two-part tag");
Eq(Logic.IsNewer("nightly", new Version(1, 0, 0)), false, "garbage tag");

Eq(Logic.ParseHotkey("Ctrl+Alt+N", out var m, out var k) && m == (Logic.ModCtrl | Logic.ModAlt) && k == "N", true, "hotkey");
Eq(Logic.ParseHotkey("N", out _, out _), false, "hotkey needs modifier");

Console.WriteLine(failed == 0 ? "All checks passed" : $"{failed} check(s) failed");
return failed == 0 ? 0 : 1;
