using System.Runtime.InteropServices;
namespace MonoClip.Windows.UI;

public sealed class HotkeyRegistration : NativeWindow, IDisposable
{
    readonly Action callback; int currentId; int nextId = 1; bool disposed;
    public Hotkey? Current { get; private set; }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    public HotkeyRegistration(Action action) { callback = action; CreateHandle(new CreateParams { Caption = "MonoClip hotkey", Parent = new IntPtr(-3) }); }
    public void Set(Hotkey hotkey)
    {
        ObjectDisposedException.ThrowIf(disposed, this); if (Current == hotkey) return;
        var id = nextId++; if (nextId >= 0xBFFF) nextId = 1;
        if (!RegisterHotKey(Handle, id, hotkey.Modifiers, (uint)hotkey.Key)) throw new InvalidOperationException("Diese Tastenkombination ist bereits von einer anderen App belegt.");
        if (currentId != 0) UnregisterHotKey(Handle, currentId); currentId = id; Current = hotkey;
    }
    protected override void WndProc(ref Message message) { if (message.Msg == 0x312 && message.WParam.ToInt32() == currentId) callback(); base.WndProc(ref message); }
    public void Dispose() { if (disposed) return; if (currentId != 0) UnregisterHotKey(Handle, currentId); DestroyHandle(); disposed = true; }
}
