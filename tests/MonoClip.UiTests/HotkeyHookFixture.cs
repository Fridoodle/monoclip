using System.Runtime.InteropServices;

// Keep the test hook's installing thread inside a native GetMessage loop. The
// production application never installs a keyboard hook.
internal sealed class HotkeyHookFixture : IDisposable
{
    internal delegate IntPtr Procedure(int code, IntPtr w, IntPtr l);
    readonly Procedure procedure;
    readonly Thread thread;
    readonly ManualResetEventSlim ready = new();
    uint threadId;
    IntPtr hook;
    [StructLayout(LayoutKind.Sequential)] struct Msg { public IntPtr Window; public uint Message; public UIntPtr W; public IntPtr L; public uint Time; public int X, Y; public uint Private; }
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int kind, Procedure proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern int GetMessage(out Msg message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint message, UIntPtr w, IntPtr l);
    internal HotkeyHookFixture(Procedure procedure)
    {
        this.procedure = procedure;
        thread = new Thread(Run) { IsBackground = true };
        thread.Start();
        if(!ready.Wait(5000) || hook == IntPtr.Zero) throw new Exception("Dedicated keyboard hook installation failed");
    }
    void Run()
    {
        threadId = GetCurrentThreadId();
        hook = SetWindowsHookEx(13, procedure, GetModuleHandle(null), 0);
        ready.Set();
        if(hook == IntPtr.Zero) return;
        try { while(GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { } }
        finally { UnhookWindowsHookEx(hook); }
    }
    public void Dispose()
    {
        PostThreadMessage(threadId, 0x12, UIntPtr.Zero, IntPtr.Zero);
        if(!thread.Join(5000)) throw new Exception("Keyboard hook thread did not stop");
        ready.Dispose(); GC.KeepAlive(procedure);
    }
}
