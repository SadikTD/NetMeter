# NetMeter

A calm internet speed meter that lives in the empty space of the Windows 11 taskbar, next to the tray icons. Click it for a Fluent dashboard with live graphs, data usage history, ping monitoring, a speed test, per-app traffic and an outage log.

## Features

**Taskbar meter**
- Download and upload speed in soft, easy-on-the-eyes colors that follow a light or dark taskbar
- Optional extras: ping, data used today, and a mini graph. You choose which ones appear and in what order
- Two-row or single-line layout, plus font, size, colors and a position nudge
- Hides itself during fullscreen games and videos and when an auto-hide taskbar slides away
- Hover for details, click for the dashboard, right-click for a menu, and Ctrl+Alt+N (changeable) to show or hide it

**Dashboard**
- **Overview**: live speeds, session peaks, ping, today's usage, a 5-minute graph and connection details (adapter, IPs, DNS, Wi-Fi signal, public IP, ISP)
- **Data usage**: hourly, daily and monthly charts, a monthly data limit with 80% and 100% alerts, and CSV export
- **Ping**: latency, jitter and packet loss against any host (Cloudflare, Google or a game server), with high-ping alerts
- **Speed test**: download, upload and latency through Cloudflare, with saved history
- **Apps**: which programs are using your internet right now (needs administrator rights)
- **Events**: internet outages with their length, ping spikes, speed tests and data limit warnings, with CSV export

**App**
- Starts with Windows (optionally elevated, so the Apps page works from boot)
- Light, dark or match-Windows theme
- Checks GitHub Releases for updates and installs them in one click

## Install

Download `NetMeter-x.y.z-Setup.exe` from [Releases](https://github.com/SadikTD/NetMeter/releases). It installs for the current user only, so there's no admin prompt. A portable zip is also attached.

The builds aren't code-signed yet, so Windows SmartScreen may show "Windows protected your PC". Click **More info**, then **Run anyway**.

## Privacy

Everything stays on your PC in `%AppData%\NetMeter`. The only network requests NetMeter makes are:
- pings to the host you choose (1.1.1.1 by default, with 8.8.8.8 as a fallback to detect outages)
- `speed.cloudflare.com`, for the speed test and the public IP/ISP card
- `api.github.com`, to check for updates (can be turned off)

## Build from source

Requires the .NET 10 SDK on Windows.

```bash
dotnet run --project NetMeter
```

```bash
dotnet run --project NetMeter.Tests
```

To publish a release, push a tag such as `v1.0.1`. The GitHub Actions workflow tests, publishes, builds the Inno Setup installer and attaches everything to a GitHub release.

## How the taskbar placement works

Windows 11 removed the taskbar toolbar ("deskband") API. NetMeter uses a small transparent always-on-top window, aligned to the left edge of the tray area (`TrayNotifyWnd`) and re-raised whenever the taskbar comes to the front. If a Windows update moves things, use **Settings > Nudge position**.

## License

MIT
