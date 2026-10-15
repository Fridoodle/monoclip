using System.Reflection;
using System.Runtime.InteropServices;
using MonoClip.Windows.UI;
using static UiTest;

// Startup object for -p:HotkeyTestStartup=HotkeyFallbackTests. Feeds made-up key events straight into
// HotkeyRegistration's private raw-input (RawKey) and WM_HOTKEY (NativeHotkey) handlers.
// Raw arguments: virtual key, scan code, RI_KEY_* flags (1 = key up, 2 = right-hand Ctrl/Alt), event time.
internal static class HotkeyFallbackTests
{
    static int saves;

    [STAThread]
    public static int Main()
    {
        try
        {
            using var registration = new HotkeyRegistration(() => saves++);
            registration.Set(Hotkey.Parse("Ctrl+Alt+Shift+F11"));
            RawWithoutNative(registration);
            DeliveryRules(registration);
            SharedRawTarget();
            Recording();
            Boundary();
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine("FAIL " + (e.InnerException ?? e).Message);
            return 1;
        }
    }

    static void RawWithoutNative(HotkeyRegistration registration)
    {
        // Exercise the real registration's raw-input dispatch seam, not SendInput masquerading as hardware.
        Raw(registration, Keys.ControlKey, 0x1d, 0, 10);
        Raw(registration, Keys.Menu, 0x38, 0, 11);
        Raw(registration, Keys.ShiftKey, 0x2a, 0, 12);
        Raw(registration, Keys.F11, 0x57, 0, 13);
        Equal(1, saves, "raw input without WM_HOTKEY saves");
        Console.WriteLine("PASS raw input fallback");
    }

    // Continues from RawWithoutNative: Ctrl+Alt+Shift are still held and F11 is down.
    static void DeliveryRules(HotkeyRegistration registration)
    {
        Raw(registration, Keys.F11, 0x57, 0, 14);
        Equal(1, saves, "repeat suppressed");
        Native(registration, 13);
        Equal(1, saves, "raw then native deduplicated");
        Raw(registration, Keys.F11, 0x57, 1, 15);
        Native(registration, 13);
        Equal(1, saves, "late native after release deduplicated");
        Native(registration, 20);
        Equal(2, saves, "native-only delivery works");
        Raw(registration, Keys.F11, 0x57, 0, 20);
        Equal(2, saves, "native then raw deduplicated");
        Raw(registration, Keys.F11, 0x57, 1, 21);

        Raw(registration, Keys.RWin, 0, 0, 22);
        Raw(registration, Keys.F11, 0x57, 0, 23);
        Equal(2, saves, "extra modifier rejected");
        Raw(registration, Keys.F11, 0x57, 1, 24);
        Raw(registration, Keys.RWin, 0, 1, 25);

        registration.Suppressed = () => true;
        Raw(registration, Keys.F11, 0x57, 0, 26);
        Equal(2, saves, "suppressed raw input");
        Native(registration, 26);
        Equal(2, saves, "suppressed native input");
        registration.Suppressed = () => false;
        Raw(registration, Keys.F11, 0x57, 0, 27);
        Equal(2, saves, "lifting suppression while held does not save");
        Raw(registration, Keys.F11, 0x57, 1, 28);

        Raw(registration, Keys.ControlKey, 0x1d, 2, 29); // Right Ctrl down; left Ctrl remains down.
        Raw(registration, Keys.ControlKey, 0x1d, 1, 30); // Left Ctrl up.
        Raw(registration, Keys.F11, 0x57, 0, 31);
        Equal(3, saves, "right Ctrl survives left Ctrl release");
        Raw(registration, Keys.F11, 0x57, 1, 32);
        Raw(registration, Keys.Menu, 0x38, 2, 33);
        Raw(registration, Keys.Menu, 0x38, 1, 34);
        Raw(registration, Keys.ShiftKey, 0x36, 0, 35);
        Raw(registration, Keys.ShiftKey, 0x2a, 1, 36);
        Raw(registration, Keys.F11, 0x57, 0, 37);
        Equal(4, saves, "right Alt and Shift survive left release");
        Raw(registration, Keys.F11, 0x57, 1, 38);
        Raw(registration, Keys.ControlKey, 0x1d, 3, 39);
        Raw(registration, Keys.F11, 0x57, 0, 40);
        Equal(4, saves, "missing modifier rejected");
        Raw(registration, Keys.F11, 0x57, 1, 41);
        Raw(registration, Keys.ControlKey, 0x1d, 2, 42);

        Native(registration, 50);
        Native(registration, 60);
        Equal(6, saves, "two queued native presses");
        Raw(registration, Keys.F11, 0x57, 0, 50);
        Raw(registration, Keys.F11, 0x57, 1, 51);
        Raw(registration, Keys.F11, 0x57, 0, 60);
        Raw(registration, Keys.F11, 0x57, 1, 61);
        Equal(6, saves, "queued raw counterparts do not duplicate");

        registration.Dispose();
        Raw(registration, Keys.F11, 0x57, 1, 70);
        Native(registration, 71);
        Equal(6, saves, "disposed registration does not dispatch");
        Console.WriteLine("PASS repeat, exact modifiers, left/right, both delivery orders, late native, suppression, disposal, queued delivery");
    }

    static void SharedRawTarget()
    {
        var before = KeyboardTargets();
        using (var first = new HotkeyRegistration(() => { }))
        {
            int local = 0;
            using (var second = new HotkeyRegistration(() => local++))
            {
                first.Set(Hotkey.Parse("Ctrl+Alt+Shift+F11"));
                second.Set(Hotkey.Parse("Ctrl+Alt+Shift+F12"));
                var target = KeyboardTargets().Single();
                if (target.Flags != 0x100 || target.Target == IntPtr.Zero) throw new Exception("keyboard INPUTSINK missing");
                first.Dispose();
                if (KeyboardTargets().Single().Target != target.Target) throw new Exception("disposing first removed shared raw target");
                // As if the key was already held when the hotkey was registered.
                SetField(second, "keyHeld", true);
                SetField(second, "pressHandled", true);
                Native(second, 80);
                Equal(0, local, "initial held key ignored");
            }
        }
        if (KeyboardTargets().Length != before.Length) throw new Exception("raw target leaked after last dispose");
        Console.WriteLine("PASS native INPUTSINK registration, shared target ownership, cleanup, initial held guard");
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);

    static void Recording()
    {
        var foreground = GetForegroundWindow();
        int local = 0;
        using var form = NewSettingsForm();
        // Wired as the tray wires it: the settings window says when it records a hotkey.
        using var registration = new HotkeyRegistration(() => local++) { Suppressed = () => form.RecordingHotkey };
        registration.Set(Hotkey.Parse("Ctrl+Alt+Shift+F11"));
        try
        {
            form.Show();
            SetForegroundWindow(form.Handle);
            Application.DoEvents();
            var recorder = Field<TextBox>(form, "hotkey");
            recorder.Focus();
            Application.DoEvents();
            if (!recorder.Focused) throw new Exception("Cannot focus isolated hotkey recorder fixture");
            Raw(registration, Keys.LControlKey, 0, 0, 100);
            Raw(registration, Keys.LMenu, 0, 0, 101);
            Raw(registration, Keys.LShiftKey, 0, 0, 102);
            Raw(registration, Keys.F11, 0, 0, 103);
            Native(registration, 103);
            Equal(0, local, "recording suppresses both paths");

            SetForegroundWindow(foreground);
            Application.DoEvents();
            if (GetForegroundWindow() == form.Handle) throw new Exception("Cannot deactivate recorder fixture");
            Raw(registration, Keys.F11, 0, 1, 110);
            Raw(registration, Keys.F11, 0, 0, 111);
            Native(registration, 111);
            Equal(1, local, "visible inactive recorder does not suppress foreground game");

            form.Hide();
            Application.DoEvents();
            Raw(registration, Keys.F11, 0, 0, 104);
            Equal(1, local, "focus change while held does not save");
            Raw(registration, Keys.F11, 0, 1, 105);
            Raw(registration, Keys.F11, 0, 0, 106);
            Equal(2, local, "next press after recording saves");
            Console.WriteLine("PASS focused settings recorder suppression and held focus edge");
        }
        finally
        {
            form.Hide();
            SetForegroundWindow(foreground);
        }
    }

    static void Boundary()
    {
        int next = 0;
        using var first = RawHotkeyInput.Subscribe((_, _, _, _) => throw new InvalidOperationException("fixture"));
        using var second = RawHotkeyInput.Subscribe((_, _, _, _) => next++);
        var shared = typeof(RawHotkeyInput).GetField("shared", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var dispatch = typeof(RawHotkeyInput).GetMethod("Dispatch", Hidden) ?? throw new Exception("raw callback exception boundary absent");
        dispatch.Invoke(shared, [(ushort)Keys.F11, (ushort)0, (ushort)0, 200]);
        Equal(1, next, "throwing subscriber does not suppress later subscribers");
        var body = typeof(RawHotkeyInput).GetMethod("WndProc", Hidden)!.GetMethodBody()!;
        if (!body.ExceptionHandlingClauses.Any(c => c.Flags == ExceptionHandlingClauseOptions.Finally))
            throw new Exception("WM_INPUT default cleanup lacks finally");
        Console.WriteLine("PASS callback exception isolation and mandatory default cleanup");
    }

    static void Raw(HotkeyRegistration registration, Keys key, ushort scan, ushort flags, int time)
    {
        var method = typeof(HotkeyRegistration).GetMethod("RawKey", Hidden) ?? throw new Exception("raw input fallback is absent");
        method.Invoke(registration, [(ushort)key, scan, flags, time]);
    }

    static void Native(HotkeyRegistration registration, int time) => Call(registration, "NativeHotkey", time);

    static void Equal(int expected, int actual, string name)
    {
        if (expected != actual) throw new Exception($"{name}: expected {expected}, got {actual}");
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Target; }

    [DllImport("user32.dll")] static extern uint GetRegisteredRawInputDevices([Out] Device[]? devices, ref uint count, uint size);

    // This process's raw-input registrations for the keyboard (usage page 1, usage 6).
    static Device[] KeyboardTargets()
    {
        uint count = 0;
        uint size = (uint)Marshal.SizeOf<Device>();
        if (GetRegisteredRawInputDevices(null, ref count, size) == uint.MaxValue) throw new Exception("Cannot query raw devices");
        if (count == 0) return Array.Empty<Device>();
        var devices = new Device[count];
        if (GetRegisteredRawInputDevices(devices, ref count, size) == uint.MaxValue) throw new Exception("Cannot read raw devices");
        return devices.Where(d => d.Page == 1 && d.Usage == 6).ToArray();
    }
}
