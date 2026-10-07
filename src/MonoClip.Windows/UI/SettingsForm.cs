using MonoClip.Core;
using System.Diagnostics;
namespace MonoClip.Windows.UI;

public sealed class SettingsForm : Form
{
    static readonly Color Black = Color.FromArgb(12, 12, 12), White = Color.FromArgb(240, 240, 240), Muted = Color.FromArgb(160, 160, 160), Line = Color.FromArgb(48, 48, 48);
    AppSettings original; readonly IClipEngine engine; readonly Action<AppSettings> saveSettings; readonly Action toggle, saveClip;
    readonly ComboBox resolution = new(), fps = new(), games = new(); readonly NumericUpDown duration = new(); readonly TextBox hotkey = new(), directory = new();
    readonly CheckBox mic = new(), desktop = new(), minimized = new(), autostart = new(), bufferOnLaunch = new(), clipBeep = new();
    readonly Label status = new(), target = new(), budget = new(), feedback = new(); readonly Button toggleButton = new MonoButton(), clipButton = new MonoButton();
    readonly TableLayoutPanel table = new() { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(28, 18, 28, 18) };
    public SettingsForm(AppSettings settings, IClipEngine engine, Action<AppSettings> saveSettings, Action toggle, Action saveClip)
    {
        original = settings with { }; this.engine = engine; this.saveSettings = saveSettings; this.toggle = toggle; this.saveClip = saveClip;
        Text = "MonoClip"; BackColor = Black; ForeColor = White; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; ShowIcon = false; StartPosition = FormStartPosition.CenterScreen; MinimumSize = new Size(540, 440);
        Size = new Size(660, Math.Min(900, (Screen.PrimaryScreen?.WorkingArea.Height ?? 1000) - 70));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(table); Controls.Add(scroll);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 66, Padding = new Padding(24, 12, 16, 10), FlowDirection = FlowDirection.RightToLeft, BackColor = Black }; Controls.Add(footer);
        var apply = Button("Übernehmen", () => Run(() => { var s = ReadSettings(); saveSettings(s); original = s with { }; Feedback("Einstellungen gespeichert."); UpdateStatus(); })); apply.BackColor = White; apply.ForeColor = Black; footer.Controls.Add(apply);
        var hide = Button("In den Tray", Hide); footer.Controls.Add(hide);
        Full(new Label { Text = "MONOCLIP", Font = new Font("Segoe UI Semibold", 22), AutoSize = true, Margin = new Padding(0, 0, 0, 12) });
        Full(new Label { Text = "Der letzte Moment. Ohne offene Aufnahmeoberfläche.", ForeColor = Muted, AutoSize = true, Margin = new Padding(0, 0, 0, 14) });
        status.AutoSize = true; status.Font = new Font("Segoe UI Semibold", 11); Full(status); target.AutoSize = true; target.ForeColor = Muted; Full(target);
        var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 4) };
        StyleButton(toggleButton); toggleButton.Click += (_, _) => Run(toggle); StyleButton(clipButton); clipButton.Text = "Clip speichern"; clipButton.Click += (_, _) => Run(saveClip); actions.Controls.Add(toggleButton); actions.Controls.Add(clipButton); Full(actions);
        Section("AUFNAHME");
        StyleCombo(resolution); resolution.Items.AddRange(new object[] { "1280 × 720", "1920 × 1080", "2560 × 1440", "3840 × 2160" }); resolution.SelectedIndex = settings.Width switch { 1280 => 0, 1920 => 1, 2560 => 2, _ => 3 }; Row("Auflösung", resolution);
        StyleCombo(fps); fps.Items.AddRange(new object[] { 30, 60, 120 }); fps.SelectedItem = settings.Fps; Row("Bilder pro Sekunde", fps);
        duration.Minimum = 5; duration.Maximum = 300; duration.Value = settings.ClipSeconds; duration.BackColor = Black; duration.ForeColor = White; duration.BorderStyle = BorderStyle.FixedSingle; Row("Cliplänge · Sekunden", duration);
        hotkey.ReadOnly = true; StyleText(hotkey); hotkey.Text = settings.Hotkey; hotkey.AccessibleName = "Clip-Tastenkombination";
        hotkey.KeyDown += (_, e) => { e.SuppressKeyPress = true; if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return; uint mods = Hotkey.NoRepeat | (e.Control ? 2u : 0) | (e.Shift ? 4u : 0) | (e.Alt ? 1u : 0); hotkey.Text = new Hotkey(mods, e.KeyCode).ToString(); }; Row("Hotkey · drücken zum Ändern", hotkey);
        budget.AutoSize = true; budget.ForeColor = Muted; budget.MaximumSize = new Size(570, 0); Full(budget); resolution.SelectedIndexChanged += (_, _) => UpdateBudget(); fps.SelectedIndexChanged += (_, _) => UpdateBudget(); duration.ValueChanged += (_, _) => UpdateBudget();
        Section("AUDIO");
        Check(desktop, "Desktop-Audio aufnehmen", settings.DesktopAudio); Check(mic, "Mikrofon aufnehmen", settings.Microphone);
        Full(new Label { Text = "Windows-Standardgeräte. Separate Desktop- und Mikrofonspur;\nzusätzlich eine gemischte Spur für normale Wiedergabe.", ForeColor = Muted, AutoSize = true, Margin = new Padding(0, 4, 0, 8) });
        Check(clipBeep,"Kurzer Beep bei gespeichertem Clip",settings.ClipBeep);
        Section("HINTERGRUND"); Check(minimized, "Nur im Tray starten", settings.StartMinimized); Check(bufferOnLaunch, "Replay-Puffer beim App-Start aktivieren", settings.StartBufferOnLaunch); Check(autostart, "Mit Windows starten · immer minimiert", settings.StartWithWindows);
        Section("CLIPS"); StyleText(directory); directory.Text = settings.ClipsDirectory;
        var pathPanel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill }; pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); directory.Dock = DockStyle.Fill; pathPanel.Controls.Add(directory, 0, 0);
        var browse = Button("…", () => { using var dialog = new FolderBrowserDialog { InitialDirectory = directory.Text, Description = "Ordner für lokale Clips" }; if (dialog.ShowDialog(this) == DialogResult.OK) directory.Text = dialog.SelectedPath; }); browse.Width = 42; pathPanel.Controls.Add(browse, 1, 0); Row("Speicherordner", pathPanel);
        StyleCombo(games); games.Items.Add("Alle Clips"); games.SelectedIndex = 0; Row("Nach Spiel", games);
        var libraryActions = new FlowLayoutPanel { AutoSize = true }; libraryActions.Controls.Add(Button("Ordner öffnen", () => Run(() => { var path = games.SelectedIndex > 0 ? Path.Combine(original.ClipsDirectory, games.Text) : original.ClipsDirectory; Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }))); libraryActions.Controls.Add(Button("Liste aktualisieren", RefreshGames)); Full(libraryActions);
        feedback.AutoSize = true; feedback.ForeColor = Muted; feedback.MaximumSize = new Size(560, 0); feedback.Margin = new Padding(0, 8, 0, 0); Full(feedback);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } }; Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); }; VisibleChanged += (_, _) => { if (Visible) { WindowState = FormWindowState.Normal; RefreshGames(); UpdateStatus(); } };
        engine.StatusChanged += EngineStatus; UpdateBudget(); UpdateStatus();
    }
    void EngineStatus(object? sender, EventArgs args) { if (!IsDisposed) UpdateStatus(); }
    void Row(string text, Control control) { var label = new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 9, 12, 9) }; control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 4, 0, 4); int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(label, 0, row); table.Controls.Add(control, 1, row); }
    void Full(Control control) { int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); control.Anchor = AnchorStyles.Left | AnchorStyles.Right; table.Controls.Add(control, 0, row); table.SetColumnSpan(control, 2); }
    void Section(string text) { Full(new Label { Text = text, AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI Semibold", 9), Margin = new Padding(0, 19, 0, 7) }); }
    void Check(CheckBox box, string text, bool value) { box.FlatStyle = FlatStyle.Flat; box.FlatAppearance.BorderColor = Muted; box.FlatAppearance.CheckedBackColor = Black; box.Text = text; box.Checked = value; box.AutoSize = true; box.Margin = new Padding(0, 4, 0, 4); Full(box); }
    static void StyleCombo(ComboBox c)
    {
        c.DropDownStyle = ComboBoxStyle.DropDownList; c.BackColor = Color.FromArgb(24, 24, 24); c.ForeColor = White; c.FlatStyle = FlatStyle.Flat;
        c.DrawMode = DrawMode.OwnerDrawFixed; c.ItemHeight = 24; c.DrawItem += (_, e) => { if (e.Index < 0) return; bool selected = (e.State & DrawItemState.Selected) != 0; using var background = new SolidBrush(selected ? White : Color.FromArgb(24, 24, 24)); e.Graphics.FillRectangle(background, e.Bounds); TextRenderer.DrawText(e.Graphics, c.Items[e.Index]?.ToString() ?? "", c.Font, e.Bounds, selected ? Black : White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter); e.DrawFocusRectangle(); };
    }
    static void StyleText(TextBox t) { t.BackColor = Color.FromArgb(24, 24, 24); t.ForeColor = White; t.BorderStyle = BorderStyle.FixedSingle; }
    static void StyleButton(Button b) { b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Line; b.BackColor = Black; b.ForeColor = White; b.AutoSize = true; b.Padding = new Padding(9, 4, 9, 4); b.MinimumSize = new Size(100, 34); b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false; }
    static Button Button(string text, Action action) { var b = new MonoButton { Text = text }; StyleButton(b); b.Click += (_, _) => action(); return b; }
    public AppSettings ReadSettings() { var size = resolution.SelectedIndex switch { 0 => (1280, 720), 1 => (1920, 1080), 2 => (2560, 1440), _ => (3840, 2160) }; var s = original with { Width = size.Item1, Height = size.Item2, Fps = (int)(fps.SelectedItem ?? 60), ClipSeconds = (int)duration.Value, Hotkey = Hotkey.Parse(hotkey.Text).ToString(), ClipsDirectory = directory.Text.Trim(), Microphone = mic.Checked, DesktopAudio = desktop.Checked, ClipBeep = clipBeep.Checked, StartMinimized = minimized.Checked, StartBufferOnLaunch = bufferOnLaunch.Checked, StartWithWindows = autostart.Checked }; s.Validate(); return s; }
    void UpdateBudget() { if (fps.SelectedItem == null) return; var size = resolution.SelectedIndex switch { 0 => (1280, 720), 1 => (1920, 1080), 2 => (2560, 1440), _ => (3840, 2160) }; var s = original with { Width = size.Item1, Height = size.Item2, Fps = (int)fps.SelectedItem, ClipSeconds = (int)duration.Value }; budget.Text = $"Automatisch: {CapturePolicy.BitrateKbps(s.Width, s.Height, s.Fps) / 1000d:0.#} Mbit/s · RAM-Pufferlimit {CapturePolicy.BufferMegabytes(s)} MB\nGPU-Encoder · keine laufenden Video-Schreibzugriffe · SDR"; }
    public void UpdateStatus() { status.Text = (engine.IsRunning ? "●  " : "○  ") + engine.Status; target.Text = engine.CaptureTarget + "  /  " + engine.EncoderName; toggleButton.Text = engine.IsRunning ? "Puffer stoppen" : "Puffer starten"; clipButton.Enabled = engine.IsRunning; }
    public void Feedback(string text) { feedback.Text = text; }
    void Run(Action action) { try { action(); UpdateStatus(); } catch (Exception e) { Feedback(e.Message); } }
    void RefreshGames() { Run(() => { games.Items.Clear(); games.Items.Add("Alle Clips"); foreach (var g in ClipLibrary.GetGames(original.ClipsDirectory)) games.Items.Add(g); games.SelectedIndex = 0; }); }
    protected override void Dispose(bool disposing) { if (disposing) engine.StatusChanged -= EngineStatus; base.Dispose(disposing); }
}
