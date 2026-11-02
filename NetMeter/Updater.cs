using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace NetMeter;

/// <summary>Checks GitHub Releases for a newer version and installs it with the release's setup file.</summary>
public static class Updater
{
    public const string Repo = "SadikTD/NetMeter";
    public static string RepoUrl => $"https://github.com/{Repo}";

    public static Version Current { get; } = typeof(Updater).Assembly.GetName().Version ?? new(1, 0, 0);

    public sealed record Release(string Tag, string PageUrl, string? SetupUrl);
    public static Release? Available { get; private set; }

    public static async Task<Release?> Check()
    {
        try
        {
            using var doc = JsonDocument.Parse(await Core.Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest"));
            var r = doc.RootElement;
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!Logic.IsNewer(tag, Current)) return Available = null;
            string? setup = null;
            foreach (var a in r.GetProperty("assets").EnumerateArray())
                if (a.GetProperty("name").GetString()?.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase) == true)
                    setup = a.GetProperty("browser_download_url").GetString();
            return Available = new Release(tag, r.GetProperty("html_url").GetString() ?? RepoUrl, setup);
        }
        catch { return null; }
    }

    /// <summary>True when running from the installer's folder (a portable copy is updated by hand).</summary>
    static bool Installed =>
        Core.ExePath.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), StringComparison.OrdinalIgnoreCase);

    public static async Task Install(Release r)
    {
        if (r.SetupUrl == null || !Installed)
        {
            Core.OpenUrl(r.PageUrl);
            return;
        }
        var path = Path.Combine(Path.GetTempPath(), "NetMeter-Setup.exe");
        await using (var file = File.Create(path))
        await using (var s = await Core.Http.GetStreamAsync(r.SetupUrl))
            await s.CopyToAsync(file);
        // The installer closes this app and starts the new version when it finishes.
        Process.Start(new ProcessStartInfo(path, "/SILENT /SP- /SUPPRESSMSGBOXES /NORESTART") { UseShellExecute = true });
        App.Quit();
    }
}
