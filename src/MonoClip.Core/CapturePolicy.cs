namespace MonoClip.Core;

public static class CapturePolicy
{
    public const int AudioKbpsPerTrack = 160;
    // Balanced 1080p60 reference. Pixels and frames scale sublinearly: H.264 spends
    // fewer bits per pixel at higher resolutions and per frame at higher frame rates.
    const double ReferenceKbps = 15000, PixelExponent = 0.85, FrameExponent = 0.75;
    public static double QualityFactor(QualityPreset quality) => quality switch { QualityPreset.Performance => 0.6, QualityPreset.Quality => 1.6, _ => 1.0 };
    public static int BitrateKbps(int width, int height, int fps, QualityPreset quality = QualityPreset.Balanced)
    {
        var kbps = ReferenceKbps * QualityFactor(quality) * Math.Pow(width * (double)height / (1920 * 1080), PixelExponent) * Math.Pow(fps / 60d, FrameExponent);
        return (int)Math.Clamp(Math.Round(kbps / 500, MidpointRounding.AwayFromZero) * 500, 2500, 100000);
    }
    public static int BitrateKbps(AppSettings s) => BitrateKbps(s.Width, s.Height, s.Fps, s.Quality);
    // Track 1 is always the playback mix (silent when both sources are off).
    public static int AudioTracks(AppSettings s) => 1 + (s.DesktopAudio ? 1 : 0) + (s.Microphone ? 1 : 0);
    public static int TotalKbps(AppSettings s) => BitrateKbps(s) + AudioTracks(s) * AudioKbpsPerTrack;
    // Keyframe-aligned saves (1 s GOP) run up to a second longer; Matroska overhead is ~2 %.
    public static double EstimatedClipMegabytes(AppSettings s) => TotalKbps(s) * 1000d * (s.ClipSeconds + 0.5) / 8 / (1024 * 1024) * 1.02;
    public static int BufferMegabytes(AppSettings s) => (int)Math.Ceiling(TotalKbps(s) * (s.ClipSeconds + 2d) / (8 * 1024) * 1.3) + 16;
}
