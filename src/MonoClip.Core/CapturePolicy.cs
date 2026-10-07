namespace MonoClip.Core;

public static class CapturePolicy
{
    public static int BufferMegabytes(AppSettings settings) => (int)Math.Ceiling((BitrateKbps(settings.Width, settings.Height, settings.Fps) + 512d) * (settings.ClipSeconds + 2) / (8 * 1024) * 1.3) + 16;
    public static int BitrateKbps(int width, int height, int fps) =>
        (int)Math.Clamp(Math.Round(12d * width * height * fps / (1920d * 1080 * 60), MidpointRounding.AwayFromZero) * 1000, 4000, 80000);
}
