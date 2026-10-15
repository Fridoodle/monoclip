using System.Diagnostics;
using System.Reflection;
using MonoClip.Core;
using MonoClip.Windows;
using MonoClip.Windows.UI;

// What every UI test shares: the runner, the assertion, and typed access to the private members the tests drive.
internal static class UiTest
{
    public const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    public static int Failures { get; private set; }

    public static void Test(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS " + name);
        }
        catch (Exception e)
        {
            Failures++;
            Console.WriteLine("FAIL " + name + ": " + (e.InnerException ?? e).Message);
        }
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    }

    // Private instance fields and methods of the object's own class.
    public static T Field<T>(object owner, string name) => (T)FieldOf(owner, name).GetValue(owner)!;
    public static void SetField(object owner, string name, object? value) => FieldOf(owner, name).SetValue(owner, value);
    public static object? Call(object owner, string name, params object?[] args)
    {
        var method = owner.GetType().GetMethod(name, Hidden) ?? throw new Exception($"{owner.GetType().Name}.{name}() missing");
        return method.Invoke(owner, args);
    }
    static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, Hidden) ?? throw new Exception($"{owner.GetType().Name}.{name} missing");

    // Runs the message loop for a while, or until done() holds.
    public static void Pump(int milliseconds, int step = 10) => PumpUntil(() => false, milliseconds, step);
    public static void PumpUntil(Func<bool> done, int timeout, int step = 10)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < timeout)
        {
            Application.DoEvents();
            Thread.Sleep(step);
        }
    }

    // Every control below c, depth first.
    public static IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(x => All(x).Prepend(x));

    public static SettingsForm NewSettingsForm(AppSettings? settings = null, Action<AppSettings>? save = null) =>
        new(settings ?? new AppSettings(), new FakeEngine(), save ?? (_ => { }), () => { }, () => { });

    public static NotifyIcon Tray(TrayAppContext ctx) => Field<NotifyIcon>(ctx, "tray");
    public static List<ToolStripItem> MenuItems(TrayAppContext ctx) => Tray(ctx).ContextMenuStrip!.Items.Cast<ToolStripItem>().ToList();
    public static ToolStripMenuItem MenuItem(TrayAppContext ctx, string text) => (ToolStripMenuItem)MenuItems(ctx).Single(i => i.Text == text);
    public static ToolStripMenuItem ShareItem(TrayAppContext ctx) =>
        (ToolStripMenuItem)MenuItems(ctx).Single(i => i.Text!.StartsWith("Quick share last clip"));
    // What the engine raises after a clip is written.
    public static void RaiseClipSaved(TrayAppContext ctx, ClipRecord clip) => Call(ctx, "ClipSaved", null, new ClipSavedEventArgs(clip));
}

// A fresh folder under %TEMP%, deleted with everything in it on dispose.
internal sealed class TempFolder : IDisposable
{
    public string Path { get; }

    public TempFolder(string prefix)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid());
        Directory.CreateDirectory(Path);
    }

    public void Dispose() => Directory.Delete(Path, true);
}
