using MonoClip.Core;
using System.Runtime.InteropServices;
namespace MonoClip.Windows.UI;

public sealed class SettingsForm : Form
{
    static readonly Color Black = Color.FromArgb(12, 12, 12), White = Color.FromArgb(240, 240, 240), Muted = Color.FromArgb(160, 160, 160), Line = Color.FromArgb(48, 48, 48);
    // Logical (96 DPI) sizes. Below NarrowContent the form switches to a single column: label above control.
    const int LogicalWidth = 660, LogicalHeight = 900, LogicalMinWidth = 380, LogicalMinHeight = 320, NarrowContent = 430, WidePadding = 28, NarrowPadding = 16;
    AppSettings original; readonly IClipEngine engine; readonly Action<AppSettings> saveSettings; readonly Action toggle, saveClip;
    readonly ComboBox resolution = new(), fps = new(), games = new(); readonly NumericUpDown duration = new(); readonly TextBox hotkey = new(), directory = new();
    readonly CheckBox mic = new(), desktop = new(), minimized = new(), autostart = new(), bufferOnLaunch = new(), clipBeep = new(), advanced = new();
    readonly MonoSlider quality = new("Performance", "Ausgewogen", "Qualität"); readonly List<Control> advancedOnly = [];
    readonly Label status = new(), target = new(), budget = new(), feedback = new(); readonly Button toggleButton = new MonoButton(), clipButton = new MonoButton();
    readonly TableLayoutPanel table = new() { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Padding = new Padding(WidePadding, 18, WidePadding, 18) };
    readonly Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    readonly List<(Label Label, Control Control, int Row)> rows = new(); readonly List<Control> fullWidth = new();
    bool? narrow; bool relayouting;

    public SettingsForm(AppSettings settings, IClipEngine engine, Action<AppSettings> saveSettings, Action toggle, Action saveClip)
    {
        original = settings with { }; this.engine = engine; this.saveSettings = saveSettings; this.toggle = toggle; this.saveClip = saveClip;
        Text = "MonoClip"; BackColor = Black; ForeColor = White; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; ShowIcon = false; StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 1000);
        MinimumSize = new Size(Math.Min(Px(LogicalMinWidth), area.Width), Math.Min(Px(LogicalMinHeight), area.Height));
        Size = new Size(Math.Min(Px(LogicalWidth), area.Width), Math.Min(Px(LogicalHeight), area.Height - Px(70)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        scroll.BackColor = Black; scroll.Controls.Add(table); Controls.Add(scroll);
        // AutoSize footer: its height follows the scaled button height instead of clipping it at high DPI.
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Padding = new Padding(16, 12, 16, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Black }; Controls.Add(footer);
        var apply = Button("Übernehmen", () => Run(() => { var s = ReadSettings(); saveSettings(s); original = s with { }; Feedback("Einstellungen gespeichert."); UpdateStatus(); })); apply.BackColor = White; apply.ForeColor = Black; footer.Controls.Add(apply);
        var hide = Button("In den Tray", Hide); footer.Controls.Add(hide);
        Full(new Label { Text = "MONOCLIP", Font = new Font("Segoe UI Semibold", 22), AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        Full(new Label { Text = "Der letzte Moment. Ohne offene Aufnahmeoberfläche.", ForeColor = Muted, AutoSize = true, Margin = new Padding(0, 0, 0, 14) });
        status.AutoSize = true; status.Font = new Font("Segoe UI Semibold", 11); Full(status); target.AutoSize = true; target.ForeColor = Muted; Full(target);
        var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = new Padding(0, 10, 0, 4) };
        StyleButton(toggleButton); toggleButton.Click += (_, _) => Run(toggle); StyleButton(clipButton); clipButton.Text = "Clip speichern"; clipButton.Click += (_, _) => Run(saveClip); actions.Controls.Add(toggleButton); actions.Controls.Add(clipButton); Full(actions);
        Check(advanced, "Erweiterte Einstellungen", settings.AdvancedMode); advanced.Margin = new Padding(0, 10, 0, 0); advanced.CheckedChanged += (_, _) => { ApplyMode(); Run(() => { var next = original with { AdvancedMode = advanced.Checked }; saveSettings(next); original = next; }); };
        Section("AUFNAHME");
        StyleCombo(resolution); resolution.Items.AddRange(new object[] { "1280 × 720", "1920 × 1080", "2560 × 1440", "3840 × 2160" }); resolution.SelectedIndex = settings.Width switch { 1280 => 0, 1920 => 1, 2560 => 2, _ => 3 }; Row("Auflösung", resolution);
        StyleCombo(fps); fps.Items.AddRange(new object[] { 30, 60, 120 }); fps.SelectedItem = settings.Fps; Row("Bilder pro Sekunde", fps);
        duration.Minimum = 5; duration.Maximum = 300; duration.Value = settings.ClipSeconds; duration.BackColor = Black; duration.ForeColor = White; duration.BorderStyle = BorderStyle.FixedSingle; Row("Cliplänge · Sekunden", duration);
        hotkey.ReadOnly = true; StyleText(hotkey); hotkey.Text = settings.Hotkey; hotkey.AccessibleName = "Clip-Tastenkombination";
        hotkey.KeyDown += (_, e) => { e.SuppressKeyPress = true; if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return; uint mods = Hotkey.NoRepeat | (e.Control ? 2u : 0) | (e.Shift ? 4u : 0) | (e.Alt ? 1u : 0); hotkey.Text = new Hotkey(mods, e.KeyCode).ToString(); }; Row("Hotkey · drücken zum Ändern", hotkey);
        quality.BackColor = Black; quality.ForeColor = White; quality.Value = (int)settings.Quality; quality.AccessibleName = "Qualität und Bitrate"; quality.AccessibleDescription = quality.ValueText; quality.MinimumSize = new Size(0, quality.GetPreferredSize(Size.Empty).Height); quality.ValueChanged += (_, _) => UpdateBudget(); Row("Qualität · Bitrate", quality, true);
        budget.AutoSize = true; budget.ForeColor = Muted; Full(budget); resolution.SelectedIndexChanged += (_, _) => UpdateBudget(); fps.SelectedIndexChanged += (_, _) => UpdateBudget(); duration.ValueChanged += (_, _) => UpdateBudget();
        Section("AUDIO", true);
        Check(desktop, "Desktop-Audio aufnehmen", settings.DesktopAudio, true); Check(mic, "Mikrofon aufnehmen", settings.Microphone, true); desktop.CheckedChanged += (_, _) => UpdateBudget(); mic.CheckedChanged += (_, _) => UpdateBudget();
        Full(new Label { Text = "Windows-Standardgeräte. Separate Desktop- und Mikrofonspur; zusätzlich eine gemischte Spur für normale Wiedergabe.", ForeColor = Muted, AutoSize = true, Margin = new Padding(0, 4, 0, 8) }, true);
        Check(clipBeep, "Kurzer Beep bei gespeichertem Clip", settings.ClipBeep, true);
        Section("HINTERGRUND"); Check(autostart, "Mit Windows starten · immer minimiert", settings.StartWithWindows); Check(minimized, "Nur im Tray starten", settings.StartMinimized, true); Check(bufferOnLaunch, "Replay-Puffer beim App-Start aktivieren", settings.StartBufferOnLaunch, true);
        Section("CLIPS"); StyleText(directory); directory.Text = settings.ClipsDirectory;
        var pathPanel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill }; pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); directory.Dock = DockStyle.Fill; directory.Margin = new Padding(0, 3, 6, 3); pathPanel.Controls.Add(directory, 0, 0);
        var browse = Button("…", () => { using var dialog = new FolderBrowserDialog { InitialDirectory = directory.Text, Description = "Ordner für lokale Clips" }; if (dialog.ShowDialog(this) == DialogResult.OK) directory.Text = dialog.SelectedPath; }); browse.MinimumSize = new Size(Px(42), Px(30)); browse.Margin = Padding.Empty; pathPanel.Controls.Add(browse, 1, 0); Row("Speicherordner", pathPanel);
        StyleCombo(games); games.Items.Add("Alle Clips"); games.SelectedIndex = 0; Row("Nach Spiel", games, true);
        var libraryActions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true }; libraryActions.Controls.Add(Button("Ordner öffnen", () => Run(() => WindowsIntegration.OpenClipFolder(games.SelectedIndex > 0 ? Path.Combine(original.ClipsDirectory, games.Text) : original.ClipsDirectory)))); var refresh = Button("Liste aktualisieren", RefreshGames); libraryActions.Controls.Add(refresh); advancedOnly.Add(refresh); Full(libraryActions);
        feedback.AutoSize = true; feedback.ForeColor = Muted; feedback.Margin = new Padding(0, 8, 0, 0); Full(feedback);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } }; Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); }; VisibleChanged += (_, _) => { if (Visible) { WindowState = FormWindowState.Normal; RefreshGames(); UpdateStatus(); } };
        scroll.ClientSizeChanged += (_, _) => Relayout(); DpiChanged += (_, _) => { narrow = null; Relayout(); };
        scroll.HandleCreated += (_, _) => DarkScrollbars(scroll.Handle);
        engine.StatusChanged += EngineStatus; ApplyMode(); UpdateStatus(); Relayout();
    }

    int Px(int logical) => LogicalToDeviceUnits(logical);

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkTitleBar(Handle); }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Never open larger than the screen the window lands on (small displays, high scaling).
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
        if (Width > area.Width || Height > area.Height)
        {
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }
        Relayout();
    }

    /// <summary>Re-flows the form for the current width: wraps every text, and stacks label/control rows when narrow.</summary>
    void Relayout()
    {
        if (relayouting || IsDisposed) return;
        relayouting = true;
        try
        {
            // Wrapping can show/hide the vertical scrollbar, which changes the width again; settle in a few passes.
            for (int pass = 0; pass < 3; pass++)
            {
                int used = scroll.ClientSize.Width;
                LayoutFor(used);
                if (scroll.ClientSize.Width == used) break;
            }
        }
        finally { relayouting = false; }
    }

    void LayoutFor(int clientWidth)
    {
        int available = Math.Max(Px(200), clientWidth);
        bool isNarrow = available - 2 * Px(WidePadding) < Px(NarrowContent);
        int pad = Px(isNarrow ? NarrowPadding : WidePadding);
        int content = Math.Max(Px(120), available - 2 * pad);
        table.SuspendLayout();
        try
        {
            if (narrow != isNarrow)
            {
                narrow = isNarrow;
                table.Padding = new Padding(pad, Px(18), pad, Px(18));
                foreach (var (label, control, row) in rows)
                {
                    if (isNarrow)
                    {
                        table.SetCellPosition(control, new TableLayoutPanelCellPosition(0, row + 1)); table.SetColumnSpan(control, 2);
                        table.SetCellPosition(label, new TableLayoutPanelCellPosition(0, row)); table.SetColumnSpan(label, 2); label.Margin = new Padding(0, Px(8), 0, 0);
                    }
                    else
                    {
                        table.SetColumnSpan(label, 1); table.SetColumnSpan(control, 1); label.Margin = new Padding(0, Px(9), Px(12), Px(9));
                        table.SetCellPosition(label, new TableLayoutPanelCellPosition(0, row)); table.SetCellPosition(control, new TableLayoutPanelCellPosition(1, row));
                    }
                }
            }
            int labelWidth = isNarrow ? content : Math.Max(Px(60), (int)(content * 0.43) - Px(12));
            foreach (var (label, _, _) in rows) label.MaximumSize = new Size(labelWidth, 0);
            foreach (var control in fullWidth) control.MaximumSize = new Size(Math.Max(Px(60), content - control.Margin.Horizontal), 0);
        }
        finally { table.ResumeLayout(true); }
    }

    void EngineStatus(object? sender, EventArgs args) { if (!IsDisposed) UpdateStatus(); }
    void Row(string text, Control control, bool advancedOnly = false)
    {
        var label = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 9, 12, 9) }; control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 4, 0, 4);
        // Two rows per entry: side by side uses the first one, the narrow layout stacks the control into the second.
        int row = table.RowCount; table.RowCount += 2; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(label, 0, row); table.Controls.Add(control, 1, row); rows.Add((label, control, row));
        if (advancedOnly) this.advancedOnly.AddRange([label, control]);
    }
    void Full(Control control, bool advancedOnly = false)
    {
        int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); control.Anchor = AnchorStyles.Left; table.Controls.Add(control, 0, row); table.SetColumnSpan(control, 2);
        if (control is Label or FlowLayoutPanel) fullWidth.Add(control);
        if (advancedOnly) this.advancedOnly.Add(control);
    }
    void Section(string text, bool advancedOnly = false) { Full(new Label { Text = text, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI Semibold", 9), Margin = new Padding(0, 19, 0, 7) }, advancedOnly); }
    void Check(CheckBox box, string text, bool value, bool advancedOnly = false) { box.FlatStyle = FlatStyle.Flat; box.FlatAppearance.BorderColor = Muted; box.FlatAppearance.CheckedBackColor = Black; box.Text = text; box.Checked = value; box.AutoSize = true; box.Margin = new Padding(0, 4, 0, 4); Full(box, advancedOnly); }
    // Simple mode keeps resolution, FPS, length, hotkey, autostart and folder; the rest is advanced.
    void ApplyMode() { table.SuspendLayout(); foreach (var control in advancedOnly) control.Visible = advanced.Checked; table.ResumeLayout(); UpdateBudget(); }
    static void StyleCombo(ComboBox c)
    {
        c.DropDownStyle = ComboBoxStyle.DropDownList; c.BackColor = Color.FromArgb(24, 24, 24); c.ForeColor = White; c.FlatStyle = FlatStyle.Flat;
        c.DrawMode = DrawMode.OwnerDrawFixed; c.ItemHeight = Math.Max(24, c.Font.Height + 6); c.DrawItem += (_, e) => { if (e.Index < 0) return; bool selected = (e.State & DrawItemState.Selected) != 0; using var background = new SolidBrush(selected ? White : Color.FromArgb(24, 24, 24)); e.Graphics.FillRectangle(background, e.Bounds); TextRenderer.DrawText(e.Graphics, c.Items[e.Index]?.ToString() ?? "", c.Font, e.Bounds, selected ? Black : White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); e.DrawFocusRectangle(); };
    }
    static void StyleText(TextBox t) { t.BackColor = Color.FromArgb(24, 24, 24); t.ForeColor = White; t.BorderStyle = BorderStyle.FixedSingle; }
    void StyleButton(Button b) { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Line; b.BackColor = Black; b.ForeColor = White; b.AutoSize = true; b.Padding = new Padding(9, 4, 9, 4); b.MinimumSize = new Size(Px(100), Px(34)); b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false; }
    Button Button(string text, Action action) { var b = new MonoButton { Text = text }; StyleButton(b); b.Click += (_, _) => action(); return b; }
    public AppSettings ReadSettings() { var size = resolution.SelectedIndex switch { 0 => (1280, 720), 1 => (1920, 1080), 2 => (2560, 1440), _ => (3840, 2160) }; var s = original with { Width = size.Item1, Height = size.Item2, Fps = (int)(fps.SelectedItem ?? 60), ClipSeconds = (int)duration.Value, Hotkey = Hotkey.Parse(hotkey.Text).ToString(), Quality = (QualityPreset)quality.Value, ClipsDirectory = directory.Text.Trim(), Microphone = mic.Checked, DesktopAudio = desktop.Checked, ClipBeep = clipBeep.Checked, StartMinimized = minimized.Checked, StartBufferOnLaunch = bufferOnLaunch.Checked, StartWithWindows = autostart.Checked, AdvancedMode = advanced.Checked }; s.Validate(); return s; }
    void UpdateBudget()
    {
        if (fps.SelectedItem == null) return; var size = resolution.SelectedIndex switch { 0 => (1280, 720), 1 => (1920, 1080), 2 => (2560, 1440), _ => (3840, 2160) };
        var s = original with { Width = size.Item1, Height = size.Item2, Fps = (int)fps.SelectedItem, ClipSeconds = (int)duration.Value, Quality = (QualityPreset)quality.Value, DesktopAudio = desktop.Checked, Microphone = mic.Checked };
        var estimate = $"Geschätzte Dateigröße ≈ {FormatSize(CapturePolicy.EstimatedClipMegabytes(s))} pro Clip";
        budget.Text = advanced.Checked
            ? $"{quality.ValueText}: {CapturePolicy.BitrateKbps(s) / 1000d:0.#} Mbit/s Video + {CapturePolicy.AudioTracks(s)} × {CapturePolicy.AudioKbpsPerTrack} kbit/s Audio\n{estimate} · RAM-Pufferlimit {CapturePolicy.BufferMegabytes(s)} MB\nGPU-Encoder · keine laufenden Video-Schreibzugriffe · SDR"
            : estimate;
    }
    internal static string FormatSize(double megabytes) => megabytes >= 1024 ? $"{megabytes / 1024:0.0} GB" : megabytes >= 10 ? $"{megabytes:0} MB" : $"{megabytes:0.0} MB";
    public void UpdateStatus() { status.Text = (engine.IsRunning ? "●  " : "○  ") + engine.Status; target.Text = engine.CaptureTarget + "  /  " + engine.EncoderName; toggleButton.Text = engine.IsRunning ? "Puffer stoppen" : "Puffer starten"; clipButton.Enabled = engine.IsRunning; }
    public void Feedback(string text) { feedback.Text = text; }
    void Run(Action action) { try { action(); UpdateStatus(); } catch (Exception e) { Feedback(e.Message); } }
    void RefreshGames() { Run(() => { games.Items.Clear(); games.Items.Add("Alle Clips"); foreach (var g in ClipLibrary.GetGames(original.ClipsDirectory)) games.Items.Add(g); games.SelectedIndex = 0; }); }
    protected override void Dispose(bool disposing) { if (disposing) engine.StatusChanged -= EngineStatus; base.Dispose(disposing); }

    // Window chrome: black caption bar instead of the light default. Attribute 20 (19 before Windows 10 20H1)
    // switches the title bar to dark mode; 34/35/36 set exact border/caption/text colors on Windows 11.
    // Unsupported attributes just return an error HRESULT, which is ignored.
    const int DarkModeOld = 19, DarkMode = 20, BorderColor = 34, CaptionColor = 35, TextColor = 36;
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);
    static int ColorRef(Color c) => c.R | c.G << 8 | c.B << 16;
    static void DarkTitleBar(IntPtr window)
    {
        try
        {
            int on = 1; if (DwmSetWindowAttribute(window, DarkMode, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(window, DarkModeOld, ref on, sizeof(int));
            int caption = ColorRef(Black), text = ColorRef(White), border = ColorRef(Line);
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
