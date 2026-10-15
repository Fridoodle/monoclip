using MonoClip.Core;
namespace MonoClip.Windows;

public sealed class ClipSavedEventArgs(ClipRecord clip) : EventArgs { public ClipRecord Clip { get; } = clip; }
public interface IClipEngine : IDisposable
{
    bool IsRunning { get; }
    string Status { get; }
    string EncoderName { get; }
    string CaptureTarget { get; }
    event EventHandler? StatusChanged;
    // A failure the user must notice (capture stopped, a clip could not be stored…). Its message is also the new
    // Status; a failure that is found again on the next poll is not raised again.
    event EventHandler<string>? Failed;
    event EventHandler<ClipSavedEventArgs>? ClipSaved;
    void Start(AppSettings settings);
    void Stop();
    void SaveClip();
    void Apply(AppSettings settings);
}
