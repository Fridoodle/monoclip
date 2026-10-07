using System.Runtime.InteropServices;
using MonoClip.Windows.UI;

internal static class HotkeyDeliveryTests
{
    sealed class HotkeyHarnessUnavailableException(string message) : Exception(message);
    static readonly UIntPtr Tag = new(0x4D434854);
    [StructLayout(LayoutKind.Sequential)] struct HookKey { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] struct InputUnion { [FieldOffset(0)] public Keyboard Keyboard; }
    [StructLayout(LayoutKind.Sequential)] struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern uint SendInput(uint count, Input[] inputs, int size);
    [STAThread] public static int Main(string[] args)
    {
        try { if(args.Contains("--physical")) Physical(); else Delivery(args.Contains("--blocked-expect-delivery")); Console.WriteLine("PASS native delivery harness"); return 0; }
        catch(HotkeyHarnessUnavailableException e) { Console.WriteLine("SKIP " + e.Message); return 2; }
        catch(Exception e) { Console.WriteLine("FAIL " + e.Message); return 1; }
    }
    // Explicit opt-in only: no injected input and no foreground change. This validates
    // hardware WM_INPUT delivery under a consuming hook, not capture/LoL itself.
    static void Physical()
    {
        int saves = 0, consumed = 0; bool suppressRelease = false;
        HotkeyHookFixture.Procedure proc = (code, w, l) => {
            if(code >= 0)
            {
                var k = Marshal.PtrToStructure<HookKey>(l);
                bool up = (k.Flags & 0x80) != 0;
                if(k.Key == (uint)Keys.F11 && (k.Flags & 0x10) == 0 &&
                   (suppressRelease || !up && GetAsyncKeyState(0x11) < 0 && GetAsyncKeyState(0x12) < 0 && GetAsyncKeyState(0x10) < 0))
                {
                    suppressRelease = !up; consumed++; return new IntPtr(1);
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, w, l);
        };
        using var registration = new HotkeyRegistration(() => saves++);
        registration.Set(Hotkey.Parse("Ctrl+Alt+Shift+F11"));
        using var hook = new HotkeyHookFixture(proc);
        Console.WriteLine("Within 20 seconds press/release physical Ctrl+Alt+Shift+F11 ONCE. Only this test chord is consumed.");
        var until = Environment.TickCount64 + 20000;
        while(Environment.TickCount64 < until && (Volatile.Read(ref consumed) < 2 || Volatile.Read(ref suppressRelease))) { Application.DoEvents(); Thread.Sleep(1); }
        Pump();
        Console.WriteLine($"Physical input experiment: consumed={consumed}, saves={saves}");
        if(consumed != 2 || saves != 1) throw new Exception("Physical consuming-hook fallback test incomplete or failed");
    }
    static void Pump() { var until = Environment.TickCount64 + 150; while(Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(1); } }
    static void Press()
    {
        var keys = new[] { Keys.ControlKey, Keys.Menu, Keys.ShiftKey, Keys.F11 };
        // SendInput can wait on low-level hook delivery; keep the installing STA
        // pumping while a worker injects, otherwise the test can time out its own hook.
        var injection = Task.Run(() => {
            var held = new HashSet<Keys>();
            try
            {
                foreach(var k in keys)
                {
                    if(SendInput(1, new[] { Make(k, false) }, Marshal.SizeOf<Input>()) != 1) throw new Exception("SendInput blocked");
                    held.Add(k); Thread.Sleep(20);
                }
                foreach(var k in keys.Reverse())
                {
                    if(SendInput(1, new[] { Make(k, true) }, Marshal.SizeOf<Input>()) != 1) throw new Exception("SendInput blocked");
                    held.Remove(k); Thread.Sleep(20);
                }
            }
            finally { foreach(var k in held) SendInput(1, new[] { Make(k, true) }, Marshal.SizeOf<Input>()); }
        });
        while(!injection.IsCompleted) { Application.DoEvents(); Thread.Sleep(1); }
        injection.GetAwaiter().GetResult(); Pump();
    }
    static Input Make(Keys key, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Key = (ushort)key, Flags = up ? 2u : 0, Extra = Tag } } };
    static void Delivery(bool expectBlockedDelivery)
    {
        foreach(var k in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, (int)Keys.F11 })
            if(GetAsyncKeyState(k) < 0) throw new Exception("SKIP: user holds a modifier/test key; no input injected");
        Console.WriteLine($"INPUT size={Marshal.SizeOf<Input>()}, data offset={Marshal.OffsetOf<Input>(nameof(Input.Data))}");
        var foreground = GetForegroundWindow();
        using var fixture = new Form { Text = "MonoClip isolated hotkey test", Width = 250, Height = 100, ShowInTaskbar = false };
        HotkeyHookFixture? hook = null;
        int count = 0, consumed = 0, observed = 0; bool consume = false;
        HotkeyHookFixture.Procedure proc = (code, w, l) => {
            if(code >= 0) { var key = Marshal.PtrToStructure<HookKey>(l); if(key.Extra == Tag) { Interlocked.Increment(ref observed); if(Volatile.Read(ref consume) && key.Key == (uint)Keys.F11) { Interlocked.Increment(ref consumed); return new IntPtr(1); } } }
            return CallNextHookEx(IntPtr.Zero, code, w, l);
        };
        try
        {
            fixture.Show(); SetForegroundWindow(fixture.Handle); Pump();
            using var registration = new HotkeyRegistration(() => count++);
            registration.Set(Hotkey.Parse("Ctrl+Alt+Shift+F11"));
            hook = new HotkeyHookFixture(proc);
            Press(); if(count != 1) throw new Exception($"Baseline expected one save, got {count}; observed tagged events={observed}");
            hook.Dispose(); hook = null;
            consume = true; count = 0; observed = 0;
            hook = new HotkeyHookFixture(proc);
            Press();
            Console.WriteLine($"Native experiment: baseline=1, hookConsumed={consumed}, blockedDelivery={count}, observedTagged={observed}");
            if(observed == 0) throw new HotkeyHarnessUnavailableException("Installed hook received no tagged input; consuming-hook experiment unavailable, not a successful fallback test.");
            if(consumed != 2) throw new Exception("Fixture did not consume both target transitions");
            if(expectBlockedDelivery && count != 1) throw new Exception($"Consumed hotkey expected one save, got {count}");
            if(!expectBlockedDelivery && count != 0) throw new Exception("RegisterHotKey suppression hypothesis disproved");
        }
        finally { try { hook?.Dispose(); } finally { fixture.Hide(); SetForegroundWindow(foreground); } }
    }
}
