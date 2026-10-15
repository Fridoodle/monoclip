using MonoClip.Windows.UI;

internal static class HotkeyFallbackTests
{
    [STAThread] public static int Main()
    {
        try
        {
            using var registration = new HotkeyRegistration(() => saves++);
            registration.Set(Hotkey.Parse("Ctrl+Alt+Shift+F11"));
            // Exercise the real registration's raw-input dispatch seam, not SendInput masquerading as hardware.
            Raw(registration, Keys.ControlKey, 0x1d, 0, 10);
            Raw(registration, Keys.Menu, 0x38, 0, 11);
            Raw(registration, Keys.ShiftKey, 0x2a, 0, 12);
            Raw(registration, Keys.F11, 0x57, 0, 13);
            Equal(1, saves, "raw input without WM_HOTKEY saves");
            Console.WriteLine("PASS raw input fallback");
            Raw(registration, Keys.F11, 0x57, 0, 14);
            Equal(1, saves, "repeat suppressed");
            Native(registration, 13); Equal(1, saves, "raw then native deduplicated");
            Raw(registration, Keys.F11, 0x57, 1, 15);
            Native(registration, 13); Equal(1, saves, "late native after release deduplicated");
            Native(registration, 20); Equal(2, saves, "native-only delivery works");
            Raw(registration, Keys.F11, 0x57, 0, 20); Equal(2, saves, "native then raw deduplicated");
            Raw(registration, Keys.F11, 0x57, 1, 21);
            Raw(registration, Keys.RWin, 0, 0, 22);
            Raw(registration, Keys.F11, 0x57, 0, 23); Equal(2, saves, "extra modifier rejected");
            Raw(registration, Keys.F11, 0x57, 1, 24);
            Raw(registration, Keys.RWin, 0, 1, 25);
            registration.Enabled = false;
            Raw(registration, Keys.F11, 0x57, 0, 26); Equal(2, saves, "disabled suppresses raw input");
            Native(registration, 26); Equal(2, saves, "disabled suppresses native input");
            registration.Enabled = true;
            Raw(registration, Keys.F11, 0x57, 0, 27); Equal(2, saves, "reenable while held does not save");
            Raw(registration, Keys.F11, 0x57, 1, 28);
            Raw(registration, Keys.ControlKey, 0x1d, 2, 29); // Right Ctrl down; left Ctrl remains down.
            Raw(registration, Keys.ControlKey, 0x1d, 1, 30); // Left Ctrl up.
            Raw(registration, Keys.F11, 0x57, 0, 31); Equal(3, saves, "right Ctrl survives left Ctrl release");
            Raw(registration, Keys.F11, 0x57, 1, 32);
            Raw(registration, Keys.Menu, 0x38, 2, 33);
            Raw(registration, Keys.Menu, 0x38, 1, 34);
            Raw(registration, Keys.ShiftKey, 0x36, 0, 35);
            Raw(registration, Keys.ShiftKey, 0x2a, 1, 36);
            Raw(registration, Keys.F11, 0x57, 0, 37); Equal(4, saves, "right Alt and Shift survive left release");
            Raw(registration, Keys.F11, 0x57, 1, 38);
            Raw(registration, Keys.ControlKey, 0x1d, 3, 39);
            Raw(registration, Keys.F11, 0x57, 0, 40); Equal(4, saves, "missing modifier rejected");
            Raw(registration, Keys.F11, 0x57, 1, 41);
            Raw(registration, Keys.ControlKey, 0x1d, 2, 42);
            Native(registration, 50); Native(registration, 60);
            Equal(6, saves, "two queued native presses");
            Raw(registration, Keys.F11, 0x57, 0, 50);
            Raw(registration, Keys.F11, 0x57, 1, 51);
            Raw(registration, Keys.F11, 0x57, 0, 60);
            Raw(registration, Keys.F11, 0x57, 1, 61);
            Equal(6, saves, "queued raw counterparts do not duplicate");
            registration.Dispose(); Raw(registration, Keys.F11, 0x57, 1, 70); Native(registration, 71);
            Equal(6, saves, "disposed registration does not dispatch");
            Console.WriteLine("PASS repeat, exact modifiers, left/right, both delivery orders, late native, enabled, disposal, queued delivery");
            var before = KeyboardTargets();
            using(var first = new HotkeyRegistration(() => { }))
            {
                int local = 0;
                using(var second = new HotkeyRegistration(() => local++))
                {
                    first.Set(Hotkey.Parse("Ctrl+Alt+Shift+F11")); second.Set(Hotkey.Parse("Ctrl+Alt+Shift+F12"));
                    var target = KeyboardTargets().Single();
                    if(target.Flags != 0x100 || target.Target == IntPtr.Zero) throw new Exception("keyboard INPUTSINK missing");
                    first.Dispose();
                    if(KeyboardTargets().Single().Target != target.Target) throw new Exception("disposing first removed shared raw target");
                    var held = typeof(HotkeyRegistration).GetField("keyHeld", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    held.SetValue(second, true);
                    typeof(HotkeyRegistration).GetField("pressHandled", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(second, true);
                    Native(second, 80); Equal(0, local, "initial held key ignored");
                }
            }
            if(KeyboardTargets().Length != before.Length) throw new Exception("raw target leaked after last dispose");
            Console.WriteLine("PASS native INPUTSINK registration, shared target ownership, cleanup, initial held guard");
            Recording();
            Boundary();
            return 0;
        }
        catch(Exception e) { Console.WriteLine("FAIL " + (e.InnerException ?? e).Message); return 1; }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    static void Recording()
    {
        var foreground = GetForegroundWindow(); int local = 0;
        using var form = new SettingsForm(new MonoClip.Core.AppSettings(), new FakeEngine(), _ => { }, () => { }, () => { });
        using var registration = new HotkeyRegistration(() => local++);
        registration.Set(Hotkey.Parse("Ctrl+Alt+Shift+F11"));
        try
        {
            form.Show(); SetForegroundWindow(form.Handle); Application.DoEvents();
            var recorder = (TextBox)typeof(SettingsForm).GetField("hotkey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(form)!;
            recorder.Focus(); Application.DoEvents();
            if(!recorder.Focused) throw new Exception("Cannot focus isolated hotkey recorder fixture");
            Raw(registration, Keys.LControlKey, 0, 0, 100);
            Raw(registration, Keys.LMenu, 0, 0, 101);
            Raw(registration, Keys.LShiftKey, 0, 0, 102);
            Raw(registration, Keys.F11, 0, 0, 103); Native(registration, 103);
            Equal(0, local, "recording suppresses both paths");
            SetForegroundWindow(foreground); Application.DoEvents();
            if(GetForegroundWindow()==form.Handle) throw new Exception("Cannot deactivate recorder fixture");
            Raw(registration, Keys.F11, 0, 1, 110);
            Raw(registration, Keys.F11, 0, 0, 111); Native(registration, 111);
            Equal(1, local, "visible inactive recorder does not suppress foreground game");
            form.Hide(); Application.DoEvents();
            Raw(registration, Keys.F11, 0, 0, 104); Equal(1, local, "focus change while held does not save");
            Raw(registration, Keys.F11, 0, 1, 105);
            Raw(registration, Keys.F11, 0, 0, 106); Equal(2, local, "next press after recording saves");
            Console.WriteLine("PASS focused settings recorder suppression and held focus edge");
        }
        finally { form.Hide(); SetForegroundWindow(foreground); }
    }
    static void Native(HotkeyRegistration registration, int time) => typeof(HotkeyRegistration).GetMethod("NativeHotkey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(registration, new object[] { time });
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetRegisteredRawInputDevices([System.Runtime.InteropServices.Out] Device[]? devices, ref uint count, uint size);
    static Device[] KeyboardTargets()
    {
        uint count = 0; uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Device>();
        if(GetRegisteredRawInputDevices(null, ref count, size) == uint.MaxValue) throw new Exception("Cannot query raw devices");
        if(count == 0) return Array.Empty<Device>();
        var devices = new Device[count];
        if(GetRegisteredRawInputDevices(devices, ref count, size) == uint.MaxValue) throw new Exception("Cannot read raw devices");
        return devices.Where(d => d.Page == 1 && d.Usage == 6).ToArray();
    }
    static void Boundary()
    {
        var type=typeof(HotkeyRegistration).Assembly.GetType("MonoClip.Windows.UI.RawHotkeyInput")!;
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var subscribe=type.GetMethod("Subscribe",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        int next=0;
        using var first=(IDisposable)subscribe.Invoke(null,new object[]{(Action<ushort,ushort,ushort,int>)((_,_,_,_)=>throw new InvalidOperationException("fixture"))})!;
        using var second=(IDisposable)subscribe.Invoke(null,new object[]{(Action<ushort,ushort,ushort,int>)((_,_,_,_)=>next++)})!;
        var shared=type.GetField("shared",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var dispatch=type.GetMethod("Dispatch",flags)??throw new Exception("raw callback exception boundary absent");
        dispatch.Invoke(shared,new object[]{(ushort)Keys.F11,(ushort)0,(ushort)0,200});
        Equal(1,next,"throwing subscriber does not suppress later subscribers");
        var body=type.GetMethod("WndProc",flags)!.GetMethodBody()!;
        if(!body.ExceptionHandlingClauses.Any(c=>c.Flags==System.Reflection.ExceptionHandlingClauseOptions.Finally))throw new Exception("WM_INPUT default cleanup lacks finally");
        Console.WriteLine("PASS callback exception isolation and mandatory default cleanup");
    }
    static int saves;
    static void Raw(HotkeyRegistration registration, Keys key, ushort scan, ushort flags, int time)
    {
        var method = typeof(HotkeyRegistration).GetMethod("RawKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if(method == null) throw new Exception("raw input fallback is absent");
        method.Invoke(registration, new object[] { (ushort)key, scan, flags, time });
    }
    static void Equal(int expected, int actual, string name) { if(expected != actual) throw new Exception($"{name}: expected {expected}, got {actual}"); }
}
