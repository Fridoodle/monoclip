using MonoClip.Core;
using System.Runtime.InteropServices;
namespace MonoClip.Windows.UI;

public enum TrayIconKind { Recording, Stopped, Busy, Error }

// Tray app. No pop-up notifications: feedback is a sound plus the tray symbol (busy while a link is
// being created, a corner dot while a link is online, "!" after an error) and its tooltip.
public sealed class TrayAppContext : ApplicationContext
{
    const int SpinnerFrames = 12;
    static readonly TimeSpan ErrorShown = TimeSpan.FromSeconds(6);
    readonly SoundEffects sounds = new(); readonly Action<Sound> playSound;
    readonly IClipEngine engine; AppSettings settings; readonly string settingsPath; readonly NotifyIcon tray; readonly HotkeyRegistration hotkeys;
    readonly ToolStripMenuItem toggleItem, saveItem; SettingsForm? form; bool disposed; string? lastClip;
    readonly IClipSharer? sharer; readonly ToolStripMenuItem shareItem, copyLinkItem; ShareInfo? lastShare; int lastShareMinutes;
    readonly System.Windows.Forms.Timer shareTicker = new() { Interval = 30000 }, animation = new() { Interval = 90 };
    readonly Dictionary<(TrayIconKind, bool), Icon> icons = []; Icon[] spinner = [];
    int spinnerFrame; string? errorText; DateTime errorUntil; string lastStatus = "";
    // Replaceable so tests never touch the real clipboard.
    public Action<string> CopyText { get; set; } = text => Clipboard.SetText(text);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath) : this(engine, settings, settingsPath, false) { }
    public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath, bool showSettings) : this(engine, settings, settingsPath, showSettings, null) { }
    public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath, bool showSettings, Action<Sound>? soundOverride) : this(engine, settings, settingsPath, showSettings, soundOverride, null) { }
    public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath, bool showSettings, Action<Sound>? soundOverride, IClipSharer? sharer)
    {
        playSound = soundOverride ?? sounds.Play; this.sharer = sharer;
        this.engine = engine; this.settings = settings with { }; this.settingsPath = settingsPath;
        BuildIcons(); shareTicker.Tick += (_, _) => UpdateTray(); animation.Tick += (_, _) => Animate();
        var menu = new ContextMenuStrip { BackColor = Color.FromArgb(16, 16, 16), ForeColor = Color.White, ShowImageMargin = false, Renderer = new ToolStripProfessionalRenderer(new MonoMenuColors()) };
        toggleItem = new ToolStripMenuItem("Puffer starten", null, (_, _) => Run(Toggle)); menu.Items.Add(toggleItem); saveItem = new ToolStripMenuItem("Clip speichern", null, (_, _) => Run(engine.SaveClip)); menu.Items.Add(saveItem);
        shareItem = new ToolStripMenuItem($"Letzten Clip teilen · {settings.ShareMinutes} Min.", null, (_, _) => ToggleShare()) { Visible = sharer != null }; menu.Items.Add(shareItem); copyLinkItem = new ToolStripMenuItem("Link erneut kopieren", null, (_, _) => Run(CopyShareLink)) { Visible = false }; menu.Items.Add(copyLinkItem); menu.Opening += (_, _) => UpdateShareItems();
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Einstellungen", null, (_, _) => ShowSettings()); menu.Items.Add("Clip-Ordner öffnen", null, (_, _) => Run(() => WindowsIntegration.OpenClipFolder(this.settings.ClipsDirectory, lastClip))); menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Beenden", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = icons[(TrayIconKind.Stopped, false)], Text = "MonoClip · Puffer gestoppt", ContextMenuStrip = menu, Visible = true }; tray.DoubleClick += (_, _) => ShowSettings();
        hotkeys = new(() => Run(engine.SaveClip));
        try { hotkeys.Set(Hotkey.Parse(settings.Hotkey)); } catch (Exception e) { Fail("Hotkey nicht verfügbar: " + e.Message); }
        engine.StatusChanged += StatusChanged; engine.ClipSaved += ClipSaved; if (sharer != null) sharer.Changed += ShareChanged;
        if (settings.StartBufferOnLaunch) Run(() => engine.Start(this.settings));
        UpdateTray(); if (showSettings || !settings.StartMinimized) ShowSettings();
    }

    // Black rounded tile (easy to spot on light and dark taskbars) with a white symbol:
    // dot = recording, ring = stopped, spinning arc = creating a link, "!" = error.
    // The corner badge marks a link that is online.
    internal static Icon MakeIcon(TrayIconKind kind, int size, bool online = false, int frame = 0)
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
            using var fill = new SolidBrush(white); using var pen = new Pen(white, stroke) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            var ring = new RectangleF(inset + stroke / 2, inset + stroke / 2, diameter - stroke, diameter - stroke);
            switch (kind)
            {
                case TrayIconKind.Recording: g.FillEllipse(fill, inset, inset, diameter, diameter); break;
                case TrayIconKind.Stopped: g.DrawEllipse(pen, ring); break;
                case TrayIconKind.Busy: g.DrawArc(pen, ring, frame * (360f / SpinnerFrames) - 90, 270); break;
                case TrayIconKind.Error:
                    float x = size / 2f, bar = Math.Max(1.6f, size / 7f); using (var mark = new Pen(white, bar) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round }) g.DrawLine(mark, x, size * 0.24f, x, size * 0.58f);
                    g.FillEllipse(fill, x - bar * 0.62f, size * 0.70f, bar * 1.24f, bar * 1.24f); break;
            }
            if (online)
            {
                // Corner badge with a black outline so it reads on top of the symbol.
                float badge = Math.Max(5, size * 0.32f), gap = Math.Max(1.2f, size / 11f), at = size - badge - size * 0.06f, top = size * 0.06f;
                using var outline = new SolidBrush(Color.FromArgb(12, 12, 12)); g.FillEllipse(outline, at - gap, top - gap, badge + gap * 2, badge + gap * 2);
                g.FillEllipse(fill, at, top, badge, badge);
            }
        }
        var handle = image.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
    }
    void BuildIcons()
    {
        int size = SystemInformation.SmallIconSize.Width;
        foreach (var kind in new[] { TrayIconKind.Recording, TrayIconKind.Stopped, TrayIconKind.Error }) foreach (var online in new[] { false, true }) icons[(kind, online)] = MakeIcon(kind, size, online);
        spinner = Enumerable.Range(0, SpinnerFrames).Select(f => MakeIcon(TrayIconKind.Busy, size, false, f)).ToArray();
    }
    public void ShowSettings() { if (form == null || form.IsDisposed) { form = new(settings, engine, SaveSettings, Toggle, engine.SaveClip); if (sharer != null) form.AttachSharing(sharer, ToggleShare, () => Run(CopyShareLink), path => _ = ShareClip(path, chosen: true)); } form.Show(); form.WindowState = FormWindowState.Normal; }
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
    // Background failures (export, unexpected stop) only show up as a status text: make them heard.
    void StatusChanged(object? sender, EventArgs args)
    {
        var status = engine.Status;
        if (status != lastStatus && (status.StartsWith("Fehler") || status.StartsWith("Clip-Fehler") || status.StartsWith("Clip bleibt erhalten") || status.StartsWith("Aufnahme wurde unerwartet") || status.StartsWith("Clip-Speicherung dauert"))) Fail(status);
        lastStatus = status; UpdateTray();
    }
    // Tray/settings button: stop a running share, otherwise share the last saved clip (or the newest on disk).
    void ToggleShare()
    {
        if (sharer == null) return;
        if (sharer.Current != null || sharer.IsStarting) { sharer.Stop(); return; }
        var clip = lastClip != null && File.Exists(lastClip) ? lastClip : ClipLibrary.LatestClip(settings.ClipsDirectory);
        if (clip == null) { Fail("Noch kein Clip gespeichert."); return; }
        _ = ShareClip(clip, chosen: false);
    }
    // Creates the link and copies it. Sounds: upload on start, link when copied, error otherwise.
    internal async Task ShareClip(string clip, bool chosen)
    {
        if (sharer == null) return;
        if (chosen) { try { SharePolicy.CheckShareable(clip); } catch (ArgumentException e) { Fail(e.Message); return; } }
        int minutes = settings.ShareMinutes;
        Cue(Sound.Upload); form?.Feedback(sharer.FirstUse ? "Link wird erstellt … beim ersten Mal wird cloudflared geladen (ca. 55 MB)." : $"Link wird erstellt … ({minutes} Min. online)");
        try
        {
            var progress = new Progress<string>(text => { form?.Feedback(text); SetTooltip("MonoClip · " + text); });
            var info = await sharer.ShareAsync(clip, TimeSpan.FromMinutes(minutes), progress);
            lastShare = info; lastShareMinutes = minutes;
            try { CopyText(info.Url); } catch (ExternalException) { }
            Cue(Sound.Link);
            form?.Feedback($"Link kopiert. Online bis {info.ExpiresAt:HH:mm} Uhr ({minutes} Min.).");
        }
        catch (OperationCanceledException) { form?.Feedback("Teilen abgebrochen."); }
        catch (Exception e) { Fail("Teilen fehlgeschlagen: " + e.Message); }
        finally { UpdateTray(); }
    }
    void CopyShareLink() { if (sharer?.Current is not { } c) return; CopyText(c.Url); form?.Feedback($"Link kopiert. Online bis {c.ExpiresAt:HH:mm} Uhr."); }
    void ShareChanged(object? sender, EventArgs args)
    {
        if (disposed || sharer == null) return;
        if (lastShare != null && sharer.Current == null && !sharer.IsStarting)
        {
            bool expired = DateTimeOffset.Now >= lastShare.ExpiresAt.AddSeconds(-5);
            form?.Feedback(expired ? $"Die {lastShareMinutes} Minuten sind um – der Clip ist nicht mehr erreichbar." : "Teilen beendet – der Clip ist nicht mehr erreichbar."); lastShare = null;
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
    void ClipSaved(object? sender, ClipSavedEventArgs args)
    {
        lastClip = args.Clip.Path; Cue(Sound.Clip); form?.Feedback("Gespeichert: " + args.Clip.Path);
        // Auto-share replaces a link that is still online: the newest clip is the one to post.
        if (settings.AutoShare && sharer != null) { sharer.Stop(); _ = ShareClip(args.Clip.Path, chosen: false); }
    }
    // The sound setting switches all cues off; feedback then stays in the tray symbol and tooltip.
    void Cue(Sound sound) { if (settings.ClipBeep) playSound(sound); }
    void Fail(string message) { errorText = message; errorUntil = DateTime.UtcNow + ErrorShown; Cue(Sound.Error); form?.Feedback(message); UpdateTray(); }
    void SetTooltip(string text) => tray.Text = text.Length > 63 ? text[..62] + "…" : text;
    void Animate() { if (sharer?.IsStarting == true) spinnerFrame = (spinnerFrame + 1) % SpinnerFrames; UpdateIcon(); }
    void UpdateIcon()
    {
        bool error = errorText != null && DateTime.UtcNow < errorUntil, busy = sharer?.IsStarting == true;
        if (!error) errorText = null;
        tray.Icon = error ? icons[(TrayIconKind.Error, false)] : busy ? spinner[spinnerFrame] : icons[(engine.IsRunning ? TrayIconKind.Recording : TrayIconKind.Stopped, sharer?.Current != null)];
        // The timer only runs while something moves or an error mark has to disappear again.
        if (busy || error) animation.Start(); else animation.Stop();
    }
    void UpdateTray()
    {
        if (disposed) return; bool active = engine.IsRunning; UpdateIcon();
        SetTooltip(errorText != null ? "MonoClip · " + errorText : "MonoClip · " + (sharer?.IsStarting == true ? "Link wird erstellt · " : sharer?.Current is { } shared ? $"online, noch {MinutesLeft(shared)} Min. · " : "") + engine.Status);
        toggleItem.Text = active ? "Puffer stoppen" : "Puffer starten"; saveItem.Text = $"Letzte {settings.ClipSeconds} Sekunden speichern"; saveItem.Enabled = active; UpdateShareItems(); form?.UpdateStatus();
    }
    void Run(Action action) { try { action(); } catch (Exception e) { Fail(e.Message); } UpdateTray(); }
    protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            engine.Dispose(); disposed = true; engine.StatusChanged -= StatusChanged; engine.ClipSaved -= ClipSaved; if (sharer != null) { sharer.Changed -= ShareChanged; sharer.Dispose(); }
            shareTicker.Dispose(); animation.Dispose(); hotkeys.Dispose(); form?.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
            foreach (var icon in icons.Values.Concat(spinner)) icon.Dispose(); sounds.Dispose();
        }
        base.Dispose(disposing);
    }
}
