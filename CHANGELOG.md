# Changelog

## 1.2.1

- New settings window, like a tray flyout: double-click the tray icon (or choose Settings) and it opens right above the taskbar next to the tray. Clicking anywhere else closes it. It shows the buffer status, the actions (start/stop, save clip, quick share, export) and the basic settings; the gear opens all other settings on a second page.
- Changes apply immediately (after a short pause, as some restart the buffer); there is no Apply button anymore. The clip folder is chosen with the folder picker.
- Smaller and calmer look: slightly rounded blocks that all have the same height, switches instead of check boxes, a segmented quality choice, − / + steppers and dark drop-downs. The export editor's buttons match.
- Lighter recording: with only one audio source on, MonoClip no longer encodes a second track that was an exact copy of the mix (about 1.5 % of a CPU core less). New setting "Separate desktop and mic tracks" (on by default) to skip the extra tracks entirely when both are recorded; Export then has a single audio switch.
- Fixed: every stop or restart of the buffer left the capture sources to be freed after libobs had shut down ("Double destroy" in capture.log).
- Memory used for sharing and exporting (Windows media and shell components) is handed back to Windows afterwards.

## 1.1.2

- Capture follows the active window (the one you are tabbed into) instead of the window under the mouse, so games played with a controller are captured too. The desktop is captured on the monitor of the active window; MonoClip's own windows do not take over the capture.

## 1.1.1

- The tray entry is now **Export clip** (settings: **Export clip**). It opens the editor with the last clip and stays available while a link is online.
- **Choose clip…** (bottom left) opens the clip folder to pick another clip or any MKV/MP4; dropping a file on the editor switches to it as well. Without any saved clip the editor opens empty, ready to choose one.
- More room in the settings (defaults unchanged): clips up to 1000 s (was 300 s), links online up to 2 hours (was 30 min), files up to 1 GB can be shared or exported (was 500 MB).

## 1.1.0

- **Quick share** (as before) and new **Export…**: a small editor to watch the clip, drag the start and end of the timeline to trim it and switch off the game/desktop audio or the microphone, then **Share** it or **Save…** it as an MP4 wherever you like (the last folder is remembered). Dropped files and **Export a file…** open the editor too; automatic sharing stays quick.
- Trimming is lossless and takes a fraction of a second: nothing is re-encoded, so the shared clip can start up to a second before the start mark (at the previous keyframe).
- Fixed: the copy Discord plays (since 1.0.3) used the desktop-only audio track, so the microphone was missing in Discord. It now uses the playback mix like the original.

## 1.0.4

- Intel graphics (Quick Sync) are supported for recording. PCs with an AMD or NVIDIA card keep using it. Untested on Intel hardware so far.

## 1.0.3

- Share links now open a small player page with a preview image, title, length and resolution, like Medal or YouTube links.
- Large clips now play in Discord. Discord shows a player only if the whole video has arrived about 10 seconds after it opened the link, which a home upload cannot do for big clips. MonoClip measures your upload speed while sharing and gives Discord a copy that uploads in about 4 seconds (up to 1080p, at most 40 MB; 10 MB before the first measurement). Clips that already fit are embedded as they are. The copy is made on the GPU with Windows Media Foundation in about 5 seconds, so it does not slow down a running game; the page itself still plays the original.
- Advanced option "Link to the video file instead of a player page" for the previous behaviour.
- Advanced option "Copy new share links to the clipboard" (on by default).

## 1.0.2

- Fixed the settings window layout after moving it between monitors with different scaling (squashed drop-downs, misaligned and oversized rows).
- New option "Sort clips by application" (advanced, on by default). When off, all clips are saved directly in the clip folder.
- New sounds for starting and stopping the buffer and for stopping a share.
- A short sound on the very first start, so new users notice that MonoClip is running in the tray.

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
