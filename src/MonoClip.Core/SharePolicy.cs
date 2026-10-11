using System.Security.Cryptography;
using System.Text.RegularExpressions;
namespace MonoClip.Core;

public static partial class SharePolicy
{
    // Short on purpose: friends watch it now, then the home upload is free again.
    public const int DefaultMinutes = 15, MinMinutes = 5, MaxMinutes = 30;
    // A browser or Discord's proxy opens one or two connections per viewer.
    public const int MaxConnections = 6;
    // Manually chosen files: only real clips, and nothing that would tie up the upload for long.
    public const int MaxShareMegabytes = 500;
    // Discord shows a player only if the whole video has arrived about 10 s after it fetched the page,
    // whatever the size (done after 8-9 s worked for 20 and 40 MB; after 11-14 s failed for 20, 29 and 50 MB).
    // Discord itself sometimes waits up to 5 s before it starts the video download. So the embed video gets
    // as many bytes as this PC's upload sends in EmbedSeconds; 10 MB until the upload has been measured,
    // never more than the largest size tested.
    public const double EmbedSeconds = 4;
    public const long DefaultEmbedBytes = 10L << 20, MinEmbedBytes = 4L << 20, MaxEmbedBytes = 40L << 20;
    public static long EmbedBudget(double uploadBytesPerSecond) => uploadBytesPerSecond <= 0 ? DefaultEmbedBytes : Math.Clamp((long)(uploadBytesPerSecond * EmbedSeconds), MinEmbedBytes, MaxEmbedBytes);
    // Clips within the budget are embedded as they are; larger ones get a smaller copy.
    public static bool NeedsEmbedCopy(long clipBytes, long budget) => clipBytes > budget;
    // Video bitrate for the embed copy: the budget spread over the clip, minus audio and container overhead.
    public static int EmbedVideoBitrate(TimeSpan duration, long budget) => (int)Math.Clamp(budget * 8 / Math.Max(1, duration.TotalSeconds) - 200_000, 500_000, 12_000_000);
    // Highest resolution that still looks clean at that bitrate (30 fps).
    public static int EmbedHeight(int bitrate) => bitrate >= 6_000_000 ? 1080 : bitrate >= 2_000_000 ? 720 : 540;
    // Upload speed from a finished transfer: short ones mostly measure latency and are ignored. The estimate
    // follows new measurements halfway, so one slow transfer (another viewer at the same time) does not halve it.
    public const long MinMeasureBytes = 4L << 20;
    public static double UpdateUploadEstimate(double previous, long bytes, TimeSpan elapsed)
    {
        if (bytes < MinMeasureBytes || elapsed <= TimeSpan.Zero) return previous;
        double measured = bytes / elapsed.TotalSeconds;
        return previous <= 0 ? measured : (previous + measured) / 2;
    }
    static readonly string[] ShareableExtensions = [".mkv", ".mp4"];
    // Throws ArgumentException with a user-facing reason when a file must not be shared.
    public static void CheckShareable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new ArgumentException("File not found.");
        if (!ShareableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Only MKV or MP4 videos can be shared.");
        long size = new FileInfo(path).Length;
        if (size == 0) throw new ArgumentException("The file is empty.");
        if (size > MaxShareMegabytes * 1024L * 1024) throw new ArgumentException($"Too large to share: {size / (1024 * 1024)} MB (max {MaxShareMegabytes} MB).");
        // The extension can lie; the container signature cannot: Matroska starts with EBML, MP4 with an ftyp box.
        var head = new byte[12]; using (var s = File.OpenRead(path)) { if (s.ReadAtLeast(head, head.Length, false) < head.Length) throw new ArgumentException("Not a valid video file."); }
        bool matroska = head.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }), mp4 = head.AsSpan(4, 4).SequenceEqual("ftyp"u8);
        if (!matroska && !mp4) throw new ArgumentException("Not a valid MKV or MP4 video.");
    }
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    // Unguessable path: the random tunnel host alone is not a secret once posted somewhere.
    public static string RequestPath(string token, string clipPath) => $"/{token}/{Uri.EscapeDataString(Path.GetFileNameWithoutExtension(clipPath))}.mp4";
    public static string PagePath(string token) => $"/{token}";
    public static string ImagePath(string token) => $"/{token}/preview.jpg";
    public static string EmbedPath(string token) => $"/{token}/embed.mp4";
    [GeneratedRegex(@"https://(?!api\.)[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)] private static partial Regex QuickTunnelUrl();
    // cloudflared prints the assigned quick tunnel host in a log box; its API host is not a tunnel.
    public static string? FindQuickTunnelUrl(string logLine) { var m = QuickTunnelUrl().Match(logLine); return m.Success ? m.Value.ToLowerInvariant() : null; }
}
