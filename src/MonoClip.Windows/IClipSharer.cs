namespace MonoClip.Windows;

public sealed record ShareInfo(string Url, DateTimeOffset ExpiresAt, string Clip);
public interface IClipSharer : IDisposable
{
    ShareInfo? Current { get; }
    bool IsStarting { get; }
    // True until the tunnel program has been downloaded once.
    bool FirstUse { get; }
    // Raised on the UI thread whenever a share starts, ends or is cancelled.
    event EventHandler? Changed;
    Task<ShareInfo> ShareAsync(string clipPath, TimeSpan duration, IProgress<string>? progress = null);
    void Stop();
}
