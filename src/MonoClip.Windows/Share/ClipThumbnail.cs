using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
namespace MonoClip.Windows.Share;

// Preview image for the share page: the Windows video thumbnail (as in Explorer), or a plain
// MonoClip placeholder when Windows cannot provide one (thumbnails disabled, codec missing).
// Plain vtable calls: the published app is trimmed, which turns off .NET's built-in COM interop.
internal static unsafe class ClipThumbnail
{
    static readonly Guid ImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
    [StructLayout(LayoutKind.Sequential)] struct NativeSize { public int Width, Height; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, in Guid riid, out IntPtr item);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    const int BiggerSizeOk = 0x1, ThumbnailOnly = 0x8;

    // The shell thumbnail API needs an STA thread; the share flow runs elsewhere.
    public static Task<byte[]> CreateJpegAsync(string video, int width, int height, string label)
    {
        var done = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { done.SetResult(CreateJpeg(video, width, height, label)); } catch (Exception e) { done.SetException(e); } }) { IsBackground = true, Name = "MonoClip thumbnail" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return done.Task;
    }
    public static byte[] CreateJpeg(string video, int width, int height, string label)
    {
        using var canvas = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.FromArgb(12, 12, 12)); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.SmoothingMode = SmoothingMode.AntiAlias;
            using var shot = ShellThumbnail(video, width, height);
            if (shot != null)
            {
                float scale = Math.Min((float)width / shot.Width, (float)height / shot.Height); int w = (int)(shot.Width * scale), h = (int)(shot.Height * scale);
                g.DrawImage(shot, (width - w) / 2, (height - h) / 2, w, h);
            }
            else
            {
                float dot = height * 0.12f; using var white = new SolidBrush(Color.FromArgb(240, 240, 240));
                g.FillEllipse(white, (width - dot) / 2, height * 0.36f, dot, dot);
                using var font = new Font("Segoe UI", height * 0.045f, GraphicsUnit.Pixel); using var muted = new SolidBrush(Color.FromArgb(160, 160, 160));
                var text = g.MeasureString(label, font); g.DrawString(label, font, muted, (width - text.Width) / 2, height * 0.58f);
            }
        }
        using var jpeg = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var quality = new EncoderParameters(1); quality.Param[0] = new EncoderParameter(Encoder.Quality, 85L);
        canvas.Save(jpeg, codec, quality); return jpeg.ToArray();
    }
    static Bitmap? ShellThumbnail(string video, int width, int height)
    {
        if (SHCreateItemFromParsingName(Path.GetFullPath(video), IntPtr.Zero, in ImageFactory, out var factory) < 0 || factory == IntPtr.Zero) return null;
        try
        {
            // IShellItemImageFactory::GetImage is the first method after IUnknown.
            IntPtr handle;
            if (((delegate* unmanaged[Stdcall]<IntPtr, NativeSize, int, IntPtr*, int>)(*(void***)factory)[3])(factory, new NativeSize { Width = width, Height = height }, ThumbnailOnly | BiggerSizeOk, &handle) < 0 || handle == IntPtr.Zero) return null;
            try { return Image.FromHbitmap(handle); } finally { DeleteObject(handle); }
        }
        catch (Exception e) when (e is ArgumentException or ExternalException) { return null; }
        finally { Marshal.Release(factory); }
    }
}
