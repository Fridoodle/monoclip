using MonoClip.Core;
using System.Runtime.InteropServices;
namespace MonoClip.Windows.Share;

// Smaller copy of a large clip for chat embeds: Discord shows a player only when it can download the whole
// og:video within a few seconds, which a home upload cannot do for a full-quality clip. Windows Media
// Foundation decodes the clip, scales it (SharePolicy.EmbedHeight, at most 30 fps) and re-encodes it as
// H.264 at a bitrate that keeps the copy near the given size budget. The first audio track is copied unchanged.
// It may run right after a clip while a game is still running: decoding, scaling and encoding all happen on
// the GPU (Direct3D 11) when possible, because the software path keeps most CPU cores busy for seconds.
internal static unsafe class ClipEmbedCopy
{
    [DllImport("mfplat.dll")] static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] static extern int MFShutdown();
    [DllImport("mfplat.dll")] static extern int MFCreateAttributes(out IntPtr attributes, uint size);
    [DllImport("mfplat.dll")] static extern int MFCreateMediaType(out IntPtr type);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IntPtr writer);
    [DllImport("mfplat.dll")] static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IntPtr manager);
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint levels, uint sdkVersion, out IntPtr device, IntPtr featureLevel, IntPtr context);
    const int HardwareDriver = 1; const uint VideoSupport = 0x800, SdkVersion = 7;
    const uint MfVersion = 0x00020070, StartupLite = 1, AllStreams = 0xFFFFFFFE, FirstVideo = 0xFFFFFFFC, FirstAudio = 0xFFFFFFFD, EndOfStream = 0x2, ReadError = 0x1;
    static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"),
        AvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e"), Interlace = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd"),
        FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d"), FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"),
        PixelAspect = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6"), Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036"),
        Video = new("73646976-0000-0010-8000-00AA00389B71"), Nv12 = new("3231564E-0000-0010-8000-00AA00389B71"), H264 = new("34363248-0000-0010-8000-00AA00389B71"),
        AdvancedVideoProcessing = new("0f81da2c-b537-4672-a8b2-a681b17307a3"), HardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0"),
        DisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f"), D3DManager = new("ec822da2-e1e9-4b29-a0d8-563c719f5269"),
        Multithread = new("9b7e4e00-342c-4106-a19f-4f2704f689f0");
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
    // A selected reader stream and its writer stream, with the next sample waiting to be written.
    sealed class Track { public uint Reader, Writer; public bool Video, Done; public IntPtr Sample; public long Time; }
    static void Transcode(string mp4, string output, int bitrate, bool gpu, bool hardware, CancellationToken cancel)
    {
        IntPtr reader = 0, writer = 0, attributes = 0, native = 0, decoded = 0, encoded = 0, audio = 0, device = 0, manager = 0; var tracks = new List<Track>();
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
            Check(Reader(reader, 4)(reader, AllStreams, 0), "stream selection"); Check(Reader(reader, 4)(reader, FirstVideo, 1), "video selection");
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, int>)Vtbl(reader)[5])(reader, FirstVideo, 0, &native), "video format");
            ulong size = GetU64(native, FrameSize), rate = GetU64(native, FrameRate);
            int sourceWidth = (int)(size >> 32), sourceHeight = (int)(uint)size; if (sourceWidth <= 0 || sourceHeight <= 0) throw new InvalidDataException("Clip has no video size.");
            double sourceFps = (uint)rate > 0 ? (double)(rate >> 32) / (uint)rate : 60;
            int height = Math.Min(SharePolicy.EmbedHeight(bitrate), sourceHeight) & ~1, width = (int)Math.Round((double)sourceWidth * height / sourceHeight) & ~1; uint fps = (uint)Math.Clamp(Math.Round(sourceFps), 1, 30);
            Release(ref native);

            // Decoder output: NV12 already scaled to the copy's size.
            decoded = VideoType(Nv12, width, height, rate);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint*, IntPtr, int>)Vtbl(reader)[7])(reader, FirstVideo, null, decoded), "Decoding the clip");
            Check(MFCreateAttributes(out attributes, 3), "attributes"); SetU32(attributes, HardwareTransforms, hardware ? 1u : 0u); SetU32(attributes, DisableThrottling, 1); if (gpu) SetUnknown(attributes, D3DManager, manager);
            Check(MFCreateSinkWriterFromURL(output, 0, attributes, out writer), "Creating the copy");
            encoded = VideoType(H264, width, height, (ulong)fps << 32 | 1); SetU32(encoded, AvgBitrate, (uint)bitrate); SetU32(encoded, Profile, 100);
            uint index; Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, int>)Vtbl(writer)[3])(writer, encoded, &index), "video stream");
            var video = new Track { Reader = FirstVideo, Writer = index, Video = true }; tracks.Add(video);
            Release(ref decoded); decoded = VideoType(Nv12, width, height, (ulong)fps << 32 | 1);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Vtbl(writer)[4])(writer, video.Writer, decoded, 0), "Encoding the copy");

            // The first audio track as is (AAC from the clip); a clip without audio just has none.
            if (((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, int>)Vtbl(reader)[5])(reader, FirstAudio, 0, &audio) >= 0)
            {
                Check(Reader(reader, 4)(reader, FirstAudio, 1), "audio selection");
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint*, IntPtr, int>)Vtbl(reader)[7])(reader, FirstAudio, null, audio), "audio format");
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, int>)Vtbl(writer)[3])(writer, audio, &index), "audio stream");
                var sound = new Track { Reader = FirstAudio, Writer = index };
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Vtbl(writer)[4])(writer, sound.Writer, audio, 0), "audio copy");
                tracks.Add(sound);
            }
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(writer)[5])(writer), "Starting the copy");

            // Interleave by time so a player can stream the copy from the start. Video frames above
            // 30 fps are dropped evenly; the kept ones last one output frame each.
            long frame = 10_000_000 / fps, nextFrame = long.MinValue;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                foreach (var t in tracks)
                    while (!t.Done && t.Sample == 0)
                    {
                        uint actual, flags; long time; IntPtr sample;
                        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)Vtbl(reader)[9])(reader, t.Reader, 0, &actual, &flags, &time, &sample), "Reading the clip");
                        if ((flags & ReadError) != 0) throw new InvalidDataException("The clip could not be decoded.");
                        if ((flags & EndOfStream) != 0) { t.Done = true; if (sample != 0) Marshal.Release(sample); break; }
                        if (sample == 0) continue;
                        if (t.Video)
                        {
                            // 1 ms tolerance for timestamps that are not exact multiples of the source frame time.
                            if (nextFrame != long.MinValue && time + 10_000 < nextFrame) { Marshal.Release(sample); continue; }
                            nextFrame = nextFrame == long.MinValue || nextFrame + frame < time ? time + frame : nextFrame + frame;
                            Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(sample)[38])(sample, frame), "frame duration");
                        }
                        t.Sample = sample; t.Time = time;
                    }
                var next = tracks.Where(t => t.Sample != 0).MinBy(t => t.Time);
                if (next == null) break;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, int>)Vtbl(writer)[6])(writer, next.Writer, next.Sample), "Writing the copy");
                Release(ref next.Sample);
            }
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(writer)[11])(writer), "Finishing the copy");
        }
        finally
        {
            foreach (var t in tracks) Release(ref t.Sample);
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
    static void** Vtbl(IntPtr com) => *(void***)com;
    static delegate* unmanaged[Stdcall]<IntPtr, uint, int, int> Reader(IntPtr reader, int slot) => (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int>)Vtbl(reader)[slot];
    // IMFAttributes: GetUINT64 = 8, SetUINT32 = 21, SetUINT64 = 22, SetGUID = 24, SetUnknown = 27.
    static ulong GetU64(IntPtr attributes, Guid key) { ulong value; Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong*, int>)Vtbl(attributes)[8])(attributes, &key, &value), "media attribute"); return value; }
    static void SetU32(IntPtr attributes, Guid key, uint value) => Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Vtbl(attributes)[21])(attributes, &key, value), "media attribute");
    static void SetU64(IntPtr attributes, Guid key, ulong value) => Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)Vtbl(attributes)[22])(attributes, &key, value), "media attribute");
    static void SetGuid(IntPtr attributes, Guid key, Guid value) => Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Vtbl(attributes)[24])(attributes, &key, &value), "media attribute");
    static void SetUnknown(IntPtr attributes, Guid key, IntPtr value) => Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr, int>)Vtbl(attributes)[27])(attributes, &key, value), "media attribute");
    static void Release(ref IntPtr com) { if (com != 0) Marshal.Release(com); com = 0; }
    static void Check(int hr, string step) { if (hr < 0) throw new InvalidOperationException($"{step} failed (0x{hr:X8})."); }
}
