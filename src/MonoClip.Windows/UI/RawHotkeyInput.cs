using System.Runtime.InteropServices;

namespace MonoClip.Windows.UI;

// One keyboard TLC target per process. Shared by registrations, never steals focus or
// suppresses legacy input. Only hotkey/modifier state is retained; no key history/logging.
internal sealed class RawHotkeyInput : NativeWindow, IDisposable
{
    static RawHotkeyInput? shared;
    event Action<ushort, ushort, ushort, int>? key;
    Action<ushort,ushort,ushort,int>[] handlers = [];
    void RefreshHandlers() => handlers = key?.GetInvocationList().Cast<Action<ushort,ushort,ushort,int>>().ToArray() ?? [];
    readonly IntPtr buffer = Marshal.AllocHGlobal(48);
    bool registered;
    [StructLayout(LayoutKind.Sequential)] struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] static extern int GetMessageTime();

    RawHotkeyInput()
    {
        CreateHandle(new CreateParams { Caption = "MonoClip raw hotkey", Parent = new IntPtr(-3) });
        registered = RegisterRawInputDevices(new[] { new Device { Page = 1, Usage = 6, Flags = 0x100, Target = Handle } }, 1, (uint)Marshal.SizeOf<Device>());
    }
    internal static IDisposable Subscribe(Action<ushort, ushort, ushort, int> action)
    {
        var owner = shared ??= new RawHotkeyInput(); owner.key += action; owner.RefreshHandlers();
        return new Subscription(owner, action);
    }
    sealed class Subscription(RawHotkeyInput owner, Action<ushort, ushort, ushort, int> action) : IDisposable
    {
        bool disposed;
        public void Dispose()
        {
            if(disposed) return; disposed = true; owner.key -= action; owner.RefreshHandlers();
            if(owner.key == null) { owner.Dispose(); shared = null; }
        }
    }
    protected override void WndProc(ref Message message)
    {
        try
        {
        if(message.Msg == 0xFF)
        {
            uint size = 48; uint header = (uint)(8 + 2 * IntPtr.Size);
            var read = GetRawInputData(message.LParam, 0x10000003, buffer, ref size, header);
            if(read != uint.MaxValue && read >= header + 16 && Marshal.ReadInt32(buffer) == 1)
            {
                var offset = (int)header;
                var scan = (ushort)Marshal.ReadInt16(buffer, offset);
                var flags = (ushort)Marshal.ReadInt16(buffer, offset + 2);
                var vk = (ushort)Marshal.ReadInt16(buffer, offset + 6);
                if(scan != 0xFF && vk is > 0 and < 255) Dispatch(vk, scan, flags, GetMessageTime());
            }
        }
        }
        finally { base.WndProc(ref message); } // Required foreground WM_INPUT cleanup, including failures.
    }
    void Dispatch(ushort vk, ushort scan, ushort flags, int time)
    {
        foreach(var handler in handlers)
        {
            try { handler(vk,scan,flags,time); }
            catch(Exception error) { System.Diagnostics.Trace.TraceError("MonoClip hotkey callback failed: {0}",error.GetType().Name); }
        }
    }
    public void Dispose()
    {
        if(registered) { RegisterRawInputDevices(new[] { new Device { Page = 1, Usage = 6, Flags = 1 } }, 1, (uint)Marshal.SizeOf<Device>()); registered = false; }
        DestroyHandle(); Marshal.FreeHGlobal(buffer);
    }
}
