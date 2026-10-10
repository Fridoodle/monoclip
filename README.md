# MonoClip

Replay clipping for Windows. MonoClip keeps the last seconds of gameplay in a GPU-encoded buffer in RAM and saves them on a hotkey. It runs in the tray.

> Windows 11 x64. Requires an AMD (AMF) or NVIDIA (NVENC) GPU encoder. Tested on AMD hardware; NVIDIA is untested. No Intel-only, CPU encoding or HDR support.

## Features

- Captures what the cursor is over: the hooked game, otherwise the window under the cursor, or the monitor when over the desktop.
- 720p–2160p at 30/60/120 FPS, 5–300 s clips, three quality presets.
- Global hotkey (default `Ctrl+Shift+F9`).
- Separate desktop and microphone tracks plus a mixed track.
- Share a clip as a temporary link (5–30 min) without port forwarding, accounts or admin rights.
- Sound cues instead of notifications; tray icon shows the current state.
- Portable: one ZIP, no installer, no telemetry.

## Usage

1. Download `MonoClip-<version>-win-x64-portable.zip` from [Releases](https://github.com/Fridoodle/monoclip/releases) and extract it.
2. Run `MonoClip.exe`. The buffer starts automatically.
3. Press the hotkey to save a clip. Right-click the tray icon for settings, sharing and the clip folder.

The folder contains `MonoClip.exe` (a small launcher), `app\` and `info\`. Settings and logs are stored in `%LOCALAPPDATA%\MonoClip`. To update, exit MonoClip and extract the new version into a new folder. "Start with Windows" follows the version you started last.

## Settings

The window shows the basic options; **Advanced settings** shows the rest.

| Setting | Default |
|---|---|
| Clip length | 30 s |
| Hotkey | `Ctrl+Shift+F9` |
| Start with Windows | off |
| Folder | `Videos\MonoClip` |
| Resolution / frame rate (advanced) | 1920 × 1080, 60 FPS |
| Quality (advanced) | Balanced (15 Mbit/s at 1080p60) |
| Desktop audio, microphone, sound effects (advanced) | on |
| Start in tray, start buffer on launch (advanced) | on |
| Share new clips automatically (advanced) | off |
| Link lifetime (advanced) | 15 min |

Bitrate scales with resolution and frame rate: Performance is 0.6× Balanced, Quality is 1.6×, limited to 2.5–100 Mbit/s.

## Clips

Clips are MKV files sorted into one folder per game (`Desktop` for everything else). Audio tracks: 1 mixed, 2 desktop, 3 microphone. `.pending` holds recordings whose export failed; they are not deleted automatically.

## Sharing

> **Sharing is experimental.** It relies on Cloudflare's free quick tunnels, which have no uptime guarantee. Creating a link can fail or take longer, especially after many links in a short time. If it fails, try again a few minutes later.

**Share last clip** (tray or settings) creates a link and copies it to the clipboard. With **Share new clips automatically** this happens after every clip. To share any MKV/MP4 up to 500 MB, drag it onto the settings window or use **Share a file…**.

- The clip is remuxed to MP4 and served from `127.0.0.1` through a [Cloudflare quick tunnel](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/do-more-with-tunnels/trycloudflare/). This works behind CGNAT and does not expose your IP address.
- The link stops working after the chosen time or when sharing is stopped. A working tunnel is kept for 30 minutes after the last share, so the next clip is shared in about a second.
- `cloudflared` (~55 MB) is downloaded from GitHub on first use and verified by SHA256.
- Anyone with the link can watch and download the clip while it is online. Viewers stream from your upload connection (max. 6 connections).
- Discord embeds videos up to roughly 100 MB.

## Troubleshooting

- **Black or missing capture:** protected content and some games or anti-cheat systems cannot be captured. Try borderless mode and update the GPU driver.
- **Hotkey does not work:** another app may use it; choose a different one.
- **Missing DLL or "Capture runtime incomplete":** extract the complete ZIP into a new folder. Some antivirus tools quarantine single DLLs.
- **Clips are slightly longer than set:** saves start on a keyframe.

## Building

Requires Windows 11 x64, .NET SDK 10 and Python 3.11+.

```powershell
.\build.ps1
```

This downloads the pinned OBS runtime (SHA256-verified), runs the tests and creates `dist/MonoClip-<version>-win-x64-portable.zip`. The launcher is built with NativeAOT when the Visual Studio C++ build tools are installed.

Tests:

```powershell
dotnet run --project tests/MonoClip.Tests
dotnet run --project tests/MonoClip.UiTests
dotnet build tests/MonoClip.NativeTests -c Release
.\tests\MonoClip.NativeTests\bin\Release\net10.0-windows\MonoClip.NativeTests.exe --capture
```

Run native tests as the built EXE. Other modes include `--exports`, `--restart`, `--rapid-save`, `--export-error`, `--game` (opens a fullscreen test window) and `--share-live` (uses a real Cloudflare tunnel).

Pushing a tag `v<version>` that matches `<Version>` in `MonoClip.Windows.csproj` builds and publishes a release.

## License

GPL-2.0-or-later, see [LICENSE](LICENSE). Bundled OBS libraries and other components keep their own licenses, see [THIRD-PARTY.md](THIRD-PARTY.md).
