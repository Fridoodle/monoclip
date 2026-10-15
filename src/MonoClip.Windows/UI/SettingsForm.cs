using MonoClip.Core;
using System.Runtime.InteropServices;
namespace MonoClip.Windows.UI;

// The settings popup, opened from the tray like Mullvad's: it appears right above the taskbar next to the tray with
// the everyday actions and the basic settings; the gear opens everything else on a second page. Changes apply at
// once (after a short pause, as some restart the buffer), so clicking elsewhere simply closes the popup.
public sealed class SettingsForm : Form
{
    const int LogicalWidth = 360, ScreenMargin = 12, CommitDelay = 500;
    AppSettings original; readonly IClipEngine engine; readonly Action<AppSettings> saveSettings; readonly Action toggle, saveClip;
    readonly ComboBox resolution = new MonoComboBox(), fps = new MonoComboBox(), games = new MonoComboBox();
    readonly NumericUpDown duration = new MonoNumeric(), shareMinutes = new MonoNumeric(); readonly TextBox hotkey = new(), directory = new();
    // The basics again on the settings page, so that page has every setting; each is kept in step with home's.
    readonly NumericUpDown duration2 = new MonoNumeric(); readonly TextBox hotkey2 = new(), directory2 = new(); readonly CheckBox autostart2 = new MonoToggle();
    readonly CheckBox separateTracks = new MonoToggle(), mic = new MonoToggle(), desktop = new MonoToggle(), minimized = new MonoToggle(), autostart = new MonoToggle(), bufferOnLaunch = new MonoToggle(),
        clipBeep = new MonoToggle(), autoShare = new MonoToggle(), sortByApp = new MonoToggle(), directLink = new MonoToggle(), copyLink = new MonoToggle();
    readonly MonoSlider quality = new("Performance", "Balanced", "Quality");
    readonly Button toggleButton = new MonoButton(), clipButton = new MonoButton(), shareButton = new MonoButton(), editButton = new MonoButton(), copyLinkButton = new MonoButton(),
        openButton = new MonoButton(), pickButton = new MonoButton(), gear = new MonoButton(), back = new MonoButton();
    readonly Label status = new(), target = new(), budget = new(), budgetDetail = new(), shareStatus = new(), feedback = new(), moreFeedback = new();
    readonly Page home = new(), more = new(); readonly ButtonRow shareRow;
    readonly System.Windows.Forms.Timer shareTicker = new() { Interval = 15000 }, commitTimer = new() { Interval = CommitDelay };
    IClipSharer? sharer; Action<string>? shareFile; Point? anchor; bool loading = true, popup; int dialogs;

    public SettingsForm(AppSettings settings, IClipEngine engine, Action<AppSettings> saveSettings, Action toggle, Action saveClip)
    {
        original = settings with { }; this.engine = engine; this.saveSettings = saveSettings; this.toggle = toggle; this.saveClip = saveClip;
        Text = "MonoClip"; BackColor = Look.Page; ForeColor = Look.Text; Font = Look.Body(); AutoScaleMode = AutoScaleMode.Dpi; Icon = AppIcon();
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; KeyPreview = true; TopMost = true;
        ClientSize = new Size(Px(LogicalWidth), Px(560));
        Controls.Add(more); Controls.Add(home); more.Visible = false;

        // Home: status, actions, the basic settings.
        IconButton(gear, "\uE713", "Settings"); gear.Click += (_, _) => ShowMore();
        home.Add(new Header("MONOCLIP", gear), 0);
        status.Font = Look.Strong(10); home.Add(status, 4); target.ForeColor = Look.Muted; home.Add(target, 2);
        Style(toggleButton, "Start buffer"); toggleButton.Click += (_, _) => Run(toggle);
        Style(clipButton, "Save clip", primary: true); clipButton.Click += (_, _) => Run(saveClip);
        home.Add(new ButtonRow(toggleButton, clipButton), 12);
        Style(shareButton, "Quick share"); Style(copyLinkButton, "Copy link"); Style(editButton, "Export clip");
        shareRow = home.Add(new ButtonRow(shareButton, copyLinkButton, editButton)); Look.Show(shareRow, false); Look.Show(copyLinkButton, false);
        shareStatus.ForeColor = Look.Muted; home.Add(shareStatus, 4);
        home.Section("CLIPS");
        ClipBasics(home, duration, hotkey, directory, settings);
        Style(openButton, "Open folder"); openButton.Click += (_, _) => Run(() => WindowsIntegration.OpenClipFolder(games.SelectedIndex > 0 ? Path.Combine(original.ClipsDirectory, games.Text) : original.ClipsDirectory));
        Style(pickButton, "Export a file…"); pickButton.Enabled = false; pickButton.Click += (_, _) => PickAndShare();
        home.Add(new ButtonRow(openButton, pickButton));
        budget.ForeColor = Look.Muted; home.Add(budget, 6);
        home.Section("STARTUP"); Toggle(home, "Start with Windows", autostart, settings.StartWithWindows);
        feedback.ForeColor = Look.Muted; home.Add(feedback, 10);

        // Settings page: everything else.
        IconButton(back, "\uE72B", "Back"); back.Tag = "back"; back.Click += (_, _) => ShowHome();
        more.Add(new Header("Settings", null, back), 0);
        more.Section("RECORDING");
        StyleCombo(resolution); resolution.Items.AddRange(["1280 × 720", "1920 × 1080", "2560 × 1440", "3840 × 2160"]); resolution.SelectedIndex = settings.Width switch { 1280 => 0, 1920 => 1, 2560 => 2, _ => 3 }; more.Add(new Card("Resolution", resolution, 130));
        StyleCombo(fps); fps.Items.AddRange([30, 60, 120]); fps.SelectedItem = settings.Fps; more.Add(new Card("Frame rate", fps, 130));
        quality.Font = new Font("Segoe UI", 8.5f); quality.Value = (int)settings.Quality; quality.AccessibleName = "Quality"; quality.AccessibleDescription = quality.ValueText; more.Add(new Card("Quality", quality, 230));
        budgetDetail.ForeColor = Look.Muted; more.Add(budgetDetail, 6);
        more.Section("AUDIO");
        Toggle(more, "Record desktop audio", desktop, settings.DesktopAudio); Toggle(more, "Record microphone", mic, settings.Microphone);
        Toggle(more, "Separate desktop and mic tracks", separateTracks, settings.SeparateAudioTracks);
        more.Note("Default Windows devices. With both recorded, separate tracks let Export mute one of them; off saves a little CPU.");
        Toggle(more, "Sound effects", clipBeep, settings.ClipBeep);
        more.Section("CLIPS");
        ClipBasics(more, duration2, hotkey2, directory2, settings);
        Toggle(more, "Sort clips by application", sortByApp, settings.SortByApp);
        StyleCombo(games); games.Items.Add("All clips"); games.SelectedIndex = 0; more.Add(new Card("Open folder shows", games, 150));
        more.Section("SHARING");
        Toggle(more, "Share new clips automatically", autoShare, settings.AutoShare);
        Toggle(more, "Copy new links to the clipboard", copyLink, settings.CopyShareLink);
        Toggle(more, "Link to the video file, no player page", directLink, settings.DirectLink);
        shareMinutes.Minimum = SharePolicy.MinMinutes; shareMinutes.Maximum = SharePolicy.MaxMinutes; shareMinutes.Increment = 5; shareMinutes.Value = settings.ShareMinutes; shareMinutes.AccessibleName = "Link lifetime in minutes";
        more.Add(new Card("Link lifetime (min)", new Stepper(shareMinutes)));
        more.Note($"MKV or MP4, up to {SharePolicy.MaxShareMegabytes / 1024} GB.");
        more.Section("STARTUP"); Toggle(more, "Start with Windows", autostart2, settings.StartWithWindows); Toggle(more, "Start in tray", minimized, settings.StartMinimized); Toggle(more, "Start buffer on launch", bufferOnLaunch, settings.StartBufferOnLaunch);
        moreFeedback.ForeColor = Look.Muted; more.Add(moreFeedback, 10);

        duration.ValueChanged += (_, _) => duration2.Value = duration.Value; duration2.ValueChanged += (_, _) => duration.Value = duration2.Value;
        hotkey.TextChanged += (_, _) => hotkey2.Text = hotkey.Text; hotkey2.TextChanged += (_, _) => hotkey.Text = hotkey2.Text;
        directory.TextChanged += (_, _) => directory2.Text = directory.Text; directory2.TextChanged += (_, _) => directory.Text = directory2.Text;
        autostart.CheckedChanged += (_, _) => autostart2.Checked = autostart.Checked; autostart2.CheckedChanged += (_, _) => autostart.Checked = autostart2.Checked;
        // Every change is applied after a short pause; a few in a row are applied together.
        foreach (var box in new[] { separateTracks, mic, desktop, minimized, autostart, bufferOnLaunch, clipBeep, autoShare, sortByApp, directLink, copyLink }) box.CheckedChanged += (_, _) => Changed();
        foreach (var combo in new[] { resolution, fps }) combo.SelectedIndexChanged += (_, _) => Changed();
        duration.ValueChanged += (_, _) => Changed(); shareMinutes.ValueChanged += (_, _) => Changed(); quality.ValueChanged += (_, _) => Changed();
        commitTimer.Tick += (_, _) => Commit(); shareTicker.Tick += (_, _) => UpdateShareControls();
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Commit(); Hide(); } };
        // The share countdown only needs updating while the popup is open.
        VisibleChanged += (_, _) => { if (Visible) { RefreshGames(); UpdateStatus(); if (sharer != null) shareTicker.Start(); } else { Commit(); shareTicker.Stop(); } };
        DpiChanged += (_, _) => BeginInvoke(() => { if (popup && Visible) Place(); });
        home.HandleCreated += (_, _) => DarkScrollbars(home.Handle); more.HandleCreated += (_, _) => DarkScrollbars(more.Handle);
        EnableDrop(this);
        engine.StatusChanged += EngineStatus; UpdateBudget(); UpdateStatus(); loading = false;
    }

    int Px(int logical) => LogicalToDeviceUnits(logical);
    static void Style(Button b, string text, bool primary = false)
    {
        b.Text = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.UseVisualStyleBackColor = false; b.Cursor = Cursors.Hand;
        b.BackColor = primary ? Look.Text : Look.Card; b.ForeColor = primary ? Look.Page : Look.Text;
    }
    void IconButton(Button b, string glyph, string name) { Style(b, glyph); b.BackColor = Look.Page; b.ForeColor = Look.Muted; b.Font = Look.Icons(11); b.AccessibleName = name; b.TabStop = true; }
    // Clip length, save hotkey and folder (home has them, the settings page too).
    void ClipBasics(Page page, NumericUpDown length, TextBox key, TextBox folderBox, AppSettings settings)
    {
        length.Minimum = AppSettings.MinClipSeconds; length.Maximum = AppSettings.MaxClipSeconds; length.Value = settings.ClipSeconds; page.Add(new Card("Clip length (s)", new Stepper(length)));
        key.ReadOnly = true; key.Text = settings.Hotkey; key.TextAlign = HorizontalAlignment.Center; key.AccessibleName = "Clip hotkey"; key.Cursor = Cursors.Hand;
        key.KeyDown += (_, e) => { e.SuppressKeyPress = true; if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.Escape or Keys.Tab) return; uint mods = Hotkey.NoRepeat | (e.Control ? 2u : 0) | (e.Shift ? 4u : 0) | (e.Alt ? 1u : 0); key.Text = new Hotkey(mods, e.KeyCode).ToString(); Changed(); };
        page.Add(new Card("Save hotkey", new Field(key), 130));
        // The folder is chosen with the folder picker; typing a path would apply half-typed folders.
        folderBox.ReadOnly = true; folderBox.Text = settings.ClipsDirectory; folderBox.Cursor = Cursors.Hand; folderBox.Click += (_, _) => Browse(); folderBox.KeyDown += (_, e) => { if (e.KeyCode is Keys.Enter or Keys.Space) Browse(); };
        var field = new Field(folderBox); field.Click += (_, _) => Browse(); page.Add(new Card("Folder", field, stretch: true));
    }
    static void Toggle(Page page, string text, CheckBox box, bool value) { box.Checked = value; box.AccessibleName = text; page.Add(new Card(text, box)); }
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
        // A tray click allows taking the foreground; without it the popup would not get focus, and so never close by itself.
        if (!Visible) Show(); SetForegroundWindow(Handle); Activate(); BeginInvoke(Place); // a monitor with other scaling resizes the window once shown
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
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ClassStyle |= 0x00020000; return p; } } // CS_DROPSHADOW
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11: rounded corners and a thin border on the borderless window. Older versions ignore it.
        try { int round = 2, border = ColorRef(Look.Line); DwmSetWindowAttribute(Handle, CornerPreference, ref round, sizeof(int)); DwmSetWindowAttribute(Handle, BorderColor, ref border, sizeof(int)); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
    }

    void Changed() { if (loading) return; UpdateBudget(); commitTimer.Stop(); commitTimer.Start(); }
    // Applies what the controls show. Invalid values (or a hotkey that is taken) are reported and not applied.
    void Commit()
    {
        commitTimer.Stop(); if (loading) return;
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
    // Own dialogs take the focus; the popup must stay open behind them.
    T Dialog<T>(Func<T> show) { dialogs++; try { return show(); } finally { dialogs--; } }

    void EngineStatus(object? sender, EventArgs args) { if (!IsDisposed) UpdateStatus(); }
    public AppSettings ReadSettings()
    {
        var size = resolution.SelectedIndex switch { 0 => (1280, 720), 1 => (1920, 1080), 2 => (2560, 1440), _ => (3840, 2160) };
        var s = original with { Width = size.Item1, Height = size.Item2, Fps = (int)(fps.SelectedItem ?? 60), ClipSeconds = (int)duration.Value, Hotkey = Hotkey.Parse(hotkey.Text).ToString(), Quality = (QualityPreset)quality.Value, ClipsDirectory = directory.Text.Trim(), Microphone = mic.Checked, DesktopAudio = desktop.Checked, SeparateAudioTracks = separateTracks.Checked, ClipBeep = clipBeep.Checked, StartMinimized = minimized.Checked, StartBufferOnLaunch = bufferOnLaunch.Checked, StartWithWindows = autostart.Checked, ShareMinutes = (int)shareMinutes.Value, AutoShare = autoShare.Checked, CopyShareLink = copyLink.Checked, SortByApp = sortByApp.Checked, DirectLink = directLink.Checked };
        s.Validate(); return s;
    }
    void UpdateBudget()
    {
        if (fps.SelectedItem == null) return; var size = resolution.SelectedIndex switch { 0 => (1280, 720), 1 => (1920, 1080), 2 => (2560, 1440), _ => (3840, 2160) };
        var s = original with { Width = size.Item1, Height = size.Item2, Fps = (int)fps.SelectedItem, ClipSeconds = (int)duration.Value, Quality = (QualityPreset)quality.Value, DesktopAudio = desktop.Checked, Microphone = mic.Checked, SeparateAudioTracks = separateTracks.Checked };
        budget.Text = $"≈ {FormatSize(CapturePolicy.EstimatedClipMegabytes(s))} per clip";
        budgetDetail.Text = $"{CapturePolicy.BitrateKbps(s) / 1000d:0.#} Mbit/s video + {CapturePolicy.AudioTracks(s)} × {CapturePolicy.AudioKbpsPerTrack} kbit/s audio · ≈ {FormatSize(CapturePolicy.EstimatedClipMegabytes(s))} per clip · RAM buffer up to {CapturePolicy.BufferMegabytes(s)} MB";
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
        Look.Show(shareRow, true);
        if (sharer.IsStarting) { shareButton.Text = "Cancel sharing"; shareStatus.Text = "Creating link…"; }
        else if (sharer.Current is { } c)
        {
            int left = Math.Max(1, (int)Math.Ceiling((c.ExpiresAt - DateTimeOffset.Now).TotalMinutes));
            shareButton.Text = "Stop sharing"; shareStatus.Text = $"Shared · {left} min left (until {c.ExpiresAt:HH:mm})\n{new Uri(c.Url).Host}";
        }
        else { shareButton.Text = $"Quick share ({original.ShareMinutes} min)"; shareStatus.Text = ""; }
        Look.Show(copyLinkButton, sharer.Current != null); Look.Show(shareStatus, shareStatus.Text.Length > 0);
        if (popup && Visible) Place(); // the popup grows or shrinks with the share line, keeping its place by the tray
    }
    internal static Icon? AppIcon()
    {
        // Embedded in the app; absent when the form is compiled into tests.
        using var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream("MonoClip.ico");
        return stream == null ? null : new Icon(stream);
    }
    public void Feedback(string text) { feedback.Text = text; moreFeedback.Text = text; }
    // The tray saved a new export folder: keep it when these settings are applied later.
    internal void KeepExportDirectory(string folder) => original = original with { ExportDirectory = folder };
    void Run(Action action) { try { action(); UpdateStatus(); } catch (Exception e) { Feedback(e.Message); } }
    void RefreshGames() { Run(() => { var chosen = games.Text; games.Items.Clear(); games.Items.Add("All clips"); foreach (var g in ClipLibrary.GetGames(original.ClipsDirectory)) games.Items.Add(g); games.SelectedIndex = Math.Max(0, games.Items.IndexOf(chosen)); }); }
    protected override void Dispose(bool disposing) { if (disposing) { engine.StatusChanged -= EngineStatus; shareTicker.Dispose(); commitTimer.Dispose(); } base.Dispose(disposing); }

    // Window chrome. Attribute 20 (19 before Windows 10 20H1) switches a title bar to dark mode; 33 asks Windows 11
    // for rounded corners; 34/35/36 set exact border/caption/text colours. Unsupported ones just return an error.
    const int DarkModeOld = 19, DarkMode = 20, CornerPreference = 33, BorderColor = 34, CaptionColor = 35, TextColor = 36;
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);
    static int ColorRef(Color c) => c.R | c.G << 8 | c.B << 16;
    internal static void DarkTitleBar(IntPtr window)
    {
        try
        {
            int on = 1; if (DwmSetWindowAttribute(window, DarkMode, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(window, DarkModeOld, ref on, sizeof(int));
            int caption = ColorRef(Color.FromArgb(12, 12, 12)), text = ColorRef(Look.Text), border = ColorRef(Look.Line);
            DwmSetWindowAttribute(window, CaptionColor, ref caption, sizeof(int)); DwmSetWindowAttribute(window, TextColor, ref text, sizeof(int)); DwmSetWindowAttribute(window, BorderColor, ref border, sizeof(int));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { /* Cosmetic only. */ }
    }
    static void DarkScrollbars(IntPtr window)
    {
        try { SetWindowTheme(window, "DarkMode_Explorer", null); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { /* Cosmetic only. */ }
    }
}
