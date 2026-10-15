using MonoClip.Core;
using System.Runtime.InteropServices;
using static MonoClip.Windows.Share.Mf;
namespace MonoClip.Windows.Share;

// Smaller copy of a large clip for chat embeds: Discord shows a player only when it can download the whole
// og:video within a few seconds, which a home upload cannot do for a full-quality clip. Windows Media
// Foundation decodes the clip, scales it (SharePolicy.EmbedHeight, at most 30 fps) and re-encodes it as
// H.264 at a bitrate that keeps the copy near the given size budget. The playback mix (ClipAudio.DefaultTrack)
// is copied unchanged.
// It may run right after a clip while a game is still running: decoding, scaling and encoding all happen on
// the GPU (Direct3D 11) when possible, because the software path keeps most CPU cores busy for seconds.
internal static unsafe class ClipEmbedCopy
{
    // Fastest and lightest first: everything on the GPU, then GPU encoding only, then all in software.
    static readonly (bool Gpu, bool Hardware, string Name)[] Ways = [(true, true, "GPU"), (false, true, "CPU decode, GPU encode"), (false, false, "software")];

    // Writes a fast-start MP4 to target and returns how it was made. Throws when Windows cannot decode or encode the clip.
    public static string Create(string mp4, string target, TimeSpan duration, long budget, CancellationToken cancel = default)
    {
        var raw = target + ".embed.mp4"; int bitrate = SharePolicy.EmbedVideoBitrate(duration, budget);
        Check(MFStartup(MfVersion, StartupLite), "Media Foundation startup");
        try
        {
            string way = "";
            for (int i = 0; ; i++)
            {
                try { Transcode(mp4, raw, bitrate, Ways[i].Gpu, Ways[i].Hardware, cancel); way = Ways[i].Name; break; }
                catch (Exception e) when (e is not OperationCanceledException && i < Ways.Length - 1) { File.Delete(raw); }
            }
            Mp4FastStart.Apply(raw, target);
            return way;
        }
        finally { MFShutdown(); if (File.Exists(raw)) File.Delete(raw); }
    }
    static void Transcode(string mp4, string output, int bitrate, bool gpu, bool hardware, CancellationToken cancel)
    {
        IntPtr reader = 0, writer = 0, attributes = 0, native = 0, decoded = 0, encoded = 0, audio = 0, device = 0, manager = 0;
        try
        {
            if (gpu)
            {
                // Media Foundation uses the device from several threads.
                Check(D3D11CreateDevice(0, HardwareDriver, 0, VideoSupport, 0, 0, SdkVersion, out device, 0, 0), "Direct3D device");
                var iid = Multithread; Check(Marshal.QueryInterface(device, in iid, out var protect), "Direct3D multithreading");
                try { ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Vtbl(protect)[5])(protect, 1); } finally { Marshal.Release(protect); }
                Check(MFCreateDXGIDeviceManager(out var token, out manager), "device manager");
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int>)Vtbl(manager)[7])(manager, device, token), "device manager reset");
            }
            Check(MFCreateAttributes(out attributes, 2), "attributes"); SetU32(attributes, AdvancedVideoProcessing, 1); if (gpu) SetUnknown(attributes, D3DManager, manager);
            Check(MFCreateSourceReaderFromURL(mp4, attributes, out reader), "Opening the clip"); Release(ref attributes);
            SelectStream(reader, AllStreams, false); SelectStream(reader, FirstVideo, true);
            Check(NativeType(reader, FirstVideo, out native), "video format");
            ulong size = GetU64(native, FrameSize), rate = GetU64(native, FrameRate);
            int sourceWidth = (int)(size >> 32), sourceHeight = (int)(uint)size; if (sourceWidth <= 0 || sourceHeight <= 0) throw new InvalidDataException("Clip has no video size.");
            double sourceFps = (uint)rate > 0 ? (double)(rate >> 32) / (uint)rate : 60;
            int height = Math.Min(SharePolicy.EmbedHeight(bitrate), sourceHeight) & ~1, width = (int)Math.Round((double)sourceWidth * height / sourceHeight) & ~1; uint fps = (uint)Math.Clamp(Math.Round(sourceFps), 1, 30);
            Release(ref native);

            // Decoder output: NV12 already scaled to the copy's size.
            decoded = VideoType(Nv12, width, height, rate);
            SetReaderType(reader, FirstVideo, decoded, "Decoding the clip");
            Check(MFCreateAttributes(out attributes, 3), "attributes"); SetU32(attributes, HardwareTransforms, hardware ? 1u : 0u); SetU32(attributes, DisableThrottling, 1); if (gpu) SetUnknown(attributes, D3DManager, manager);
            Check(MFCreateSinkWriterFromURL(output, 0, attributes, out writer), "Creating the copy");
            encoded = VideoType(H264, width, height, (ulong)fps << 32 | 1); SetU32(encoded, AvgBitrate, (uint)bitrate); SetU32(encoded, Profile, 100);
            var routes = new List<Route> { new(FirstVideo, AddStream(writer, encoded, "video stream"), Video: true) };
            Release(ref decoded); decoded = VideoType(Nv12, width, height, (ulong)fps << 32 | 1);
            SetWriterInput(writer, routes[0].Writer, decoded, "Encoding the copy");

            // The playback mix as is (AAC from the clip), found by name; a clip without audio just has none.
            var streams = AudioStreams(reader);
            if (ClipAudio.DefaultTrack(streams.Select(a => a.Name).ToList()) is int track && NativeType(reader, streams[track].Stream, out audio) >= 0)
            {
                uint stream = streams[track].Stream;
                SelectStream(reader, stream, true); SetReaderType(reader, stream, audio, "audio format");
                var sound = new Route(stream, AddStream(writer, audio, "audio stream"), Video: false);
                SetWriterInput(writer, sound.Writer, audio, "audio copy"); routes.Add(sound);
            }
            BeginWriting(writer);

            // Video frames above 30 fps are dropped evenly; the kept ones last one output frame each.
            long frame = 10_000_000 / fps, nextFrame = long.MinValue;
            Interleave(reader, writer, routes, (route, sample, time) =>
            {
                if (!route.Video) return SampleFate.Write;
                // 1 ms tolerance for timestamps that are not exact multiples of the source frame time.
                if (nextFrame != long.MinValue && time + 10_000 < nextFrame) return SampleFate.Drop;
                nextFrame = nextFrame == long.MinValue || nextFrame + frame < time ? time + frame : nextFrame + frame;
                SetSampleDuration(sample, frame); return SampleFate.Write;
            }, cancel);
            FinishWriting(writer);
        }
        finally
        {
            Release(ref audio); Release(ref encoded); Release(ref decoded); Release(ref native); Release(ref attributes); Release(ref writer); Release(ref reader); Release(ref manager); Release(ref device);
        }
    }
    static IntPtr VideoType(Guid subtype, int width, int height, ulong rate)
    {
        Check(MFCreateMediaType(out var type), "media type");
        SetGuid(type, MajorType, Video); SetGuid(type, Subtype, subtype); SetU64(type, FrameSize, (ulong)width << 32 | (uint)height);
        SetU64(type, FrameRate, rate); SetU64(type, PixelAspect, 1UL << 32 | 1); SetU32(type, Interlace, 2); // progressive
        return type;
    }
}
