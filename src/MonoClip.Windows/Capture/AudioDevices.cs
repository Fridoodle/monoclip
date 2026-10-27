using System.Runtime.InteropServices;
namespace MonoClip.Windows.Capture;

// Windows' audio devices (Core Audio): the lists for the settings and a live level for their preview. "default" is the
// Windows default device as OBS uses it (the communications microphone, the default output) and follows Windows.
internal static unsafe class AudioDevices
{
    public const string Default = "default";
    const uint AllContexts = 23, Active = 1; const int Render = 0, Capture = 1, Console = 0, Communications = 2;
    static readonly Guid EnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E"), EnumeratorId = new("A95664D2-9614-4F35-A746-DE8DB63617E6"),
        MeterId = new("C02216F6-8C67-4B5B-9D00-D008E73E0064"), ClientId = new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), FriendlyName = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
    [DllImport("ole32.dll")] static extern int CoCreateInstance(in Guid clsid, IntPtr outer, uint context, in Guid iid, out IntPtr result);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr memory);
    [DllImport("ole32.dll")] static extern int PropVariantClear(byte* value);
    static void** V(IntPtr com) => *(void***)com;
    static void Release(IntPtr com) { if (com != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)V(com)[2])(com); }
    static IntPtr Enumerator() => CoCreateInstance(EnumeratorClass, IntPtr.Zero, AllContexts, EnumeratorId, out var e) >= 0 ? e : IntPtr.Zero;

    // Connected devices as (id, name), by name. input: microphones; otherwise outputs (what "desktop audio" records).
    public static List<(string Id, string Name)> List(bool input)
    {
        var result = new List<(string Id, string Name)>();
        IntPtr enumerator = Enumerator(), collection = IntPtr.Zero; if (enumerator == IntPtr.Zero) return result;
        try
        {
            if (((delegate* unmanaged[Stdcall]<IntPtr, int, uint, IntPtr*, int>)V(enumerator)[3])(enumerator, input ? Capture : Render, Active, &collection) < 0) return result;
            uint count; if (((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)V(collection)[3])(collection, &count) < 0) return result;
            for (uint i = 0; i < count; i++)
            {
                IntPtr device;
                if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)V(collection)[4])(collection, i, &device) < 0) continue;
                try { if (Id(device) is { } id) result.Add((id, Name(device) ?? id)); } finally { Release(device); }
            }
        }
        finally { Release(collection); Release(enumerator); }
        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    static string? Id(IntPtr device)
    {
        IntPtr text; if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)V(device)[5])(device, &text) < 0) return null;
        try { return Marshal.PtrToStringUni(text); } finally { CoTaskMemFree(text); }
    }
    static string? Name(IntPtr device)
    {
        IntPtr store; if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)V(device)[4])(device, 0, &store) < 0) return null;
        try
        {
            var key = new PropertyKey { Format = FriendlyName, Id = 14 }; var value = stackalloc byte[32]; // PROPVARIANT; VT_LPWSTR = 31
            if (((delegate* unmanaged[Stdcall]<IntPtr, PropertyKey*, byte*, int>)V(store)[5])(store, &key, value) < 0) return null;
            try { return *(ushort*)value == 31 ? Marshal.PtrToStringUni(*(IntPtr*)(value + 8)) : null; } finally { PropVariantClear(value); }
        }
        finally { Release(store); }
    }
    static IntPtr Device(IntPtr enumerator, string id, bool input)
    {
        IntPtr device;
        if (id == Default) return ((delegate* unmanaged[Stdcall]<IntPtr, int, int, IntPtr*, int>)V(enumerator)[4])(enumerator, input ? Capture : Render, input ? Communications : Console, &device) >= 0 ? device : IntPtr.Zero;
        fixed (char* text = id) return ((delegate* unmanaged[Stdcall]<IntPtr, char*, IntPtr*, int>)V(enumerator)[5])(enumerator, text, &device) >= 0 ? device : IntPtr.Zero;
    }
    static IntPtr Activate(IntPtr device, Guid iid)
    {
        IntPtr result; return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, IntPtr, IntPtr*, int>)V(device)[3])(device, &iid, AllContexts, IntPtr.Zero, &result) >= 0 ? result : IntPtr.Zero;
    }

    // The live peak (0–1) of one device for the settings' preview; 0 when it cannot be read. A microphone only reports a
    // level while something records it, so the meter keeps a shared stream of its own open (its data is never read).
    public sealed class Meter : IDisposable
    {
        IntPtr meter, client;
        public Meter(string id, bool input)
        {
            IntPtr enumerator = Enumerator(), device = IntPtr.Zero; if (enumerator == IntPtr.Zero) return;
            try
            {
                device = Device(enumerator, id, input); if (device == IntPtr.Zero) return;
                meter = Activate(device, MeterId);
                if (!input || (client = Activate(device, ClientId)) == IntPtr.Zero) return;
                IntPtr format; if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)V(client)[8])(client, &format) < 0) return;
                try
                {
                    // Shared mode, 200 ms buffer; then Start.
                    if (((delegate* unmanaged[Stdcall]<IntPtr, int, uint, long, long, IntPtr, Guid*, int>)V(client)[3])(client, 0, 0, 2_000_000, 0, format, null) >= 0)
                        ((delegate* unmanaged[Stdcall]<IntPtr, int>)V(client)[10])(client);
                }
                finally { CoTaskMemFree(format); }
            }
            finally { Release(device); Release(enumerator); }
        }
        public float Peak
        {
            get { float peak; return meter != IntPtr.Zero && ((delegate* unmanaged[Stdcall]<IntPtr, float*, int>)V(meter)[3])(meter, &peak) >= 0 ? Math.Clamp(peak, 0, 1) : 0; }
        }
        public void Dispose()
        {
            if (client != IntPtr.Zero) { ((delegate* unmanaged[Stdcall]<IntPtr, int>)V(client)[11])(client); Release(client); client = IntPtr.Zero; }
            Release(meter); meter = IntPtr.Zero;
        }
    }
}
