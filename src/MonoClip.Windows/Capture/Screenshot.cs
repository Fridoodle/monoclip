using System.Drawing.Imaging;
namespace MonoClip.Windows.Capture;

// Print screen: the monitor the user is on (the one the replay buffer follows) at its full resolution.
internal static class Screenshot
{
    // Copies the screen at once; the app in front names the file.
    public static (Bitmap Image, string App) CaptureActiveMonitor()
    {
        var name = ForegroundTracker.Monitor().Name;
        var bounds = (Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name) ?? Screen.PrimaryScreen!).Bounds;
        var image = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        try { using var g = Graphics.FromImage(image); g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy); }
        catch { image.Dispose(); throw; }
        var exe = ForegroundTracker.ActiveWindow().Info?.Executable;
        return (image, string.IsNullOrEmpty(exe) ? "Desktop" : Path.GetFileNameWithoutExtension(exe));
    }
}
