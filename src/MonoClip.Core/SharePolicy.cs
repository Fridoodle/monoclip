using System.Security.Cryptography;
using System.Text.RegularExpressions;
namespace MonoClip.Core;

public static partial class SharePolicy
{
    // Short on purpose: friends watch it now, then the home upload is free again.
    public const int DefaultMinutes = 15, MinMinutes = 5, MaxMinutes = 30;
    // A browser or Discord's proxy opens one or two connections per viewer.
    public const int MaxConnections = 6;
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    // Unguessable path: the random tunnel host alone is not a secret once posted somewhere.
    public static string RequestPath(string token, string clipPath) => $"/{token}/{Uri.EscapeDataString(Path.GetFileNameWithoutExtension(clipPath))}.mp4";
    [GeneratedRegex(@"https://(?!api\.)[a-z0-9-]+\.trycloudflare\.com", RegexOptions.IgnoreCase)] private static partial Regex QuickTunnelUrl();
    // cloudflared prints the assigned quick tunnel host in a log box; its API host is not a tunnel.
    public static string? FindQuickTunnelUrl(string logLine) { var m = QuickTunnelUrl().Match(logLine); return m.Success ? m.Value.ToLowerInvariant() : null; }
}
