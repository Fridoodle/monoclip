using MonoClip.Core;
using static MonoClip.Windows.Share.Mf;
namespace MonoClip.Windows.Share;

// Lossless trim: copies the clip's H.264 and AAC data from start to end without re-encoding, keeping one
// audio track (or none). Video can only start on a keyframe, so the copy starts at the last keyframe at or
// before the chosen start (MonoClip records one per second). Timestamps are moved to start at zero.
internal static class ClipTrim
{
    // Length and audio track names (null when a track has none) of a clip, read with Media Foundation, and the
    // stream number of each audio track (the same numbering ClipPlayer uses).
    public sealed record Media(TimeSpan Duration, IReadOnlyList<string?> AudioTracks, IReadOnlyList<uint> AudioStreams);
    public static Media Probe(string path)
    {
        Check(MFStartup(MfVersion, StartupLite), "Media Foundation startup");
        IntPtr reader = 0;
        try
        {
            Check(MFCreateSourceReaderFromURL(path, 0, out reader), "Opening the clip");
            var duration = TimeSpan.Zero;
            if (PresentationAttribute(reader, MediaSource, Duration, out var length) && length.Type == VtUi8) duration = TimeSpan.FromTicks(length.Long);
            var audio = AudioStreams(reader);
            return new(duration, audio.Select(a => a.Name).ToList(), audio.Select(a => a.Stream).ToList());
        }
        finally { Release(ref reader); MFShutdown(); }
    }

    // Writes a fast-start MP4 with the edit applied. audioTrack indexes the clip's audio tracks.
    public static void Create(string source, string target, ClipEdit edit, CancellationToken cancel = default)
    {
        var raw = target + ".trim.mp4";
        Check(MFStartup(MfVersion, StartupLite), "Media Foundation startup");
        IntPtr reader = 0, writer = 0, video = 0, audio = 0;
        try
        {
            Check(MFCreateSourceReaderFromURL(source, 0, out reader), "Opening the clip");
            var audioStreams = AudioStreams(reader);
            SelectStream(reader, AllStreams, false);
            Check(MFCreateSinkWriterFromURL(raw, 0, 0, out writer), "Creating the copy");
            // Native (compressed) types on both sides: the sink writer passes the samples through unchanged.
            SelectStream(reader, FirstVideo, true); Check(NativeType(reader, FirstVideo, out video), "video format"); SetReaderType(reader, FirstVideo, video, "video copy");
            var routes = new List<Route> { new(FirstVideo, AddStream(writer, video, "video stream"), Video: true) }; SetWriterInput(writer, routes[0].Writer, video, "video copy");
            if (edit.AudioTrack is int index && index >= 0 && index < audioStreams.Count)
            {
                uint stream = audioStreams[index].Stream;
                SelectStream(reader, stream, true); Check(NativeType(reader, stream, out audio), "audio format"); SetReaderType(reader, stream, audio, "audio copy");
                // OBS's MKV leaves out the AAC sample size, without which the MP4 writer drops the whole track.
                if (!HasU32(audio, BitsPerSample)) SetU32(audio, BitsPerSample, 16);
                var sound = new Route(stream, AddStream(writer, audio, "audio stream"), Video: false); SetWriterInput(writer, sound.Writer, audio, "audio copy"); routes.Add(sound);
            }
            BeginWriting(writer);
            if (edit.Start > TimeSpan.Zero) Seek(reader, edit.Start);

            // The first video sample after the seek is the keyframe the copy starts with (video is read first).
            long origin = long.MinValue, end = edit.End.Ticks;
            Interleave(reader, writer, routes, (route, sample, time) =>
            {
                if (route.Video && origin == long.MinValue) origin = time;
                // Without any video there is nothing to keep. Audio from before the keyframe is dropped, so sound and
                // picture stay in sync.
                if (origin == long.MinValue || time >= end) return SampleFate.EndStream;
                if (time < origin) return SampleFate.Drop;
                SetSampleTime(sample, time - origin); return SampleFate.Write;
            }, cancel);
            if (origin == long.MinValue) throw new InvalidDataException("The clip has no video in the chosen range.");
            FinishWriting(writer); Release(ref writer);
            Mp4FastStart.Apply(raw, target);
        }
        finally
        {
            Release(ref audio); Release(ref video); Release(ref writer); Release(ref reader); MFShutdown();
            if (File.Exists(raw)) File.Delete(raw);
        }
    }
}
