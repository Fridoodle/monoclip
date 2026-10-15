using MonoClip.Core;
using MonoClip.Windows.Share;
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
    int spinnerFrame; string? errorText; DateTime errorUntil;
    // Replaceable so tests never touch the real clipboard.
    public Action<string> CopyText { get; set; } = text => Clipboard.SetText(text);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    // soundOverride hears the cues instead of the speakers (tests). Without a sharer there is no sharing.
    public TrayAppContext(IClipEngine engine, AppSettings settings, string settingsPath, bool showSettings = false, Action<Sound>? soundOverride = null, IClipSharer? sharer = null)
    {
        playSound = soundOverride ?? sounds.Play; this.sharer = sharer;
        this.engine = engine; this.settings = settings with { }; this.settingsPath = settingsPath;
        BuildIcons(); shareTicker.Tick += (_, _) => UpdateTray(); animation.Tick += (_, _) => Animate();
        var menu = new ContextMenuStrip { BackColor = Color.FromArgb(16, 16, 16), ForeColor = Color.White, ShowImageMargin = false, Renderer = new ToolStripProfessionalRenderer(new MonoMenuColors()) };
        ToolStripMenuItem Item(string text, Action click) { var item = new ToolStripMenuItem(text, null, (_, _) => click()); menu.Items.Add(item); return item; }
        toggleItem = Item("Start buffer", () => Run(Toggle)); saveItem = Item("Save clip", () => Run(engine.SaveClip));
        shareItem = Item($"Quick share last clip ({settings.ShareMinutes} min)", ToggleShare); shareItem.Visible = sharer != null;
        Item("Export clip", EditLastClip); copyLinkItem = Item("Copy link", () => Run(CopyShareLink)); copyLinkItem.Visible = false;
        menu.Opening += (_, _) => UpdateShareItems();
        menu.Items.Add(new ToolStripSeparator()); Item("Settings", () => ShowSettingsAt(Cursor.Position)); Item("Open clip folder", () => Run(() => WindowsIntegration.OpenClipFolder(this.settings.ClipsDirectory, lastClip)));
        menu.Items.Add(new ToolStripSeparator()); Item("Exit", ExitThread);
        tray = new NotifyIcon { Icon = icons[(TrayIconKind.Stopped, false)], Text = "MonoClip · Buffer stopped", ContextMenuStrip = menu, Visible = true }; tray.DoubleClick += (_, _) => ShowSettingsAt(Cursor.Position);
        // While the settings window records a new hotkey, pressing the old one must not save a clip.
        hotkeys = new(() => Run(engine.SaveClip)) { Suppressed = () => form is { IsDisposed: false } f && f.RecordingHotkey };
        try { hotkeys.Set(Hotkey.Parse(settings.Hotkey)); } catch (Exception e) { Fail("Hotkey unavailable: " + e.Message); }
        engine.StatusChanged += StatusChanged; engine.Failed += EngineFailed; engine.ClipSaved += ClipSaved; if (sharer != null) sharer.Changed += ShareChanged;
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
            using (var tile = Look.Rounded(new RectangleF(0, 0, size, size), size * 0.22f)) using (var black = new SolidBrush(Color.FromArgb(12, 12, 12))) g.FillPath(black, tile);
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
    // The settings popup opens by the tray corner, or next to the tray icon (or menu) the user just clicked.
    public void ShowSettings() => ShowSettingsAt(null);
    void ShowSettingsAt(Point? anchor)
    {
        if (form == null || form.IsDisposed)
        {
            form = new(settings, engine, SaveSettings, Toggle, engine.SaveClip);
            if (sharer != null) form.AttachSharing(sharer, ToggleShare, EditLastClip, () => Run(CopyShareLink), ExportFile);
        }
        form.ShowNear(anchor);
    }
    // Manual start/stop (tray, settings window). Starting on launch plays the launch sound instead.
    void Toggle() { if (engine.IsRunning) { engine.Stop(); Cue(Sound.BufferOff); } else { engine.Start(settings); Cue(Sound.BufferOn); } UpdateTray(); }
    // Played on the very first start of a new install: the app only appears in the tray, which new users miss.
    public void AnnounceStart() => Cue(Sound.Launch);
    // Applies new settings everywhere or nowhere: each step that took effect is undone when a later one fails.
    void SaveSettings(AppSettings next)
    {
        next.Validate(); var parsed = Hotkey.Parse(next.Hotkey); var old = settings with { }; var undo = new List<Action>();
        try
        {
            hotkeys.Set(parsed); undo.Add(() => hotkeys.Set(Hotkey.Parse(old.Hotkey)));
            engine.Apply(next); undo.Add(() => engine.Apply(old));
            if ((next.StartWithWindows || old.StartWithWindows) && WindowsIntegration.SyncAutostart(next.StartWithWindows, Environment.ProcessPath!))
                undo.Add(() => WindowsIntegration.SetAutostart(old.StartWithWindows, Environment.ProcessPath!));
            SettingsStore.Save(settingsPath, next); settings = next with { }; UpdateTray();
        }
        catch (Exception original)
        {
            var failed = new List<string>();
            foreach (var step in undo) try { step(); } catch (Exception e) { failed.Add(e.Message); }
            throw new InvalidOperationException(original.Message + (failed.Count == 0 ? "" : " Rollback failed: " + string.Join("; ", failed)), original);
        }
    }
    void StatusChanged(object? sender, EventArgs args) => UpdateTray();
    // Background failures (export, unexpected stop) only show up as a status text: make them heard.
    void EngineFailed(object? sender, string message) => Fail(message);
    // Quick share (tray/settings): stop a running share, otherwise share the last saved clip as it is.
    void ToggleShare()
    {
        if (sharer == null) return;
        if (sharer.Current != null || sharer.IsStarting) { sharer.Stop(); Cue(Sound.ShareStopped); return; }
        if (LastClip is { } clip) _ = ShareClip(clip); else Fail("No clip saved yet.");
    }
    // The last saved clip, or the newest one on disk.
    string? LastClip => lastClip != null && File.Exists(lastClip) ? lastClip : ClipLibrary.LatestClip(settings.ClipsDirectory);
    // "Export clip" opens the editor with the last clip; without one it opens empty, ready to choose a clip.
    void EditLastClip() => Export(LastClip);
    // A file the user picked or dropped on the settings window. An open editor takes it as it is (and says itself what is
    // wrong with it); otherwise only a file that can be shared opens the editor.
    internal void ExportFile(string file)
    {
        if (editor == null) { try { SharePolicy.CheckShareable(file); } catch (ArgumentException e) { Fail(e.Message); return; } }
        Export(file);
    }
    // What the export editor decided: the edit, a file to save it to (null: share it) and the clip it applies to
    // (the user can switch clips in the editor).
    internal sealed record ExportChoice(ClipEdit Edit, string? SavePath, string? Clip = null);
    // Replaces the export editor in tests: gets the starting clip, returns the choice or null when cancelled.
    internal Func<string?, ExportChoice?>? EditClip { get; set; }
    ShareEditorForm? editor;
    // The editor trims the clip and picks its audio, then the clip is shared or saved as an MP4. A clip chosen
    // while the editor is open replaces the one in it.
    void Export(string? clip)
    {
        if (editor != null) { if (clip != null) editor.Open(clip); editor.Activate(); return; }
        ExportChoice? choice; TimeSpan duration = TimeSpan.MaxValue; int? defaultTrack = 0;
        if (EditClip != null) choice = EditClip(clip) is { } c ? c with { Clip = c.Clip ?? clip } : null;
        else
        {
            using (editor = new ShareEditorForm(clip, settings.ClipsDirectory, settings.ExportFolder(), ClipTitle))
            {
                try
                {
                    choice = editor.ShowDialog() == DialogResult.OK && editor.Edit is { } edit && editor.Clip is { } picked ? new(edit, editor.SavePath, picked) : null;
                    if (editor.Media is { } media) { duration = media.Duration; defaultTrack = ClipAudio.DefaultTrack(media.AudioTracks); }
                }
                finally { editor = null; }
            }
        }
        if (choice?.Clip is not { } exported) return;
        if (choice.SavePath != null) { _ = SaveEdited(exported, choice.Edit, choice.SavePath); return; }
        // Untouched clips are shared unchanged (all tracks, no trim pass).
        _ = ShareClip(exported, choice.Edit.ChangesClip(duration, defaultTrack) ? choice.Edit : null);
    }
    // Writes the edited clip (lossless) and remembers the folder for the next export.
    async Task SaveEdited(string clip, ClipEdit edit, string path)
    {
        form?.Feedback("Saving " + Path.GetFileName(path) + "…");
        try
        {
            await Task.Run(() => ClipTrim.Create(clip, path, edit));
            if (Path.GetDirectoryName(path) is { } folder && !string.Equals(folder, settings.ExportDirectory, StringComparison.OrdinalIgnoreCase))
            {
                settings = settings with { ExportDirectory = folder }; form?.KeepExportDirectory(folder);
                try { SettingsStore.Save(settingsPath, settings); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            Cue(Sound.Clip); form?.Feedback("Saved: " + path); WindowsIntegration.TrimMemory();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { Fail("Export failed: " + e.Message); }
    }
    // Creates the link and copies it. Sounds: upload on start, link when copied, error otherwise.
    internal async Task ShareClip(string clip, ClipEdit? edit = null)
    {
        if (sharer == null) return;
        int minutes = settings.ShareMinutes;
        Cue(Sound.Upload); form?.Feedback(sharer.FirstUse ? "Creating link… (first time: downloading cloudflared, ~55 MB)" : $"Creating link… ({minutes} min)");
        try
        {
            var progress = new Progress<string>(text => { form?.Feedback(text); SetTooltip("MonoClip · " + text); });
            var info = await sharer.ShareAsync(clip, TimeSpan.FromMinutes(minutes), progress, new(settings.DirectLink, ClipTitle(clip), edit));
            lastShare = info; lastShareMinutes = minutes;
            if (settings.CopyShareLink) try { CopyText(info.Url); } catch (ExternalException) { }
            Cue(Sound.Link);
            form?.Feedback($"{(settings.CopyShareLink ? "Link copied" : "Link ready")}. Online until {info.ExpiresAt:HH:mm} ({minutes} min).");
        }
        catch (OperationCanceledException) { form?.Feedback("Sharing cancelled."); }
        catch (Exception e) { Fail("Sharing failed: " + e.Message); }
        finally { UpdateTray(); WindowsIntegration.TrimMemory(); }
    }
    // "Valorant clip" for clips sorted into a game folder, otherwise a generic title.
    internal string ClipTitle(string clip)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(clip)); var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(settings.ClipsDirectory));
        return folder != null && string.Equals(Path.GetDirectoryName(folder), root, StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(folder) + " clip" : "MonoClip clip";
    }
    void CopyShareLink() { if (sharer?.Current is not { } c) return; CopyText(c.Url); form?.Feedback($"Link copied. Online until {c.ExpiresAt:HH:mm}."); }
    void ShareChanged(object? sender, EventArgs args)
    {
        if (disposed || sharer == null) return;
        if (lastShare != null && sharer.Current == null && !sharer.IsStarting)
        {
            bool expired = DateTimeOffset.Now >= lastShare.ExpiresAt.AddSeconds(-5);
            form?.Feedback(expired ? $"Link expired after {lastShareMinutes} min." : "Sharing stopped. The link is offline."); lastShare = null;
        }
        if (sharer.Current != null) shareTicker.Start(); else shareTicker.Stop();
        UpdateTray();
    }
    void UpdateShareItems()
    {
        if (sharer == null) return;
        shareItem.Text = sharer.IsStarting ? "Cancel sharing" : sharer.Current is { } c ? $"Stop sharing ({c.MinutesLeft} min left)" : $"Quick share last clip ({settings.ShareMinutes} min)";
        copyLinkItem.Visible = sharer.Current != null;
    }
    void ClipSaved(object? sender, ClipSavedEventArgs args)
    {
        lastClip = args.Clip.Path; Cue(Sound.Clip); form?.Feedback("Saved: " + args.Clip.Path);
        // Auto-share replaces a link that is still online: the newest clip is the one to post.
        if (settings.AutoShare && sharer != null) { sharer.Stop(); _ = ShareClip(args.Clip.Path); }
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
        SetTooltip(errorText != null ? "MonoClip · " + errorText : "MonoClip · " + (sharer?.IsStarting == true ? "Creating link · " : sharer?.Current is { } shared ? $"shared, {shared.MinutesLeft} min left · " : "") + engine.Status);
        toggleItem.Text = active ? "Stop buffer" : "Start buffer"; saveItem.Text = $"Save last {settings.ClipSeconds} s"; saveItem.Enabled = active; UpdateShareItems(); form?.UpdateStatus();
    }
    void Run(Action action) { try { action(); } catch (Exception e) { Fail(e.Message); } UpdateTray(); }
    protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            engine.Dispose(); disposed = true; engine.StatusChanged -= StatusChanged; engine.Failed -= EngineFailed; engine.ClipSaved -= ClipSaved; if (sharer != null) { sharer.Changed -= ShareChanged; sharer.Dispose(); }
            shareTicker.Dispose(); animation.Dispose(); hotkeys.Dispose(); form?.Dispose(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
            foreach (var icon in icons.Values.Concat(spinner)) icon.Dispose(); sounds.Dispose();
        }
        base.Dispose(disposing);
    }
}
