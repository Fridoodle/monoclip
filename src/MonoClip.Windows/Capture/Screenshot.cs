using System.Drawing.Imaging;
using MonoClip.Core;
namespace MonoClip.Windows.Capture;

// Print screen: the monitor the user is on (the one the replay buffer follows) at its full resolution.
internal static class Screenshot
{
    // Copies the screen at once; the app in front names the file. stretch: a game in a stretched resolution is stretched
    // to the desktop's size, as the monitor shows it.
    public static (Bitmap Image, string App) CaptureActiveMonitor(bool stretch)
    {
        var name = ForegroundTracker.Monitor().Name;
        var bounds = (Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name) ?? Screen.PrimaryScreen!).Bounds;
        var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        try { using var g = Graphics.FromImage(image); g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy); }
        catch { image.Dispose(); throw; }
        if (stretch && DisplayMode.Desktop(name) is { } desktop && CapturePolicy.IsStretchedMode(bounds.Width, bounds.Height, desktop.Width, desktop.Height))
            image = Resize(image, desktop);
        var exe = ForegroundTracker.ActiveWindow().Info?.Executable;
        return (image, string.IsNullOrEmpty(exe) ? "Desktop" : Path.GetFileNameWithoutExtension(exe));
    }
    static Bitmap Resize(Bitmap image, Size size)
    {
        using (image)
        {
            var result = new Bitmap(size.Width, size.Height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(result);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(image, new Rectangle(Point.Empty, size));
            return result;
        }
    }
}
