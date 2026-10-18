using System.Reflection;
using System.Text.RegularExpressions;
using MonoClip.Windows.Capture;
using MonoClip.Core;
using MonoClip.Windows.UI;
using static UiTest;

// The tray app itself: launch, menu, exit, and the sounds it plays (sharing is in TraySharingTests).
internal static class TrayTests
{
    public static void Run()
    {
        Test("Tray-only launch has no visible taskbar window and settings can reopen", TrayOnlyLaunch);
        Test("Tray never opens pop-up notifications", NeverShowsBalloons);
        Test("Clip sound fires only after success and honors the current sound toggle", ClipSoundFollowsToggle);
        Test("Default launch starts the replay buffer", DefaultLaunchStartsBuffer);
        Test("Manual buffer start/stop, stopping a share and launch have their own sounds", OwnSoundsForEachAction);
        Test("Screenshot hotkey saves a PNG named after the app into the clip folder's Screenshots, or a folder of choice", ScreenshotsAreSaved);
        Test("Screenshot copies a whole monitor at its full resolution", ScreenshotCopiesAMonitor);
    }

    static void TrayOnlyLaunch()
    {
        using var temp = new TempFolder("MonoClip-ui-");
        var dir = temp.Path;
        var settings = new AppSettings { Hotkey = "Alt+Shift+F10", ScreenshotHotkey = "Ctrl+Alt+Shift+F13", StartMinimized = true, StartBufferOnLaunch = false };
        using var ctx = new TrayAppContext(new FakeEngine(), settings, Path.Combine(dir, "settings.json"));
        var tray = Tray(ctx);
        var menu = tray.ContextMenuStrip!;
        var color = ((ToolStripProfessionalRenderer)menu.Renderer).ColorTable.MenuItemSelected;
        if (color.R != color.G || color.G != color.B) throw new Exception("colored tray menu selection");
        Application.DoEvents();
        if (Application.OpenForms.Cast<Form>().Any(f => f.Visible)) throw new Exception("window visible on minimized launch");

        ctx.ShowSettings();
        Application.DoEvents();
        var form = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Visible);
        if (form == null) throw new Exception("settings failed to open");
        form.Close();
        Application.DoEvents();
        if (form.Visible || form.IsDisposed) throw new Exception("close-to-tray broken");

        var toggle = (ToolStripMenuItem)menu.Items[0];
        var exit = (ToolStripMenuItem)menu.Items[^1];
        var engine = Field<FakeEngine>(ctx, "engine");
        toggle.PerformClick();
        if (!engine.IsRunning) throw new Exception("tray start failed");
        toggle.PerformClick();
        if (engine.IsRunning) throw new Exception("tray stop failed");

        engine.FailDispose = true;
        try
        {
            exit.PerformClick();
            throw new Exception("exit ignored pending shutdown failure");
        }
        catch (TimeoutException) { }
        if (!tray.Visible) throw new Exception("failed exit stranded capture without accessible tray");
        engine.FailDispose = false;
        exit.PerformClick();
        if (tray.Visible) throw new Exception("exit left a tray icon");
    }

    static void NeverShowsBalloons()
    {
        // Scan the tray class and its compiler-generated lambdas/state machines for ShowBalloonTip calls.
        var trayType = typeof(TrayAppContext);
        var types = trayType.Assembly.GetTypes().Where(t => t == trayType || t.FullName!.StartsWith("MonoClip.Windows.UI.TrayAppContext+"));
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var methods = types.SelectMany(t => t.GetMethods(declared))
            .Concat<MethodBase>(types.SelectMany(t => t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)));
        foreach (var method in methods)
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) continue;
            for (int i = 0; i < il.Length - 4; i++)
            {
                if (il[i] is not (0x28 or 0x6f)) continue; // call, callvirt
                try
                {
                    if (method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1))?.Name == "ShowBalloonTip")
                        throw new InvalidOperationException(method.Name + " opens a Windows pop-up");
                }
                catch (ArgumentException) { }
                catch (BadImageFormatException) { }
            }
        }
    }

    static void ClipSoundFollowsToggle()
    {
        var played = new List<Sound>();
        var engine = new FakeEngine();
        using var temp = new TempFolder("MonoClip-cue-test-");
        var dir = temp.Path;
        var settings = new AppSettings { ClipBeep = true, Hotkey = "Ctrl+Alt+Shift+F10", ScreenshotHotkey = "Ctrl+Alt+Shift+F14", StartBufferOnLaunch = false };
        using var ctx = new TrayAppContext(engine, settings, Path.Combine(dir, "settings.json"), false, played.Add);
        Equal(0, played.Count);
        var record = new ClipRecord(Path.Combine(dir, "unit-fixture.mkv"), "Fixture", DateTimeOffset.Now, 5, 1280, 720, 30, "test",
            new("Fixture", "fixture.exe", "", "desktop"));
        RaiseClipSaved(ctx, record);
        Equal("Clip", string.Join(",", played));

        Call(ctx, "SaveSettings", settings with { ClipBeep = false });
        RaiseClipSaved(ctx, record);
        Equal(1, played.Count);
        Equal(false, SettingsStore.Load(Path.Combine(dir, "settings.json")).ClipBeep);

        Call(ctx, "SaveSettings", settings with { ClipBeep = true });
        RaiseClipSaved(ctx, record);
        Equal(2, played.Count);
    }

    static void DefaultLaunchStartsBuffer()
    {
        using var temp = new TempFolder("MonoClip-ui-");
        var dir = temp.Path;
        var engine = new FakeEngine();
        using var ctx = new TrayAppContext(engine, new AppSettings { Hotkey = "Ctrl+Alt+Shift+F9", ScreenshotHotkey = "Ctrl+Alt+Shift+F15" }, Path.Combine(dir, "settings.json"));
        if (!engine.IsRunning) throw new Exception("buffer not started by default");
    }

    static void OwnSoundsForEachAction()
    {
        using var temp = new TempFolder("MonoClip-cues-");
        var dir = temp.Path;
        var played = new List<Sound>();
        var sharer = new FakeSharer();
        var engine = new FakeEngine();
        var settings = new AppSettings { Hotkey = "Ctrl+Alt+Shift+F3", ScreenshotHotkey = "Ctrl+Alt+Shift+F16", StartBufferOnLaunch = true, ClipsDirectory = dir };
        using var ctx = new TrayAppContext(engine, settings, Path.Combine(dir, "s.json"), false, played.Add, sharer) { CopyText = _ => { } };
        Equal(0, played.Count);
        ctx.AnnounceStart();
        Equal("Launch", string.Join(",", played));
        played.Clear();

        var toggle = (ToolStripMenuItem)Tray(ctx).ContextMenuStrip!.Items[0];
        toggle.PerformClick();
        toggle.PerformClick();
        Equal("BufferOff,BufferOn", string.Join(",", played));
        played.Clear();

        File.WriteAllBytes(Path.Combine(dir, "c.mkv"), [0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0, 0, 0, 0, 0]);
        var share = ShareItem(ctx);
        share.PerformClick();
        Application.DoEvents();
        played.Clear();
        share.PerformClick();
        Equal("ShareStopped", string.Join(",", played));
    }

    static void ScreenshotsAreSaved()
    {
        using var temp = new TempFolder("MonoClip-shots-");
        var dir = temp.Path;
        var played = new List<Sound>();
        var settings = new AppSettings
        {
            Hotkey = "Ctrl+Alt+Shift+F12", ScreenshotHotkey = "Ctrl+Alt+Shift+F24", StartBufferOnLaunch = false, ClipsDirectory = Path.Combine(dir, "clips"),
        };
        using var ctx = new TrayAppContext(new FakeEngine(), settings, Path.Combine(dir, "settings.json"), false, played.Add);
        var green = Color.FromArgb(10, 200, 30);
        ctx.CaptureScreen = () =>
        {
            var image = new Bitmap(64, 48);
            using (var g = Graphics.FromImage(image)) g.Clear(green);
            return (image, "Valorant");
        };
        void Shoot(int sounds)
        {
            played.Clear();
            for (int i = 0; i < sounds; i++) Call(ctx, "TakeScreenshot");
            PumpUntil(() => played.Count >= sounds, 10000);
        }

        Shoot(1);
        Equal("Screenshot", string.Join(",", played));
        var folder = Path.Combine(dir, "clips", Screenshots.DefaultFolderName);
        var files = Directory.GetFiles(folder, "*.png");
        Equal(1, files.Length);
        if (!Regex.IsMatch(Path.GetFileName(files[0]), @"^\d{4}-\d\d-\d\d_\d\d-\d\d-\d\d_Valorant\.png$")) throw new Exception("unexpected name: " + files[0]);
        using (var saved = new Bitmap(files[0]))
        {
            Equal(64, saved.Width);
            Equal(48, saved.Height);
            Equal(green.ToArgb(), saved.GetPixel(5, 5).ToArgb());
        }

        // Two in a row (same second) are two files.
        Shoot(2);
        Equal(3, Directory.GetFiles(folder, "*.png").Length);

        // A folder of choice.
        var custom = Path.Combine(dir, "custom");
        Call(ctx, "SaveSettings", settings with { ScreenshotsDirectory = custom });
        Shoot(1);
        Equal(1, Directory.GetFiles(custom, "*.png").Length);
        Equal(3, Directory.GetFiles(folder, "*.png").Length);

        // A screen that cannot be copied is an error, not a file.
        ctx.CaptureScreen = () => throw new InvalidOperationException("no screen");
        Shoot(1);
        Equal("Error", string.Join(",", played));
        if (!Tray(ctx).Text.Contains("Screenshot failed")) throw new Exception("error not in the tooltip: " + Tray(ctx).Text);
        Equal(1, Directory.GetFiles(custom, "*.png").Length);

        // The screenshot hotkey cannot be the save hotkey.
        try
        {
            Call(ctx, "SaveSettings", settings with { ScreenshotHotkey = settings.Hotkey });
            throw new Exception("screenshot hotkey equal to the save hotkey accepted");
        }
        catch (TargetInvocationException e) when (e.InnerException is ArgumentException) { }
    }

    static void ScreenshotCopiesAMonitor()
    {
        var (image, app) = Screenshot.CaptureActiveMonitor();
        using (image)
        {
            if (!Screen.AllScreens.Any(s => s.Bounds.Size == image.Size)) throw new Exception("not the size of a monitor: " + image.Size);
            if (string.IsNullOrWhiteSpace(app)) throw new Exception("no app name");
        }
    }
}
