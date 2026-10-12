using System.Text.Json.Serialization;
namespace MonoClip.Core;

[JsonConverter(typeof(JsonStringEnumConverter<QualityPreset>))]
public enum QualityPreset { Performance, Balanced, Quality }

public sealed record AppSettings
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public int Fps { get; set; } = 60;
    public int ClipSeconds { get; set; } = 30;
    // Long buffers are allowed; the replay buffer holds the whole length in RAM (see CapturePolicy.BufferMegabytes).
    public const int MinClipSeconds = 5, MaxClipSeconds = 1000;
    public QualityPreset Quality { get; set; } = QualityPreset.Balanced;
    public string Hotkey { get; set; } = "Ctrl+Shift+F9";
    public string ClipsDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "MonoClip");
    public bool Microphone { get; set; } = true;
    public bool DesktopAudio { get; set; } = true;
    public bool ClipBeep { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool StartBufferOnLaunch { get; set; } = true;
    public bool AdvancedMode { get; set; }
    public int ShareMinutes { get; set; } = SharePolicy.DefaultMinutes;
    // Share every new clip right after saving and copy the link (advanced, off by default).
    public bool AutoShare { get; set; }
    // Put the link of every new share on the clipboard (on by default); otherwise use "Copy link".
    public bool CopyShareLink { get; set; } = true;
    // One subfolder per game/application (default) or all clips directly in the clip folder.
    public bool SortByApp { get; set; } = true;
    // Share the MP4 itself instead of a player page with preview (advanced, off by default).
    public bool DirectLink { get; set; }
    // Folder the export editor last saved to; empty means the Videos folder.
    public string ExportDirectory { get; set; } = "";
    public string ExportFolder() => !string.IsNullOrWhiteSpace(ExportDirectory) && Directory.Exists(ExportDirectory) ? ExportDirectory : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
    public string MicrophoneDevice { get; set; } = "default";
    public string DesktopDevice { get; set; } = "default";
    public void Validate()
    {
        if ((Width, Height) is not ((1280, 720) or (1920, 1080) or (2560, 1440) or (3840, 2160)))
            throw new ArgumentException("Unsupported capture resolution; select a supported width/height pair.");
        if (Fps is not (30 or 60 or 120)) throw new ArgumentException("Frame rate must be 30, 60 or 120.");
        if (!Enum.IsDefined(Quality)) throw new ArgumentException("Unknown quality preset.");
        if (string.IsNullOrWhiteSpace(Hotkey)) throw new ArgumentException("Hotkey is required.");
        if (string.IsNullOrWhiteSpace(ClipsDirectory) || !Path.IsPathFullyQualified(ClipsDirectory) || ClipsDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new ArgumentException("Clip folder must be a valid absolute path.");
        if (string.IsNullOrWhiteSpace(MicrophoneDevice) || string.IsNullOrWhiteSpace(DesktopDevice)) throw new ArgumentException("Audio devices must not be empty.");
        if (ClipSeconds is < MinClipSeconds or > MaxClipSeconds) throw new ArgumentException($"Clip duration must be between {MinClipSeconds} and {MaxClipSeconds} seconds.");
        if (ShareMinutes is < SharePolicy.MinMinutes or > SharePolicy.MaxMinutes) throw new ArgumentException($"Link lifetime must be {SharePolicy.MinMinutes}-{SharePolicy.MaxMinutes} minutes.");
    }
}
