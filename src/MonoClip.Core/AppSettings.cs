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
    public string MicrophoneDevice { get; set; } = "default";
    public string DesktopDevice { get; set; } = "default";
    public void Validate()
    {
        if ((Width, Height) is not ((1280, 720) or (1920, 1080) or (2560, 1440) or (3840, 2160)))
            throw new ArgumentException("Unsupported capture resolution; select a supported width/height pair.");
        if (Fps is not (30 or 60 or 120)) throw new ArgumentException("Frame rate must be 30, 60 or 120.");
        if (!Enum.IsDefined(Quality)) throw new ArgumentException("Unbekannte Qualitätsstufe.");
        if (string.IsNullOrWhiteSpace(Hotkey)) throw new ArgumentException("Bitte einen Hotkey angeben.");
        if (string.IsNullOrWhiteSpace(ClipsDirectory) || !Path.IsPathFullyQualified(ClipsDirectory) || ClipsDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new ArgumentException("Bitte einen absoluten gültigen Clip-Ordner angeben.");
        if (string.IsNullOrWhiteSpace(MicrophoneDevice) || string.IsNullOrWhiteSpace(DesktopDevice)) throw new ArgumentException("Audiogeräte dürfen nicht leer sein.");
        if (ClipSeconds is < 5 or > 300) throw new ArgumentException("Clip duration must be between 5 and 300 seconds.");
    }
}
