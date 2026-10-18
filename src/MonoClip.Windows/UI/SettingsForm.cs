using MonoClip.Core;
namespace MonoClip.Windows.UI;

// The settings popup, opened from the tray like Mullvad's: it appears right above the taskbar next to the tray with
// the everyday actions and the basic settings; the gear opens every setting on a second page. The controls edit one
// draft (SettingsBinder), which is applied by itself after a short pause, as some changes restart the buffer; so
// clicking elsewhere simply closes the popup.
public sealed class SettingsForm : Form
{
    const int LogicalWidth = 360, ScreenMargin = 12, CommitDelay = 500;
    static readonly (string, (int, int))[] Resolutions = [("1280 × 720", (1280, 720)), ("1920 × 1080", (1920, 1080)), ("2560 × 1440", (2560, 1440)), ("3840 × 2160", (3840, 2160))];
    static readonly (string, int)[] FrameRates = [("30", 30), ("60", 60), ("120", 120)];
    AppSettings original; readonly SettingsBinder binder; readonly IClipEngine engine; readonly Action<AppSettings> saveSettings; readonly Action toggle, saveClip;
    readonly ComboBox resolution = new MonoComboBox(), fps = new MonoComboBox(), games = new MonoComboBox();
    // Clip length, hotkey, folder and autostart have a control on each page (the "2" ones on the settings page).
    readonly NumericUpDown duration = new MonoNumeric(), duration2 = new MonoNumeric(), shareMinutes = new MonoNumeric();
    readonly TextBox hotkey = new(), hotkey2 = new(), directory = new(), directory2 = new(), screenshotHotkey = new(), screenshotDirectory = new();
    readonly CheckBox autostart = new MonoToggle(), autostart2 = new MonoToggle(), separateTracks = new MonoToggle(), mic = new MonoToggle(), desktop = new MonoToggle(), minimized = new MonoToggle(),
        bufferOnLaunch = new MonoToggle(), clipBeep = new MonoToggle(), autoShare = new MonoToggle(), sortByApp = new MonoToggle(), directLink = new MonoToggle(), copyLink = new MonoToggle();
    readonly MonoSlider quality = new("Performance", "Balanced", "Quality");
    readonly Button toggleButton = new MonoButton(), clipButton = new MonoButton(), shareButton = new MonoButton(), editButton = new MonoButton(), copyLinkButton = new MonoButton(),
        openButton = new MonoButton(), pickButton = new MonoButton(), gear = new MonoButton(), back = new MonoButton(), openScreenshots = new MonoButton(), defaultScreenshots = new MonoButton();
    readonly Label status = new(), target = new(), budget = new(), budgetDetail = new(), shareStatus = new(), feedback = new(), moreFeedback = new();
    readonly Page home = new(), more = new(); readonly ButtonRow shareRow;
    readonly System.Windows.Forms.Timer shareTicker = new() { Interval = 15000 }, commitTimer = new() { Interval = CommitDelay };
    IClipSharer? sharer; Action<string>? shareFile; Point? anchor; bool popup; int dialogs;

    public SettingsForm(AppSettings settings, IClipEngine engine, Action<AppSettings> saveSettings, Action toggle, Action saveClip)
    {
        original = settings with { }; binder = new(settings with { }); this.engine = engine; this.saveSettings = saveSettings; this.toggle = toggle; this.saveClip = saveClip;
        Text = "MonoClip"; BackColor = Look.Page; ForeColor = Look.Text; Font = Look.Body(); AutoScaleMode = AutoScaleMode.Dpi; Icon = WindowChrome.AppIcon();
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; KeyPreview = true; TopMost = true;
        ClientSize = new Size(Px(LogicalWidth), Px(560));
        Controls.Add(more); Controls.Add(home); more.Visible = false;

        // Home: status, actions, the basic settings.
        IconButton(gear, "", "Settings"); gear.Click += (_, _) => ShowMore();
        home.Add(new Header("MONOCLIP", 12, right: gear), 0);
        status.Font = Look.Strong(10); home.Add(status, 4); target.ForeColor = Look.Muted; home.Add(target, 2);
        Style(toggleButton, "Start buffer"); toggleButton.Click += (_, _) => Run(toggle);
        Style(clipButton, "Save clip", primary: true); clipButton.Click += (_, _) => Run(saveClip);
        home.Add(new ButtonRow(toggleButton, clipButton), 12);
        Style(shareButton, "Quick share"); Style(copyLinkButton, "Copy link"); Style(editButton, "Export clip");
        shareRow = home.Add(new ButtonRow(shareButton, copyLinkButton, editButton)); home.SetShown(shareRow, false); shareRow.SetShown(copyLinkButton, false);
        shareStatus.ForeColor = Look.Muted; home.Add(shareStatus, 4);
        home.Section("CLIPS");
        ClipBasics(home, duration, hotkey, directory);
        Style(openButton, "Open folder"); openButton.Click += (_, _) => Run(() => WindowsIntegration.OpenClipFolder(games.SelectedIndex > 0 ? Path.Combine(original.ClipsDirectory, games.Text) : original.ClipsDirectory));
        Style(pickButton, "Export a file…"); pickButton.Enabled = false; pickButton.Click += (_, _) => PickAndShare();
        home.Add(new ButtonRow(openButton, pickButton));
        budget.ForeColor = Look.Muted; home.Add(budget, 6);
        home.Section("STARTUP"); Toggle(home, "Start with Windows", autostart, s => s.StartWithWindows, (s, v) => s with { StartWithWindows = v });
        feedback.ForeColor = Look.Muted; home.Add(feedback, 10);

        // Settings page: every setting.
        IconButton(back, "", "Back"); back.Click += (_, _) => ShowHome();
        more.Add(new Header("Settings", 11, left: back), 0);
        more.Section("RECORDING");
        StyleCombo(resolution); binder.Choice(resolution, Resolutions, s => (s.Width, s.Height), (s, v) => s with { Width = v.Item1, Height = v.Item2 }); more.Add(new Card("Resolution", resolution, 130));
        StyleCombo(fps); binder.Choice(fps, FrameRates, s => s.Fps, (s, v) => s with { Fps = v }); more.Add(new Card("Frame rate", fps, 130));
        quality.Font = new Font("Segoe UI", 8.5f); quality.AccessibleName = "Quality"; binder.Steps(quality, s => (int)s.Quality, (s, v) => s with { Quality = (QualityPreset)v }); more.Add(new Card("Quality", quality, 230));
        budgetDetail.ForeColor = Look.Muted; more.Add(budgetDetail, 6);
        more.Section("AUDIO");
        Toggle(more, "Record desktop audio", desktop, s => s.DesktopAudio, (s, v) => s with { DesktopAudio = v });
        Toggle(more, "Record microphone", mic, s => s.Microphone, (s, v) => s with { Microphone = v });
        Toggle(more, "Separate desktop and mic tracks", separateTracks, s => s.SeparateAudioTracks, (s, v) => s with { SeparateAudioTracks = v });
        more.Note("Default Windows devices. With both recorded, separate tracks let Export mute one of them; off saves a little CPU.");
        Toggle(more, "Sound effects", clipBeep, s => s.ClipBeep, (s, v) => s with { ClipBeep = v });
        more.Section("CLIPS");
        ClipBasics(more, duration2, hotkey2, directory2);
        Toggle(more, "Sort clips by application", sortByApp, s => s.SortByApp, (s, v) => s with { SortByApp = v });
        StyleCombo(games); games.Items.Add("All clips"); games.SelectedIndex = 0; more.Add(new Card("Open folder shows", games, 150));
        more.Section("SCREENSHOTS");
        HotkeyBox(screenshotHotkey, "Screenshot hotkey", s => s.ScreenshotHotkey, (s, v) => s with { ScreenshotHotkey = v });
        more.Add(new Card("Screenshot hotkey", new Field(screenshotHotkey), 130));
        // Shows the folder in use. The default (Screenshots in the clip folder) is stored as empty, so it moves along with
        // the clip folder; picking that same folder keeps it so.
        screenshotDirectory.ReadOnly = true; screenshotDirectory.Cursor = Cursors.Hand;
        binder.Text(screenshotDirectory, s => s.ScreenshotFolder(), (s, v) => s with { ScreenshotsDirectory = v == (s with { ScreenshotsDirectory = "" }).ScreenshotFolder() ? "" : v });
        screenshotDirectory.Click += (_, _) => BrowseScreenshots(); screenshotDirectory.KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) BrowseScreenshots(); };
        var screenshotField = new Field(screenshotDirectory); screenshotField.Click += (_, _) => BrowseScreenshots(); more.Add(new Card("Folder", screenshotField, stretch: true));
        Style(openScreenshots, "Open folder"); openScreenshots.Click += (_, _) => Run(() => WindowsIntegration.OpenClipFolder(original.ScreenshotFolder()));
        Style(defaultScreenshots, "Use clip folder"); defaultScreenshots.Click += (_, _) => { screenshotDirectory.Text = ""; Commit(); };
        more.Add(new ButtonRow(openScreenshots, defaultScreenshots));
        more.Note("Saves the monitor you are on as a PNG.");
        more.Section("SHARING");
        Toggle(more, "Share new clips automatically", autoShare, s => s.AutoShare, (s, v) => s with { AutoShare = v });
        Toggle(more, "Copy new links to the clipboard", copyLink, s => s.CopyShareLink, (s, v) => s with { CopyShareLink = v });
        Toggle(more, "Link to the video file, no player page", directLink, s => s.DirectLink, (s, v) => s with { DirectLink = v });
        shareMinutes.Minimum = SharePolicy.MinMinutes; shareMinutes.Maximum = SharePolicy.MaxMinutes; shareMinutes.Increment = 5; shareMinutes.AccessibleName = "Link lifetime in minutes";
        binder.Number(shareMinutes, s => s.ShareMinutes, (s, v) => s with { ShareMinutes = v }); more.Add(new Card("Link lifetime (min)", new Stepper(shareMinutes)));
        more.Note($"MKV or MP4, up to {SharePolicy.MaxShareMegabytes / 1024} GB.");
        more.Section("STARTUP");
        Toggle(more, "Start with Windows", autostart2, s => s.StartWithWindows, (s, v) => s with { StartWithWindows = v });
        Toggle(more, "Start in tray", minimized, s => s.StartMinimized, (s, v) => s with { StartMinimized = v });
        Toggle(more, "Start buffer on launch", bufferOnLaunch, s => s.StartBufferOnLaunch, (s, v) => s with { StartBufferOnLaunch = v });
        moreFeedback.ForeColor = Look.Muted; more.Add(moreFeedback, 10);

        // Every edit is applied after a short pause; a few in a row are applied together.
        binder.Edited += () => { UpdateBudget(); commitTimer.Stop(); commitTimer.Start(); };
        commitTimer.Tick += (_, _) => Commit(); shareTicker.Tick += (_, _) => UpdateShareControls();
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Commit(); Hide(); } };
        // The share countdown only needs updating while the popup is open.
        VisibleChanged += (_, _) => { if (Visible) { RefreshGames(); UpdateStatus(); if (sharer != null) shareTicker.Start(); } else { Commit(); shareTicker.Stop(); } };
        DpiChanged += (_, _) => BeginInvoke(() => { if (popup && Visible) Place(); });
        home.HandleCreated += (_, _) => WindowChrome.DarkScrollbars(home.Handle); more.HandleCreated += (_, _) => WindowChrome.DarkScrollbars(more.Handle);
        EnableDrop(this);
        binder.Push(); engine.StatusChanged += EngineStatus; UpdateBudget(); UpdateStatus();
    }

    int Px(int logical) => LogicalToDeviceUnits(logical);
    static void Style(Button b, string text, bool primary = false)
    {
        b.Text = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.UseVisualStyleBackColor = false; b.Cursor = Cursors.Hand;
        b.BackColor = primary ? Look.Text : Look.Card; b.ForeColor = primary ? Look.Page : Look.Text;
    }
    static void IconButton(Button b, string glyph, string name) { Style(b, glyph); b.BackColor = Look.Page; b.ForeColor = Look.Muted; b.Font = Look.Icons(11); b.AccessibleName = name; }
    void Toggle(Page page, string text, CheckBox box, Func<AppSettings, bool> get, Func<AppSettings, bool, AppSettings> set) { box.AccessibleName = text; binder.Toggle(box, get, set); page.Add(new Card(text, box)); }
    // Clip length, save hotkey and folder: on home, and again on the settings page.
    void ClipBasics(Page page, NumericUpDown length, TextBox key, TextBox folder)
    {
        length.Minimum = AppSettings.MinClipSeconds; length.Maximum = AppSettings.MaxClipSeconds; binder.Number(length, s => s.ClipSeconds, (s, v) => s with { ClipSeconds = v });
        page.Add(new Card("Clip length (s)", new Stepper(length)));
        HotkeyBox(key, "Clip hotkey", s => s.Hotkey, (s, v) => s with { Hotkey = v });
        page.Add(new Card("Save hotkey", new Field(key), 130));
        // The folder is chosen with the folder picker; typing a path would apply half-typed folders.
        folder.ReadOnly = true; folder.Cursor = Cursors.Hand; binder.Text(folder, s => s.ClipsDirectory, (s, v) => s with { ClipsDirectory = v });
        folder.Click += (_, _) => Browse(); folder.KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) Browse(); };
        var field = new Field(folder); field.Click += (_, _) => Browse(); page.Add(new Card("Folder", field, stretch: true));
    }
    // A hotkey box records a key combination instead of taking typed text.
    void HotkeyBox(TextBox key, string name, Func<AppSettings, string> get, Func<AppSettings, string, AppSettings> set)
    {
        key.ReadOnly = true; key.TextAlign = HorizontalAlignment.Center; key.AccessibleName = name; key.Cursor = Cursors.Hand; binder.Text(key, get, set);
        key.KeyDown += (_, e) => { e.SuppressKeyPress = true; if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.Escape or Keys.Tab) return; uint mods = Hotkey.NoRepeat | (e.Control ? 2u : 0) | (e.Shift ? 4u : 0) | (e.Alt ? 1u : 0); key.Text = new Hotkey(mods, e.KeyCode).ToString(); };
    }
    static void StyleCombo(ComboBox c)
    {
        c.DropDownStyle = ComboBoxStyle.DropDownList; c.BackColor = Look.Field; c.ForeColor = Look.Text; c.FlatStyle = FlatStyle.Flat;
        c.DrawMode = DrawMode.OwnerDrawFixed; (c as MonoComboBox)?.FitItemHeight();
        c.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            // The closed box shows its value on the field colour; only the open list highlights a row.
            bool highlight = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using var background = new SolidBrush(highlight ? Look.Text : Look.Field); e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(e.Graphics, c.Items[e.Index]?.ToString() ?? "", c.Font, Rectangle.Inflate(e.Bounds, -4, 0), highlight ? Look.Page : Look.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
    }

    // Shows the popup above the taskbar next to anchor (the tray icon the user clicked), or by the tray corner.
    public void ShowNear(Point? anchor)
    {
        popup = true; this.anchor = anchor; ShowHome(); Place();
        // Without the foreground the popup would not get the focus, and so never close by itself.
        if (!Visible) Show(); WindowChrome.TakeForeground(Handle); Activate(); BeginInvoke(Place); // a monitor with other scaling resizes the window once shown
    }
    void Place()
    {
        var screen = anchor is { } a ? Screen.FromPoint(a) : Screen.PrimaryScreen!;
        home.PerformLayout(); var size = new Size(Px(LogicalWidth), home.ContentHeight);
        var bounds = PopupBounds(size, anchor ?? new Point(screen.WorkingArea.Right, screen.WorkingArea.Bottom), screen.Bounds, screen.WorkingArea, Px(ScreenMargin));
        if (Bounds != bounds) Bounds = bounds;
    }
    // Next to the taskbar, wherever it is docked: centred on the anchor along it, inside the work area.
    internal static Rectangle PopupBounds(Size size, Point anchor, Rectangle screen, Rectangle work, int margin)
    {
        int w = Math.Min(size.Width, work.Width - 2 * margin), h = Math.Min(size.Height, work.Height - 2 * margin);
        int x = Math.Clamp(anchor.X - w / 2, work.Left + margin, work.Right - margin - w), y = Math.Clamp(anchor.Y - h / 2, work.Top + margin, work.Bottom - margin - h);
        if (work.Top > screen.Top) return new(x, work.Top + margin, w, h);
        if (work.Left > screen.Left) return new(work.Left + margin, y, w, h);
        if (work.Right < screen.Right) return new(work.Right - margin - w, y, w, h);
        return new(x, work.Bottom - margin - h, w, h);
    }
    void ShowMore() { more.Visible = true; home.Visible = false; more.AutoScrollPosition = Point.Empty; back.Focus(); }
    void ShowHome() { home.Visible = true; more.Visible = false; }
    // Like a tray flyout: clicking anywhere else closes it (not while one of its own dialogs is open).
    protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); if (popup && dialogs == 0 && Visible) { Commit(); Hide(); } }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { if (more.Visible) ShowHome(); else { Commit(); Hide(); } return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ClassStyle |= WindowChrome.DropShadow; return p; } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); WindowChrome.RoundedFrame(Handle); }

    // Applies the draft. Invalid values (or a hotkey that is taken) are reported and not applied.
    void Commit()
    {
        commitTimer.Stop();
        AppSettings next;
        try { next = ReadSettings(); } catch (Exception e) when (e is ArgumentException or FormatException or InvalidOperationException) { Feedback(e.Message); return; }
        if (next == original) return;
        try { saveSettings(next); original = next with { }; Feedback(""); } catch (Exception e) { Feedback(e.Message); }
        UpdateStatus();
    }
    void Browse()
    {
        var picked = Dialog(() => { using var dialog = new FolderBrowserDialog { InitialDirectory = directory.Text, Description = "Clip folder", UseDescriptionForTitle = true }; return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null; });
        if (picked != null && picked != directory.Text) { directory.Text = picked; Commit(); }
    }
    void BrowseScreenshots()
    {
        var current = original.ScreenshotFolder();
        var picked = Dialog(() => { using var dialog = new FolderBrowserDialog { InitialDirectory = Directory.Exists(current) ? current : original.ClipsDirectory, Description = "Screenshot folder", UseDescriptionForTitle = true }; return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null; });
        if (picked != null && picked != screenshotDirectory.Text) { screenshotDirectory.Text = picked; Commit(); }
    }
    // Own dialogs take the focus; the popup must stay open behind them.
    T Dialog<T>(Func<T> show) { dialogs++; try { return show(); } finally { dialogs--; } }

    void EngineStatus(object? sender, EventArgs args) { if (!IsDisposed) UpdateStatus(); }
    // True while the user records a new hotkey here (in front, a hotkey box focused): the old ones must not fire.
    internal bool RecordingHotkey => Visible && WindowChrome.IsForeground(Handle) && (hotkey.Focused || hotkey2.Focused || screenshotHotkey.Focused);
    // The draft as it would be applied (hotkeys in their canonical spelling), or ArgumentException when invalid.
    public AppSettings ReadSettings()
    {
        var d = binder.Draft;
        var s = d with { Hotkey = Hotkey.Parse(d.Hotkey).ToString(), ScreenshotHotkey = Hotkey.Parse(d.ScreenshotHotkey).ToString(), ClipsDirectory = d.ClipsDirectory.Trim(), ScreenshotsDirectory = d.ScreenshotsDirectory.Trim() };
        s.Validate(); return s;
    }
    void UpdateBudget()
    {
        var s = binder.Draft; var clip = FormatSize(CapturePolicy.EstimatedClipMegabytes(s));
        budget.Text = $"≈ {clip} per clip";
        budgetDetail.Text = $"{CapturePolicy.BitrateKbps(s) / 1000d:0.#} Mbit/s video + {CapturePolicy.AudioTracks(s)} × {CapturePolicy.AudioKbpsPerTrack} kbit/s audio · ≈ {clip} per clip · RAM buffer up to {CapturePolicy.BufferMegabytes(s)} MB";
    }
    internal static string FormatSize(double megabytes) => megabytes >= 1024 ? $"{megabytes / 1024:0.0} GB" : megabytes >= 10 ? $"{megabytes:0} MB" : $"{megabytes:0.0} MB";
    public void UpdateStatus()
    {
        status.Text = (engine.IsRunning ? "●  " : "○  ") + engine.Status; target.Text = engine.CaptureTarget + "  ·  " + engine.EncoderName;
        toggleButton.Text = engine.IsRunning ? "Stop buffer" : "Start buffer"; clipButton.Enabled = engine.IsRunning; UpdateShareControls();
    }
    // Called by the tray: sharing lives there so the menu and this window always show the same link.
    // toggleShare: quick share or stop; editShare: open the export editor for the last clip; shareFile: editor for a file.
    public void AttachSharing(IClipSharer sharer, Action toggleShare, Action editShare, Action copyLink, Action<string> shareFile)
    {
        this.sharer = sharer; this.shareFile = shareFile; pickButton.Enabled = true;
        shareButton.Click += (_, _) => toggleShare(); editButton.Click += (_, _) => editShare(); copyLinkButton.Click += (_, _) => copyLink(); if (Visible) shareTicker.Start(); UpdateShareControls();
    }
    // Drop a clip anywhere on the window to open it in the export editor (same checks as "Export a file…").
    void EnableDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) => { var file = shareFile != null ? DroppedFile(e.Data) : null; e.Effect = file != null ? DragDropEffects.Copy : DragDropEffects.None; if (file != null) Feedback("Drop to export " + Path.GetFileName(file)); };
        control.DragDrop += (_, e) => { if (DroppedFile(e.Data) is { } file) DropFiles([file]); };
        foreach (Control child in control.Controls) EnableDrop(child);
    }
    static string? DroppedFile(IDataObject? data) => data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files ? files[0] : null;
    // The editor opens after the drop has finished, so Explorer is not kept waiting while it is open.
    internal void DropFiles(string[] files) { if (shareFile is { } open && files.Length == 1) { var file = files[0]; if (IsHandleCreated) BeginInvoke(() => open(file)); else open(file); } }
    void PickAndShare()
    {
        var picked = Dialog(() => { using var dialog = new OpenFileDialog { Title = "Choose a clip to export", Filter = "Videos (*.mkv;*.mp4)|*.mkv;*.mp4", InitialDirectory = Directory.Exists(original.ClipsDirectory) ? original.ClipsDirectory : "", CheckFileExists = true }; return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null; });
        if (picked != null) shareFile?.Invoke(picked);
    }
    void UpdateShareControls()
    {
        if (sharer == null || IsDisposed) return;
        home.SetShown(shareRow, true);
        // An empty status line takes no room on the page.
        if (sharer.IsStarting) { shareButton.Text = "Cancel sharing"; shareStatus.Text = "Creating link…"; }
        else if (sharer.Current is { } c)
        {
            shareButton.Text = "Stop sharing"; shareStatus.Text = $"Shared · {c.MinutesLeft} min left (until {c.ExpiresAt:HH:mm})\n{new Uri(c.Url).Host}";
        }
        else { shareButton.Text = $"Quick share ({original.ShareMinutes} min)"; shareStatus.Text = ""; }
        shareRow.SetShown(copyLinkButton, sharer.Current != null);
        if (popup && Visible) Place(); // the popup grows or shrinks with the share line, keeping its place by the tray
    }
    public void Feedback(string text) { feedback.Text = text; moreFeedback.Text = text; }
    // The tray saved a new export folder: keep it when the draft is applied later.
    internal void KeepExportDirectory(string folder) { original = original with { ExportDirectory = folder }; binder.Draft = binder.Draft with { ExportDirectory = folder }; }
    void Run(Action action) { try { action(); UpdateStatus(); } catch (Exception e) { Feedback(e.Message); } }
    void RefreshGames() { Run(() => { var chosen = games.Text; games.Items.Clear(); games.Items.Add("All clips"); foreach (var g in ClipLibrary.GetGames(original.ClipsDirectory)) games.Items.Add(g); games.SelectedIndex = Math.Max(0, games.Items.IndexOf(chosen)); }); }
    protected override void Dispose(bool disposing) { if (disposing) { engine.StatusChanged -= EngineStatus; shareTicker.Dispose(); commitTimer.Dispose(); } base.Dispose(disposing); }
}
