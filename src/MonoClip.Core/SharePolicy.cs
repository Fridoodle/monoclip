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
    static readonly string[] ShareableExtensions = [".mkv", ".mp4"];
    // Throws ArgumentException with a user-facing reason when a file must not be shared.
    public static void CheckShareable(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) throw new ArgumentException("Datei nicht gefunden.");
        if (!ShareableExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Nur MKV- oder MP4-Videos können geteilt werden.");
        long size = new FileInfo(path).Length;
        if (size == 0) throw new ArgumentException("Die Datei ist leer.");
        if (size > MaxShareMegabytes * 1024L * 1024) throw new ArgumentException($"Zu groß zum Teilen: {size / (1024 * 1024)} MB (maximal {MaxShareMegabytes} MB).");
        // The extension can lie; the container signature cannot: Matroska starts with EBML, MP4 with an ftyp box.
        var head = new byte[12]; using (var s = File.OpenRead(path)) { if (s.ReadAtLeast(head, head.Length, false) < head.Length) throw new ArgumentException("Keine gültige Videodatei."); }
        bool matroska = head.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }), mp4 = head.AsSpan(4, 4).SequenceEqual("ftyp"u8);
        if (!matroska && !mp4) throw new ArgumentException("Keine gültige MKV- oder MP4-Videodatei.");
    }
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    // Unguessable path: the random tunnel host alone is not a secret once posted somewhere.
    public static string RequestPath(string token, string clipPath) => $"/{token}/{Uri.EscapeDataString(Path.GetFileNameWithoutExtension(clipPath))}.mp4";
    [GeneratedRegex(@"https://(?!api\.)[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)] private static partial Regex QuickTunnelUrl();
    // cloudflared prints the assigned quick tunnel host in a log box; its API host is not a tunnel.
    public static string? FindQuickTunnelUrl(string logLine) { var m = QuickTunnelUrl().Match(logLine); return m.Success ? m.Value.ToLowerInvariant() : null; }
}
