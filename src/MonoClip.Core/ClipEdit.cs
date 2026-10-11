namespace MonoClip.Core;

// What to share of a clip: a time range and one audio track (index among the clip's audio tracks, null for none).
public sealed record ClipEdit(TimeSpan Start, TimeSpan End, int? AudioTrack = 0)
{
    public TimeSpan Length => End - Start;
    public static readonly TimeSpan MinLength = TimeSpan.FromSeconds(1);
    // Unchanged clips (full length, default audio) are shared as they are, without a trim pass.
    public bool ChangesClip(TimeSpan duration, int? defaultTrack) => Start > TimeSpan.FromMilliseconds(50) || End < duration - TimeSpan.FromMilliseconds(50) || AudioTrack != defaultTrack;
    // Share estimate: the clip's size spread evenly over its length.
    public long EstimateBytes(long clipBytes, TimeSpan duration) => duration <= TimeSpan.Zero ? clipBytes : (long)(clipBytes * Math.Clamp(Length / duration, 0, 1));
}

// MonoClip records a "Playback mix" (desktop and microphone) first, then "Desktop" and "Microphone" tracks when
// both sources are on. Only then can one of them be muted on its own; otherwise there is one audio switch.
// Tracks are found by name: Windows lists the audio tracks of an MP4 in a different order than the file.
public static class ClipAudio
{
    public const string Mix = "Playback mix", Desktop = "Desktop", Microphone = "Microphone";
    public static bool CanSplit(IReadOnlyList<string?> tracks) => tracks.Contains(Desktop) && tracks.Contains(Microphone);
    // Label of the single switch when the tracks cannot be split.
    public static string SingleLabel(IReadOnlyList<string?> tracks) => tracks.Contains(Desktop) ? "Desktop audio" : tracks.Contains(Microphone) ? "Microphone" : "Audio";
    // The track a player should play: the playback mix, else the first one.
    public static int? DefaultTrack(IReadOnlyList<string?> tracks) => tracks.Count == 0 ? null : Math.Max(0, IndexOf(tracks, Mix));
    // Track to share for the chosen switches; with a single switch both values must be the same.
    public static int? Choose(IReadOnlyList<string?> tracks, bool keepDesktop, bool keepMicrophone)
    {
        if (tracks.Count == 0) return null;
        if (!CanSplit(tracks)) return keepDesktop && keepMicrophone ? DefaultTrack(tracks) : null;
        return (keepDesktop, keepMicrophone) switch
        {
            (true, true) => DefaultTrack(tracks),
            (true, false) => IndexOf(tracks, Desktop),
            (false, true) => IndexOf(tracks, Microphone),
            _ => null,
        };
    }
    static int IndexOf(IReadOnlyList<string?> tracks, string name) { for (int i = 0; i < tracks.Count; i++) if (tracks[i] == name) return i; return -1; }
}
