using System.Runtime.InteropServices;
namespace MonoClip.Windows.Share;

// Windows Media Foundation through plain vtable calls: the published app is trimmed, which turns off .NET's
// built-in COM interop. Slots count IUnknown's three methods (IMFAttributes starts at 3).
internal static unsafe class Mf
{
    [DllImport("mfplat.dll")] public static extern int MFStartup(uint version, uint flags);
    [DllImport("mfplat.dll")] public static extern int MFShutdown();
    [DllImport("mfplat.dll")] public static extern int MFCreateAttributes(out IntPtr attributes, uint size);
    [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IntPtr type);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSourceReaderFromURL(string url, IntPtr attributes, out IntPtr reader);
    [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] public static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IntPtr writer);
    [DllImport("mfplat.dll")] public static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IntPtr manager);
    [DllImport("d3d11.dll")] public static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr featureLevels, uint levels, uint sdkVersion, out IntPtr device, IntPtr featureLevel, IntPtr context);
    [DllImport("ole32.dll")] public static extern int PropVariantClear(PropVariant* value);
    public const int HardwareDriver = 1; public const uint VideoSupport = 0x800, SdkVersion = 7;
    public const uint MfVersion = 0x00020070, StartupLite = 1, AllStreams = 0xFFFFFFFE, FirstVideo = 0xFFFFFFFC, FirstAudio = 0xFFFFFFFD, MediaSource = 0xFFFFFFFF, EndOfStream = 0x2, ReadError = 0x1;
    public static readonly Guid MajorType = new("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Subtype = new("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"),
        AvgBitrate = new("20332624-fb0d-4d9e-bd0d-cbf6786c102e"), Interlace = new("e2724bb8-e676-4806-b4b2-a8d6efb44ccd"),
        FrameSize = new("1652c33d-d6b2-4012-b834-72030849a37d"), FrameRate = new("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"),
        PixelAspect = new("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6"), Profile = new("ad76a80b-2d5c-4e0b-b375-64e520137036"),
        Video = new("73646976-0000-0010-8000-00AA00389B71"), Audio = new("73647561-0000-0010-8000-00AA00389B71"),
        Nv12 = new("3231564E-0000-0010-8000-00AA00389B71"), H264 = new("34363248-0000-0010-8000-00AA00389B71"),
        AdvancedVideoProcessing = new("0f81da2c-b537-4672-a8b2-a681b17307a3"), HardwareTransforms = new("a634a91c-822b-41b9-a494-4de4643612b0"),
        DisableThrottling = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f"), D3DManager = new("ec822da2-e1e9-4b29-a0d8-563c719f5269"),
        Multithread = new("9b7e4e00-342c-4106-a19f-4f2704f689f0"), StreamName = new("4f1b099d-d314-41e5-a781-7fefaa4c501f"),
        Duration = new("6c990d33-bb8e-477a-8598-0d5d96fcd88a"), BitsPerSample = new("f2deb57f-40fa-4764-aa33-ed4f2d1ff669");

    // PROPVARIANT: type tag, then the value 8 bytes in (24 bytes on x64).
    [StructLayout(LayoutKind.Explicit, Size = 24)] public struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public long Long; [FieldOffset(8)] public IntPtr Pointer; }
    public const ushort VtI8 = 20, VtUi8 = 21, VtLpwstr = 31;
    public static PropVariant Time(TimeSpan time) => new() { Type = VtI8, Long = time.Ticks };

    public static void** Vtbl(IntPtr com) => *(void***)com;
    // IMFAttributes: GetUINT32 = 7, GetUINT64 = 8, GetGUID = 10, SetUINT32 = 21, SetUINT64 = 22, SetGUID = 24, SetUnknown = 27.
    public static ulong GetU64(IntPtr attributes, Guid key) { ulong value; Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong*, int>)Vtbl(attributes)[8])(attributes, &key, &value), "media attribute"); return value; }
    public static Guid GetGuid(IntPtr attributes, Guid key) { Guid value; Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Vtbl(attributes)[10])(attributes, &key, &value), "media attribute"); return value; }
    public static bool HasU32(IntPtr attributes, Guid key) { uint value; return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint*, int>)Vtbl(attributes)[7])(attributes, &key, &value) >= 0; }
    public static void SetU32(IntPtr attributes, Guid key, uint value) => Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, int>)Vtbl(attributes)[21])(attributes, &key, value), "media attribute");
    public static void SetU64(IntPtr attributes, Guid key, ulong value) => Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, ulong, int>)Vtbl(attributes)[22])(attributes, &key, value), "media attribute");
    public static void SetGuid(IntPtr attributes, Guid key, Guid value) => Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, int>)Vtbl(attributes)[24])(attributes, &key, &value), "media attribute");
    public static void SetUnknown(IntPtr attributes, Guid key, IntPtr value) => Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr, int>)Vtbl(attributes)[27])(attributes, &key, value), "media attribute");

    // IMFSourceReader: SetStreamSelection = 4, GetNativeMediaType = 5, SetCurrentMediaType = 7, SetCurrentPosition = 8,
    // ReadSample = 9, GetPresentationAttribute = 12.
    public static void SelectStream(IntPtr reader, uint stream, bool on) => Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, int, int>)Vtbl(reader)[4])(reader, stream, on ? 1 : 0), "stream selection");
    public static int NativeType(IntPtr reader, uint stream, out IntPtr type) { IntPtr t; int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, int>)Vtbl(reader)[5])(reader, stream, 0, &t); type = hr >= 0 ? t : 0; return hr; }
    public static void SetReaderType(IntPtr reader, uint stream, IntPtr type, string step) => Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint*, IntPtr, int>)Vtbl(reader)[7])(reader, stream, null, type), step);
    public static void Seek(IntPtr reader, TimeSpan position) { var key = Guid.Empty; var value = Time(position); Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, PropVariant*, int>)Vtbl(reader)[8])(reader, &key, &value), "Seeking in the clip"); }
    // Returns false at the end of the stream; sample is 0 for gaps and format changes.
    public static bool ReadSample(IntPtr reader, uint stream, out IntPtr sample, out long time)
    {
        uint actual, flags; long t; IntPtr s;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint*, uint*, long*, IntPtr*, int>)Vtbl(reader)[9])(reader, stream, 0, &actual, &flags, &t, &s), "Reading the clip");
        if ((flags & ReadError) != 0) { if (s != 0) Marshal.Release(s); throw new InvalidDataException("The clip could not be read."); }
        if ((flags & EndOfStream) != 0) { if (s != 0) Marshal.Release(s); sample = 0; time = 0; return false; }
        sample = s; time = t; return true;
    }
    public static bool PresentationAttribute(IntPtr reader, uint stream, Guid key, out PropVariant value)
    {
        PropVariant v; bool ok = ((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, PropVariant*, int>)Vtbl(reader)[12])(reader, stream, &key, &v) >= 0; value = v; return ok;
    }
    // IMFSinkWriter: AddStream = 3, SetInputMediaType = 4, BeginWriting = 5, WriteSample = 6, Finalize = 11.
    public static uint AddStream(IntPtr writer, IntPtr type, string step) { uint index; Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint*, int>)Vtbl(writer)[3])(writer, type, &index), step); return index; }
    public static void SetWriterInput(IntPtr writer, uint stream, IntPtr type, string step) => Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, IntPtr, int>)Vtbl(writer)[4])(writer, stream, type, 0), step);
    public static void BeginWriting(IntPtr writer) => Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(writer)[5])(writer), "Starting the copy");
    public static void WriteSample(IntPtr writer, uint stream, IntPtr sample) => Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr, int>)Vtbl(writer)[6])(writer, stream, sample), "Writing the copy");
    public static void FinishWriting(IntPtr writer) => Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(writer)[11])(writer), "Finishing the copy");
    // IMFSample (after IMFAttributes' 30 methods): SetSampleTime = 36, SetSampleDuration = 38.
    public static void SetSampleTime(IntPtr sample, long time) => Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(sample)[36])(sample, time), "sample time");
    public static void SetSampleDuration(IntPtr sample, long duration) => Check(((delegate* unmanaged[Stdcall]<IntPtr, long, int>)Vtbl(sample)[38])(sample, duration), "frame duration");

    public static void Release(ref IntPtr com) { if (com != 0) Marshal.Release(com); com = 0; }
    public static void Check(int hr, string step) { if (hr < 0) throw new InvalidOperationException($"{step} failed (0x{hr:X8})."); }
}
