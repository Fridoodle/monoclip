using MonoClip.Core;
using System.Runtime.InteropServices;
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
    // Reader stream index and name of each audio track, in the order Media Foundation lists them.
    public static unsafe List<(uint Stream, string? Name)> AudioStreams(IntPtr reader)
    {
        var audio = new List<(uint, string?)>();
        for (uint i = 0; NativeType(reader, i, out var type) >= 0; i++)
        {
            bool isAudio; try { isAudio = GetGuid(type, MajorType) == Audio; } finally { Release(ref type); }
            if (!isAudio) continue;
            string? name = null;
            if (PresentationAttribute(reader, i, StreamName, out var value))
            {
                if (value.Type == VtLpwstr && value.Pointer != 0) name = Marshal.PtrToStringUni(value.Pointer);
                PropVariantClear(&value);
            }
            audio.Add((i, name));
        }
        return audio;
    }

    sealed class Track { public uint Reader, Writer; public bool Video, Done; public IntPtr Sample; public long Time; }
    // Writes a fast-start MP4 with the edit applied. audioTrack indexes the clip's audio tracks.
    public static void Create(string source, string target, ClipEdit edit, CancellationToken cancel = default)
    {
        var raw = target + ".trim.mp4";
        Check(MFStartup(MfVersion, StartupLite), "Media Foundation startup");
        IntPtr reader = 0, writer = 0, video = 0, audio = 0; var tracks = new List<Track>();
        try
        {
            Check(MFCreateSourceReaderFromURL(source, 0, out reader), "Opening the clip");
            var audioStreams = AudioStreams(reader);
            SelectStream(reader, AllStreams, false);
            Check(MFCreateSinkWriterFromURL(raw, 0, 0, out writer), "Creating the copy");
            // Native (compressed) types on both sides: the sink writer passes the samples through unchanged.
            SelectStream(reader, FirstVideo, true); Check(NativeType(reader, FirstVideo, out video), "video format"); SetReaderType(reader, FirstVideo, video, "video copy");
            var picture = new Track { Reader = FirstVideo, Video = true, Writer = AddStream(writer, video, "video stream") }; SetWriterInput(writer, picture.Writer, video, "video copy"); tracks.Add(picture);
            if (edit.AudioTrack is int index && index >= 0 && index < audioStreams.Count)
            {
                uint stream = audioStreams[index].Stream;
                SelectStream(reader, stream, true); Check(NativeType(reader, stream, out audio), "audio format"); SetReaderType(reader, stream, audio, "audio copy");
                // OBS's MKV leaves out the AAC sample size, without which the MP4 writer drops the whole track.
                if (!HasU32(audio, BitsPerSample)) SetU32(audio, BitsPerSample, 16);
                var sound = new Track { Reader = stream, Writer = AddStream(writer, audio, "audio stream") }; SetWriterInput(writer, sound.Writer, audio, "audio copy"); tracks.Add(sound);
            }
            BeginWriting(writer);
            if (edit.Start > TimeSpan.Zero) Seek(reader, edit.Start);

            // The first video sample after the seek is the keyframe the copy starts with.
            long origin = long.MinValue, end = edit.End.Ticks;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                foreach (var t in tracks) // video first, so the origin is known before any audio is kept
                    while (!t.Done && t.Sample == 0)
                    {
                        if (!ReadSample(reader, t.Reader, out var sample, out var time)) { t.Done = true; break; }
                        if (sample == 0) continue;
                        if (t.Video && origin == long.MinValue) origin = time;
                        if (time >= end) { Marshal.Release(sample); t.Done = true; break; }
                        t.Sample = sample; t.Time = time;
                    }
                if (origin == long.MinValue) break; // no video at all
                var next = tracks.Where(t => t.Sample != 0).MinBy(t => t.Time);
                if (next == null) break;
                // Audio from before the keyframe is dropped, so sound and picture stay in sync.
                if (next.Time >= origin) { SetSampleTime(next.Sample, next.Time - origin); WriteSample(writer, next.Writer, next.Sample); }
                Release(ref next.Sample);
            }
            if (origin == long.MinValue) throw new InvalidDataException("The clip has no video in the chosen range.");
            FinishWriting(writer); Release(ref writer);
            Mp4FastStart.Apply(raw, target);
        }
        finally
        {
            foreach (var t in tracks) Release(ref t.Sample);
            Release(ref audio); Release(ref video); Release(ref writer); Release(ref reader); MFShutdown();
            if (File.Exists(raw)) File.Delete(raw);
        }
    }
}
