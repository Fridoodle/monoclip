using MonoClip.Core;
namespace MonoClip.Windows;

public record AudioDevice(string Id, string Name);
public sealed class ClipSavedEventArgs(ClipRecord clip) : EventArgs { public ClipRecord Clip { get; } = clip; }
public interface IClipEngine : IDisposable
{
    bool IsRunning { get; }
    string Status { get; }
    string EncoderName { get; }
    string CaptureTarget { get; }
    event EventHandler? StatusChanged;
    event EventHandler<ClipSavedEventArgs>? ClipSaved;
    void Start(AppSettings settings);
    void Stop();
    void SaveClip();
    void Apply(AppSettings settings);
    IReadOnlyList<AudioDevice> GetAudioDevices(bool input);
}
