using MonoClip.Core;
using System.Runtime.InteropServices;
namespace MonoClip.Windows.UI;

public sealed class TrayAppContext : ApplicationContext
{
    readonly ClipBeepSound clipSound=new(); readonly Action playClipBeep;
    readonly IClipEngine engine; AppSettings settings; readonly string settingsPath; readonly NotifyIcon tray; readonly HotkeyRegistration hotkeys;
    Icon runningIcon = null!, pausedIcon = null!; readonly ToolStripMenuItem toggleItem, saveItem; SettingsForm? form; bool disposed; string? lastClip;
    readonly IClipSharer? sharer; readonly ToolStripMenuItem shareItem, copyLinkItem; ShareInfo? lastShare; int lastShareMinutes;
    readonly System.Windows.Forms.Timer shareTicker = new() { Interval = 30000 };
    // Replaceable so tests never touch the real clipboard.
    public Action<string> CopyText { get; set; } = text => Clipboard.SetText(text);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath) : this(engine, settings, settingsPath, false) { }
    public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath, bool showSettings):this(engine,settings,settingsPath,showSettings,null){}
    public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath, bool showSettings, Action? beepOverride) : this(engine, settings, settingsPath, showSettings, beepOverride, null) { }
        public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath, bool showSettings, Action? beepOverride, IClipSharer? sharer)
        {
            playClipBeep=beepOverride??clipSound.Play; this.sharer = sharer;
        this.engine = engine; this.settings = settings with { }; this.settingsPath = settingsPath;
        BuildIcons(); shareTicker.Tick += (_, _) => UpdateTray(); var menu = new ContextMenuStrip { BackColor = Color.FromArgb(16, 16, 16), ForeColor = Color.White, ShowImageMargin = false, Renderer = new ToolStripProfessionalRenderer(new MonoMenuColors()) };
        toggleItem = new ToolStripMenuItem("Puffer starten", null, (_, _) => Run(Toggle)); menu.Items.Add(toggleItem); saveItem = new ToolStripMenuItem("Clip speichern", null, (_, _) => Run(engine.SaveClip)); menu.Items.Add(saveItem);
        shareItem = new ToolStripMenuItem($"Letzten Clip teilen · {settings.ShareMinutes} Min.", null, (_, _) => ToggleShare()) { Visible = sharer != null }; menu.Items.Add(shareItem); copyLinkItem = new ToolStripMenuItem("Link erneut kopieren", null, (_, _) => Run(CopyShareLink)) { Visible = false }; menu.Items.Add(copyLinkItem); menu.Opening += (_, _) => UpdateShareItems();
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Einstellungen", null, (_, _) => ShowSettings()); menu.Items.Add("Clip-Ordner öffnen", null, (_, _) => Run(() => WindowsIntegration.OpenClipFolder(this.settings.ClipsDirectory, lastClip))); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Beenden", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = pausedIcon, Text = "MonoClip · Puffer gestoppt", ContextMenuStrip = menu, Visible = true }; tray.DoubleClick += (_, _) => ShowSettings();
        hotkeys = new(() => Run(engine.SaveClip));
        try { hotkeys.Set(Hotkey.Parse(settings.Hotkey)); } catch (Exception e) { tray.ShowBalloonTip(5000, "MonoClip · Hotkey nicht verfügbar", e.Message, ToolTipIcon.Warning); }
        engine.StatusChanged += StatusChanged; engine.ClipSaved += ClipSaved; if (sharer != null) sharer.Changed += ShareChanged;
        if (settings.StartBufferOnLaunch) Run(() => engine.Start(this.settings));
        UpdateTray(); if (showSettings || !settings.StartMinimized) ShowSettings();
    }
    // Tray symbol: black rounded tile (easy to spot on light and dark taskbars) with a white dot.
    // Filled while recording, a ring while stopped.
    internal static Icon MakeIcon(bool active, int size)
    {
        using var image = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(image))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
            float radius = size * 0.22f; using (var tile = new System.Drawing.Drawing2D.GraphicsPath())
            {
                tile.AddArc(0, 0, radius * 2, radius * 2, 180, 90); tile.AddArc(size - radius * 2, 0, radius * 2, radius * 2, 270, 90);
                tile.AddArc(size - radius * 2, size - radius * 2, radius * 2, radius * 2, 0, 90); tile.AddArc(0, size - radius * 2, radius * 2, radius * 2, 90, 90); tile.CloseFigure();
                using var black = new SolidBrush(Color.FromArgb(12, 12, 12)); g.FillPath(black, tile);
            }
            var white = Color.FromArgb(245, 245, 245);
            float stroke = Math.Max(1.5f, size / 9f), diameter = size * 0.56f, inset = (size - diameter) / 2f;
            if (active) { using var fill = new SolidBrush(white); g.FillEllipse(fill, inset, inset, diameter, diameter); }
            else { using var pen = new Pen(white, stroke); g.DrawEllipse(pen, inset + stroke / 2, inset + stroke / 2, diameter - stroke, diameter - stroke); }
        }
        var handle = image.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
    }
    void BuildIcons() { int size = SystemInformation.SmallIconSize.Width; runningIcon = MakeIcon(true, size); pausedIcon = MakeIcon(false, size); }
    public void ShowSettings() { if (form == null || form.IsDisposed) { form = new(settings, engine, SaveSettings, Toggle, engine.SaveClip); if (sharer != null) form.AttachSharing(sharer, ToggleShare, () => Run(CopyShareLink)); } form.Show(); form.WindowState = FormWindowState.Normal; }
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
    // Last saved clip (this session), otherwise the newest clip on disk.
    async void ToggleShare()
    {
        if (sharer == null) return;
        if (sharer.Current != null || sharer.IsStarting) { sharer.Stop(); return; }
        var clip = lastClip != null && File.Exists(lastClip) ? lastClip : ClipLibrary.LatestClip(settings.ClipsDirectory);
        if (clip == null) { tray.ShowBalloonTip(4000, "MonoClip", "Noch kein Clip gespeichert.", ToolTipIcon.Info); return; }
        int minutes = settings.ShareMinutes;
        tray.ShowBalloonTip(6000, "MonoClip · Link wird erstellt",
            (sharer.FirstUse ? "Beim ersten Mal wird cloudflared geladen (ca. 55 MB), das dauert etwas länger. " : "Dauert meist 10–20 Sekunden. ") + $"Danach ist der Clip {minutes} Minuten online.", ToolTipIcon.Info);
        try
        {
            var progress = new Progress<string>(text => { form?.Feedback(text); var t = "MonoClip · " + text; tray.Text = t.Length > 63 ? t[..63] : t; });
            var info = await sharer.ShareAsync(clip, TimeSpan.FromMinutes(minutes), progress);
            lastShare = info; lastShareMinutes = minutes;
            try { CopyText(info.Url); } catch (ExternalException) { }
            tray.ShowBalloonTip(8000, $"MonoClip · Link kopiert · {minutes} Min. online", $"Online bis {info.ExpiresAt:HH:mm} Uhr. In Discord einfügen – danach geht der Clip automatisch offline.", ToolTipIcon.Info);
            form?.Feedback($"Link kopiert. Online bis {info.ExpiresAt:HH:mm} Uhr ({minutes} Min.).");
        }
        catch (OperationCanceledException) { form?.Feedback("Teilen abgebrochen."); }
        catch (Exception e) { tray.ShowBalloonTip(6000, "MonoClip · Teilen fehlgeschlagen", e.Message, ToolTipIcon.Warning); form?.Feedback("Teilen fehlgeschlagen: " + e.Message); }
        finally { UpdateTray(); }
    }
    void CopyShareLink() { if (sharer?.Current is not { } c) return; CopyText(c.Url); form?.Feedback($"Link kopiert. Online bis {c.ExpiresAt:HH:mm} Uhr."); }
    void ShareChanged(object? sender, EventArgs args)
    {
        if (disposed || sharer == null) return;
        if (lastShare != null && sharer.Current == null && !sharer.IsStarting)
        {
            bool expired = DateTimeOffset.Now >= lastShare.ExpiresAt.AddSeconds(-5);
            var text = expired ? $"Die {lastShareMinutes} Minuten sind um – der Clip ist nicht mehr erreichbar." : "Teilen beendet – der Clip ist nicht mehr erreichbar.";
            tray.ShowBalloonTip(5000, "MonoClip · Link offline", text, ToolTipIcon.Info); form?.Feedback(text); lastShare = null;
        }
        if (sharer.Current != null) shareTicker.Start(); else shareTicker.Stop();
        UpdateTray();
    }
    static int MinutesLeft(ShareInfo share) => Math.Max(1, (int)Math.Ceiling((share.ExpiresAt - DateTimeOffset.Now).TotalMinutes));
    void UpdateShareItems()
    {
        if (sharer == null) return;
        shareItem.Text = sharer.IsStarting ? "Teilen abbrechen" : sharer.Current is { } c ? $"Teilen beenden · noch {MinutesLeft(c)} Min." : $"Letzten Clip teilen · {settings.ShareMinutes} Min.";
        copyLinkItem.Visible = sharer.Current != null;
    }
    void ClipSaved(object? sender, ClipSavedEventArgs args) { lastClip = args.Clip.Path; if(settings.ClipBeep)playClipBeep(); form?.Feedback("Gespeichert: " + args.Clip.Path); }
    void UpdateTray() { if (disposed) return; bool active = engine.IsRunning; tray.Icon = active ? runningIcon : pausedIcon; var text = "MonoClip · " + (sharer?.Current is { } shared ? $"geteilt, noch {MinutesLeft(shared)} Min. · " : "") + engine.Status; tray.Text = text.Length > 63 ? text[..63] : text; toggleItem.Text = active ? "Puffer stoppen" : "Puffer starten"; saveItem.Text = $"Letzte {settings.ClipSeconds} Sekunden speichern"; saveItem.Enabled = active; UpdateShareItems(); form?.UpdateStatus(); }
    void Run(Action action) { try { action(); } catch (Exception e) { tray.ShowBalloonTip(5000, "MonoClip", e.Message, ToolTipIcon.Warning); form?.Feedback(e.Message); } UpdateTray(); }
    protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { engine.Dispose(); disposed = true; engine.StatusChanged -= StatusChanged; engine.ClipSaved -= ClipSaved; if (sharer != null) { sharer.Changed -= ShareChanged; sharer.Dispose(); } shareTicker.Dispose(); hotkeys.Dispose(); form?.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); runningIcon.Dispose(); pausedIcon.Dispose(); clipSound.Dispose(); }
        base.Dispose(disposing);
    }
}
