# Changelog

## 1.0.1

- "Start with Windows" always points to the version that was started last; no need to toggle it after an update.
- Scrolling the settings window no longer changes drop-downs or number fields under the cursor.
- Drag a clip onto the settings window to share it.

## 1.0.0

- First stable release.
- Sharing reuses a working tunnel for 30 minutes: the next clip is shared in about 0.5 s instead of 9 s.
- Fixed frequent HTTP 530 errors when sharing: MonoClip checked new links too early, which made Cloudflare report them as missing for several seconds.
- Up to three attempts when Cloudflare returns a broken link.

Known issue: sharing depends on Cloudflare's free quick tunnels and can still fail occasionally, especially after many links in a short time.

## 0.1.9 Beta

- App, documentation and repository are now English only.
- Shorter labels and status messages; removed the subtitle from the settings window.
- German resource files are no longer shipped.

## 0.1.8 Beta

- Option to share every new clip automatically and copy the link.
- Share any MKV/MP4 file up to 500 MB; the file content is verified.
- Sound cues (clip, upload, link ready, error) replace pop-up notifications.
- Tray icon shows when a link is being created, when it is online and when an error occurred.
- Links are ready faster (about 9 s instead of 12+ s). Broken tunnels are detected and replaced automatically.

## 0.1.7 Beta

- Share the last clip as a temporary link (5–30 min) through a Cloudflare quick tunnel.
- App icon; tray icon is a dot (recording) or ring (stopped).
- Resolution and frame rate moved to the advanced settings.
- Portable folder contains only `MonoClip.exe`, `app\` and `info\`.
- Download size reduced from 71 MB to 54 MB; unused .NET and OBS files are no longer shipped.
- Lower CPU use in the capture loop and faster startup.

## 0.1.6 Beta

- Capture follows the window under the cursor instead of a fullscreen game in the background.
- Simple and advanced settings.
- Quality presets with an estimated file size per clip.
- The buffer starts on launch by default.
- "Open clip folder" selects the latest clip.

## 0.1.5 Beta

- Single portable ZIP that includes the capture runtime; no setup script.
- Smaller runtime and a clear error when runtime files are missing.
- Settings window adapts to small sizes and display scaling; dark title bar.
- CI builds and starts the portable ZIP and publishes releases from tags.

## 0.1.4 Beta

- Fixed the runtime setup script for paths with spaces and special characters.

## 0.1.3 Beta

- Hotkeys also work in games that swallow Windows hotkey messages (Raw Input fallback).
- No clip is saved while a new hotkey is being entered.

## 0.1.2 Beta

- Clips are saved without JSON sidecar files.
- Temporary folders no longer appear as games.

## 0.1.1 Beta

- Fixed the monitor capture fallback.
- Replaced the success pop-up with an optional beep.

## 0.1.0 Beta

- First release: tray app with GPU replay buffer, game detection, separate audio tracks and per-game folders.
