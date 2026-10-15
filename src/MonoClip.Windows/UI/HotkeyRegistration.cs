using System.Runtime.InteropServices;
namespace MonoClip.Windows.UI;

public sealed class HotkeyRegistration : NativeWindow, IDisposable
{
    readonly Action callback;
    readonly IDisposable rawInput;
    readonly bool[] modifiers = new bool[8];
    int currentId, nextId = 1;
    bool disposed, keyHeld, pressHandled, enabled = true;
    int? rawTime;
    // WM_HOTKEY is queued ahead of ordinary input. Keep a small, fixed-size ledger of
    // chord event timestamps so several native notifications can precede their raw
    // counterparts without double-saving. This is not a keystroke history.
    readonly int[] handledTimes = new int[64];
    int handledCount, handledNext;
    public Hotkey? Current { get; private set; }
    // The settings hotkey recorder may disable dispatch without surrendering ownership.
    public bool Enabled { get => enabled; set { enabled = value; if(!value && keyHeld) pressHandled = true; } }
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern int GetMessageTime();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    public HotkeyRegistration(Action action)
    {
        callback = action;
        CreateHandle(new CreateParams { Caption = "MonoClip hotkey", Parent = new IntPtr(-3) });
        rawInput = RawHotkeyInput.Subscribe(RawKey);
    }
    public void Set(Hotkey hotkey)
    {
        ObjectDisposedException.ThrowIf(disposed, this); if(Current == hotkey) return;
        var id = nextId++; if(nextId >= 0xBFFF) nextId = 1;
        if(!RegisterHotKey(Handle, id, hotkey.Modifiers | Hotkey.NoRepeat, (uint)hotkey.Key))
            throw new InvalidOperationException("This key combination is already used by another app.");
        if(currentId != 0) UnregisterHotKey(Handle, currentId);
        currentId = id; Current = hotkey;
        // Seed only relevant state once; background raw delivery needs no timer/polling.
        int[] keys = { 0xA4, 0xA5, 0xA2, 0xA3, 0xA0, 0xA1, 0x5B, 0x5C };
        for(int i = 0; i < keys.Length; i++) modifiers[i] = GetAsyncKeyState(keys[i]) < 0;
        keyHeld = GetAsyncKeyState((int)hotkey.Key) < 0;
        pressHandled = keyHeld; rawTime = null; handledCount = handledNext = 0;
    }
    bool Recording => Application.OpenForms.Cast<Form>().OfType<SettingsForm>().Any(form => form.Visible && form.Handle == GetForegroundWindow() && RecorderFocused(form));
    static bool RecorderFocused(Control control)
    {
        if(control is TextBox && control.Focused && control.AccessibleName == "Clip hotkey") return true;
        foreach(Control child in control.Controls) if(child.ContainsFocus && RecorderFocused(child)) return true;
        return false;
    }
    void RawKey(ushort key, ushort scan, ushort flags, int time)
    {
        if(disposed || Current is not { } hotkey) return;
        bool up = (flags & 1) != 0;
        int side = key switch
        {
            0x12 => (flags & 2) != 0 ? 1 : 0,
            0x11 => (flags & 2) != 0 ? 3 : 2,
            0x10 => scan == 0x36 ? 5 : 4,
            0xA4 => 0, 0xA5 => 1, 0xA2 => 2, 0xA3 => 3,
            0xA0 => 4, 0xA1 => 5, 0x5B => 6, 0x5C => 7, _ => -1
        };
        if(side >= 0) { modifiers[side] = !up; return; }
        if(key != (ushort)hotkey.Key) return;
        if(up) { keyHeld = false; pressHandled = false; return; }
        if(keyHeld) return;
        keyHeld = true; rawTime = time;
        uint actual = 0;
        for(int i = 0; i < 4; i++) if(modifiers[i * 2] || modifiers[i * 2 + 1]) actual |= 1u << i;
        pressHandled = true; // A non-matching/suppressed press cannot become a save on repeat.
        if(AlreadyHandled(time) || actual != (hotkey.Modifiers & 15) || !Enabled || Recording) return;
        callback();
    }
    bool AlreadyHandled(int time)
    {
        for(int i = 0; i < handledCount; i++) if(handledTimes[i] == time) return true;
        handledTimes[handledNext] = time; handledNext = (handledNext + 1) % handledTimes.Length;
        handledCount = Math.Min(handledCount + 1, handledTimes.Length);
        return false;
    }
    void NativeHotkey(int time)
    {
        if(disposed || Current == null || keyHeld && pressHandled && rawTime == null) return;
        if(AlreadyHandled(time) || !Enabled || Recording) return;
        callback();
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg == 0x312 && message.WParam.ToInt32() == currentId) NativeHotkey(GetMessageTime());
        base.WndProc(ref message);
    }
    public void Dispose()
    {
        if(disposed) return; disposed = true;
        if(currentId != 0) UnregisterHotKey(Handle, currentId);
        rawInput.Dispose(); DestroyHandle();
    }
}
