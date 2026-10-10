using MonoClip.Core;
using System.Runtime.InteropServices;
namespace MonoClip.Windows.UI;

public sealed class TrayAppContext : ApplicationContext
{
    readonly ClipBeepSound clipSound=new(); readonly Action playClipBeep;
    readonly IClipEngine engine; AppSettings settings; readonly string settingsPath; readonly NotifyIcon tray; readonly HotkeyRegistration hotkeys;
    readonly Icon runningIcon, pausedIcon; readonly ToolStripMenuItem toggleItem, saveItem; SettingsForm? form; bool disposed; string? lastClip;
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath) : this(engine, settings, settingsPath, false) { }
    public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath, bool showSettings):this(engine,settings,settingsPath,showSettings,null){}
        public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath, bool showSettings, Action? beepOverride)
        {
            playClipBeep=beepOverride??clipSound.Play;
        this.engine = engine; this.settings = settings with { }; this.settingsPath = settingsPath;
        runningIcon = MakeIcon(true); pausedIcon = MakeIcon(false); var menu = new ContextMenuStrip { BackColor = Color.FromArgb(16, 16, 16), ForeColor = Color.White, ShowImageMargin = false, Renderer = new ToolStripProfessionalRenderer(new MonoMenuColors()) };
        toggleItem = new ToolStripMenuItem("Puffer starten", null, (_, _) => Run(Toggle)); menu.Items.Add(toggleItem); saveItem = new ToolStripMenuItem("Clip speichern", null, (_, _) => Run(engine.SaveClip)); menu.Items.Add(saveItem); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Einstellungen", null, (_, _) => ShowSettings()); menu.Items.Add("Clip-Ordner öffnen", null, (_, _) => Run(() => WindowsIntegration.OpenClipFolder(this.settings.ClipsDirectory, lastClip))); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Beenden", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = pausedIcon, Text = "MonoClip · Puffer gestoppt", ContextMenuStrip = menu, Visible = true }; tray.DoubleClick += (_, _) => ShowSettings();
        hotkeys = new(() => Run(engine.SaveClip));
        try { hotkeys.Set(Hotkey.Parse(settings.Hotkey)); } catch (Exception e) { tray.ShowBalloonTip(5000, "MonoClip · Hotkey nicht verfügbar", e.Message, ToolTipIcon.Warning); }
        engine.StatusChanged += StatusChanged; engine.ClipSaved += ClipSaved;
        if (settings.StartBufferOnLaunch) Run(() => engine.Start(this.settings));
        UpdateTray(); if (showSettings || !settings.StartMinimized) ShowSettings();
    }
    static Icon MakeIcon(bool active)
    {
        using var image = new Bitmap(32, 32); using (var g = Graphics.FromImage(image)) { g.Clear(Color.Black); using var pen = new Pen(Color.White, 3); g.DrawLine(pen, 5, 5, 12, 5); g.DrawLine(pen, 5, 5, 5, 12); g.DrawLine(pen, 27, 27, 20, 27); g.DrawLine(pen, 27, 27, 27, 20); if (active) g.FillEllipse(Brushes.White, 11, 11, 10, 10); else g.DrawEllipse(pen, 11, 11, 10, 10); }
        var handle = image.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
    }
    public void ShowSettings() { if (form == null || form.IsDisposed) form = new(settings, engine, SaveSettings, Toggle, engine.SaveClip); form.Show(); form.WindowState = FormWindowState.Normal; }
    void Toggle() { if (engine.IsRunning) engine.Stop(); else engine.Start(settings); UpdateTray(); }
    void SaveSettings(AppSettings next)
    {
        next.Validate(); var parsed = Hotkey.Parse(next.Hotkey); var old = settings with { }; bool applied = false, autostartChanged = false;
        try
        {
            hotkeys.Set(parsed); engine.Apply(next); applied = true;
            if (next.StartWithWindows != old.StartWithWindows) { WindowsIntegration.SetAutostart(next.StartWithWindows, Environment.ProcessPath!); autostartChanged = true; }
            SettingsStore.Save(settingsPath, next); settings = next with { }; UpdateTray();
        }
        catch (Exception original) { var rollbackErrors = new List<string>(); try { hotkeys.Set(Hotkey.Parse(old.Hotkey)); } catch (Exception e) { rollbackErrors.Add(e.Message); } if (applied) try { engine.Apply(old); } catch (Exception e) { rollbackErrors.Add(e.Message); } if (autostartChanged) try { WindowsIntegration.SetAutostart(old.StartWithWindows, Environment.ProcessPath!); } catch (Exception e) { rollbackErrors.Add(e.Message); } throw new InvalidOperationException(original.Message + (rollbackErrors.Count == 0 ? "" : " Rücksetzen fehlgeschlagen: " + string.Join("; ", rollbackErrors)), original); }
    }
    void StatusChanged(object? sender, EventArgs args) { UpdateTray(); }
    void ClipSaved(object? sender, ClipSavedEventArgs args) { lastClip = args.Clip.Path; if(settings.ClipBeep)playClipBeep(); form?.Feedback("Gespeichert: " + args.Clip.Path); }
    void UpdateTray() { if (disposed) return; bool active = engine.IsRunning; tray.Icon = active ? runningIcon : pausedIcon; var text = "MonoClip · " + engine.Status; tray.Text = text.Length > 63 ? text[..63] : text; toggleItem.Text = active ? "Puffer stoppen" : "Puffer starten"; saveItem.Text = $"Letzte {settings.ClipSeconds} Sekunden speichern"; saveItem.Enabled = active; form?.UpdateStatus(); }
    void Run(Action action) { try { action(); } catch (Exception e) { tray.ShowBalloonTip(5000, "MonoClip", e.Message, ToolTipIcon.Warning); form?.Feedback(e.Message); } UpdateTray(); }
    protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { engine.Dispose(); disposed = true; engine.StatusChanged -= StatusChanged; engine.ClipSaved -= ClipSaved; hotkeys.Dispose(); form?.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); runningIcon.Dispose(); pausedIcon.Dispose(); clipSound.Dispose(); }
        base.Dispose(disposing);
    }
}
