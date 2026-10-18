<img width="3200" height="960" alt="readme-banner@2x" src="https://github.com/user-attachments/assets/150a5644-50f3-4a5d-be23-9143cfcc1896" />

# MonoClip

Replay clipping for Windows. MonoClip keeps the last seconds of gameplay in a GPU-encoded buffer in RAM and saves them on a hotkey. It runs in the tray.

> Windows 11 x64. Requires an AMD (AMF), NVIDIA (NVENC) or Intel (Quick Sync) GPU encoder. Tested on AMD hardware; NVIDIA and Intel are untested. No CPU encoding or HDR support.

## Features

- Captures the active window: the hooked game, otherwise the window you are tabbed into, or the monitor when the desktop is active. Works with a controller; the mouse position does not matter.
- 720p–2160p at 30/60/120 FPS, 5–1000 s clips, three quality presets.
- Global hotkey (default `Ctrl+Shift+F9`).
- Screenshots of the monitor you are on as PNG (default `Ctrl+Shift+F10`).
- Separate desktop and microphone tracks plus a mixed track.
- Share a clip as a temporary link (5 min–2 h) without port forwarding, accounts or admin rights.
- Sound cues instead of notifications; tray icon shows the current state.
- Portable: one ZIP, no installer, no telemetry.

## Usage

1. Download `MonoClip-<version>-win-x64-portable.zip` from [Releases](https://github.com/Fridoodle/monoclip/releases) and extract it.
2. Run `MonoClip.exe`. The buffer starts automatically.
3. Press the hotkey to save a clip. Right-click the tray icon for settings, sharing and the clip folder.

The folder contains `MonoClip.exe` (a small launcher), `app\` and `info\`. Settings and logs are stored in `%LOCALAPPDATA%\MonoClip`. To update, exit MonoClip and extract the new version into a new folder. "Start with Windows" follows the version you started last.

## Settings

Double-click the tray icon (or choose **Settings** in its menu) to open MonoClip right above the taskbar next to the tray. It shows the buffer status, the actions and the basic options; the gear opens all other settings. Changes apply immediately, and clicking anywhere else closes the window.

| Setting | Default |
|---|---|
| Clip length | 30 s |
| Hotkey | `Ctrl+Shift+F9` |
| Start with Windows | off |
| Folder | `Videos\MonoClip` |
| Resolution / frame rate (gear) | 1920 × 1080, 60 FPS |
| Quality (gear) | Balanced (15 Mbit/s at 1080p60) |
| Desktop audio, microphone, sound effects (gear) | on |
| Start in tray, start buffer on launch (gear) | on |
| Screenshot hotkey (gear) | `Ctrl+Shift+F10` |
| Screenshot folder (gear) | `Screenshots` in the clip folder |
| Sort clips by application (gear) | on |
| Share new clips automatically (gear) | off |
| Copy new links to the clipboard (gear) | on |
| Link to the video file, no player page (gear) | off |
| Link lifetime (gear) | 15 min |

Bitrate scales with resolution and frame rate: Performance is 0.6× Balanced, Quality is 1.6×, limited to 2.5–100 Mbit/s.

## Clips

Clips are MKV files sorted into one folder per game (`Desktop` for everything else). Turn off **Sort clips by application** to save them all directly in the clip folder. Audio tracks: 1 mixed, 2 desktop, 3 microphone. `.pending` holds recordings whose export failed; they are not deleted automatically.

## Sharing

> **Sharing is experimental.** It relies on Cloudflare's free quick tunnels, which have no uptime guarantee. Creating a link can fail or take longer, especially after many links in a short time. If it fails, try again a few minutes later.

**Quick share** (tray or settings) shares the last clip as it is, creates a link and copies it to the clipboard (unless **Copy new share links to the clipboard** is off; **Copy link** always works). With **Share new clips automatically** this happens after every clip.

**Export clip** opens a small editor with the last clip: watch it, drag the start and end handles to trim it and switch off the game/desktop audio or the microphone (when both were recorded), then **Share** it or **Save…** it as an MP4 to a folder of your choice. **Choose clip…** or dropping a file on the editor switches to another clip. Trimming is lossless and instant, so the clip can start up to a second before the start mark. To open any MKV/MP4 up to 1 GB, drag it onto the settings window or use **Export a file…**.

- The link opens a small player page with a preview image (Open Graph tags, like Medal or YouTube links). **Link to the video file, no player page** (gear) shares the MP4 directly.
- Discord shows a player only if the whole video arrives within about 10 seconds. MonoClip measures your upload while sharing (stored in `%LOCALAPPDATA%\MonoClip\upload-speed.txt`) and gives Discord a copy that uploads in about 4 seconds: up to 1080p, at most 40 MB, 10 MB before the first measurement. Clips that already fit are embedded as they are; the page in a browser always plays the original.
- The clip is remuxed to MP4 and served from `127.0.0.1` through a [Cloudflare quick tunnel](https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/do-more-with-tunnels/trycloudflare/). This works behind CGNAT and does not expose your IP address.
- The link stops working after the chosen time or when sharing is stopped. A working tunnel is kept for 30 minutes after the last share, so the next clip is shared in about a second.
- `cloudflared` (~55 MB) is downloaded from GitHub on first use and verified by SHA256.
- Anyone with the link can watch and download the clip while it is online. Viewers stream from your upload connection (max. 6 connections).

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
