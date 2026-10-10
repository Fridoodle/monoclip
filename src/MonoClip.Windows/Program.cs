using MonoClip.Core;
using MonoClip.Windows.Capture;
using MonoClip.Windows.UI;
namespace MonoClip.Windows;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var mutex = new Mutex(true, "Local\\MonoClip.Replay", out bool unique);
        if (!unique) { MessageBox.Show("MonoClip is already running in the tray.", "MonoClip", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonoClip"); Directory.CreateDirectory(dir); var path = Path.Combine(dir, "settings.json"); AppSettings settings;
        try { settings = SettingsStore.Load(path); } catch (Exception e) { MessageBox.Show("Could not read settings. Using defaults.\n" + e.Message, "MonoClip", MessageBoxButtons.OK, MessageBoxIcon.Warning); settings = new(); }
        if (args.Contains("--minimized")) settings = settings with { StartMinimized = true };
        Application.ThreadException += (_, e) => { File.AppendAllText(Path.Combine(dir, "errors.log"), DateTimeOffset.Now + " " + e.Exception + "\n"); MessageBox.Show(e.Exception.Message, "MonoClip", MessageBoxButtons.OK, MessageBoxIcon.Error); };
        try { using var engine = new ClipEngine(); using var app = new TrayAppContext(engine, settings, path, args.Contains("--settings"), null, new MonoClip.Windows.Share.ClipShare()); Application.Run(app); }
        catch (Exception e) { File.AppendAllText(Path.Combine(dir, "errors.log"), DateTimeOffset.Now + " " + e + "\n"); MessageBox.Show(e.Message, "MonoClip", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
