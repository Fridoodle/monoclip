using MonoClip.Core;
using MonoClip.Windows.Capture;
using MonoClip.Windows.Share;
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
        void Log(string text) => File.AppendAllText(Path.Combine(dir, "errors.log"), DateTimeOffset.Now + " " + text + "\n");
        void Report(Exception e) { Log(e.ToString()); MessageBox.Show(e.Message, "MonoClip", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        // No settings file yet: a new install. Save the defaults so only this very first start counts.
        bool firstRun = !File.Exists(path);
        try { settings = SettingsStore.Load(path); } catch (Exception e) { MessageBox.Show("Could not read settings. Using defaults.\n" + e.Message, "MonoClip", MessageBoxButtons.OK, MessageBoxIcon.Warning); settings = new(); }
        if (firstRun) try { SettingsStore.Save(path, settings); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        if (args.Contains("--minimized")) settings = settings with { StartMinimized = true };
        // Keep the startup entry on this version (e.g. after extracting an update into a new folder).
        try { WindowsIntegration.SyncAutostart(settings.StartWithWindows, Environment.ProcessPath!); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException) { Log("Startup entry: " + e.Message); }
        Application.ThreadException += (_, e) => Report(e.Exception);
        try { using var engine = new ClipEngine(); using var app = new TrayAppContext(engine, settings, path, args.Contains("--settings"), sharer: new ClipShare()); if (firstRun) app.AnnounceStart(); Application.Run(app); }
        catch (Exception e) { Report(e); }
    }
}
