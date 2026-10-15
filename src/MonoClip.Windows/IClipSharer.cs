namespace MonoClip.Windows;

public sealed record ShareInfo(string Url, DateTimeOffset ExpiresAt, string Clip)
{
    // Whole minutes until the link goes offline; at least 1 while it is still online.
    public int MinutesLeft => Math.Max(1, (int)Math.Ceiling((ExpiresAt - DateTimeOffset.Now).TotalMinutes));
}
// DirectLink: hand out the MP4 itself instead of the player page. Title: shown on the page and in the preview.
// Edit: trim and audio choice from the share editor; null shares the clip as it is.
public sealed record ShareOptions(bool DirectLink = false, string? Title = null, MonoClip.Core.ClipEdit? Edit = null);
public interface IClipSharer : IDisposable
{
    ShareInfo? Current { get; }
    bool IsStarting { get; }
    // True until the tunnel program has been downloaded once.
    bool FirstUse { get; }
    // Raised on the UI thread whenever a share starts, ends or is cancelled.
    event EventHandler? Changed;
    Task<ShareInfo> ShareAsync(string clipPath, TimeSpan duration, IProgress<string>? progress = null, ShareOptions? options = null);
    void Stop();
}
