using MonoClip.Core;
using MonoClip.Windows.Share;
namespace MonoClip.Windows.UI;

// The "Export clip" window: watch a clip, trim its start and end, choose the audio, then share it or save it as
// an MP4. "Choose clip…" or dropping a file on the window switches to another clip. Space plays and pauses,
// Enter shares, Esc cancels. Trimming is lossless, so the start can move back to the previous keyframe.
internal sealed class ShareEditorForm : Form
{
    static readonly Color Black = Color.FromArgb(12, 12, 12), White = Color.FromArgb(240, 240, 240), Muted = Color.FromArgb(160, 160, 160), Line = Color.FromArgb(48, 48, 48);
    const string LosslessNote = "Lossless cut: the exported clip can start up to a second before the start mark.";
    readonly bool preview; readonly string clipsFolder, exportFolder; readonly Func<string, string> titleOf; readonly Func<string, ClipTrim.Media> probe;
    readonly Panel video = new() { Dock = DockStyle.Fill, BackColor = Color.Black, Margin = new Padding(0) };
    readonly Label empty = new() { Text = "Choose a clip or drop one here.", ForeColor = Color.FromArgb(160, 160, 160), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    readonly TrimBar bar = new() { Dock = DockStyle.Fill };
    readonly Label times = new() { AutoSize = true, Anchor = AnchorStyles.Left }, note = new() { AutoSize = true };
    readonly Button play = new MonoButton(), share = new MonoButton(), save = new MonoButton(), cancel = new MonoButton(), choose = new MonoButton();
    readonly CheckBox desktop = new(), microphone = new();
    readonly FlowLayoutPanel audio = new() { AutoSize = true, WrapContents = true };
    readonly System.Windows.Forms.Timer clock = new() { Interval = 40 };
    ClipPlayer? player; bool priming, loading; int repaint; long clipBytes;
    // The clip in the editor (null until one is opened) and what Media Foundation reported about it.
    public string? Clip { get; private set; }
    public ClipTrim.Media? Media { get; private set; }
    // The chosen edit after the user pressed Share or Save; null when cancelled.
    public ClipEdit? Edit { get; private set; }
    // Where to save the edited clip; null when it is to be shared.
    public string? SavePath { get; private set; }

    // titleOf names a clip for the window title ("Valorant clip"); probe reads a clip (tests replace it).
    public ShareEditorForm(string? clip, string clipsFolder, string exportFolder, Func<string, string> titleOf, Func<string, ClipTrim.Media>? probe = null, bool preview = true)
    {
        this.clipsFolder = clipsFolder; this.exportFolder = exportFolder; this.titleOf = titleOf; this.probe = probe ?? ClipTrim.Probe; this.preview = preview;
        Text = "Export clip"; BackColor = Black; ForeColor = White; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; Icon = SettingsForm.AppIcon();
        StartPosition = FormStartPosition.CenterScreen; MinimizeBox = false; MaximizeBox = false; KeyPreview = true; ShowInTaskbar = true;
        ClientSize = new Size(LogicalToDeviceUnits(640), LogicalToDeviceUnits(590)); MinimumSize = new Size(LogicalToDeviceUnits(460), LogicalToDeviceUnits(420));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(LogicalToDeviceUnits(16)), BackColor = Black };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(52)));
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        video.Controls.Add(empty); layout.Controls.Add(video, 0, 0);
        bar.BackColor = Black; bar.ForeColor = White; bar.Margin = new Padding(0, LogicalToDeviceUnits(8), 0, 0); layout.Controls.Add(bar, 0, 1);

        var transport = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, LogicalToDeviceUnits(6), 0, 0) };
        Style(play, "Play"); play.Click += (_, _) => TogglePlay(); transport.Controls.Add(play);
        times.Margin = new Padding(LogicalToDeviceUnits(10), LogicalToDeviceUnits(8), 0, 0); transport.Controls.Add(times); layout.Controls.Add(transport, 0, 2);
        audio.Margin = new Padding(0, LogicalToDeviceUnits(8), 0, 0); layout.Controls.Add(audio, 0, 3);
        Check(desktop); Check(microphone);
        note.ForeColor = Muted; note.Font = new Font("Segoe UI", 9); note.Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0); layout.Controls.Add(note, 0, 4);

        // Footer: "Choose clip…" on the left, the actions on the right.
        var footer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Style(choose, "Choose clip…"); choose.Margin = Padding.Empty; choose.Click += (_, _) => ChooseClip(); footer.Controls.Add(choose, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        Style(share, "Share"); share.BackColor = White; share.ForeColor = Black; share.Click += (_, _) => Finish(null); actions.Controls.Add(share);
        Style(save, "Save…"); save.Click += (_, _) => ChooseFile(); actions.Controls.Add(save);
        Style(cancel, "Cancel"); cancel.DialogResult = DialogResult.Cancel; actions.Controls.Add(cancel);
        footer.Controls.Add(actions, 1, 0); layout.Controls.Add(footer, 0, 5);
        Controls.Add(layout); AcceptButton = share; CancelButton = cancel;

        bar.RangeChanged += isStart => { Pause(); var at = isStart ? bar.Start : bar.End - TimeSpan.FromMilliseconds(40); bar.Playhead = at; SeekPlayer(at); UpdateTimes(); };
        bar.Seek += SeekPlayer;
        desktop.CheckedChanged += (_, _) => AudioChanged(); microphone.CheckedChanged += (_, _) => AudioChanged();
        video.Click += (_, _) => TogglePlay(); empty.Click += (_, _) => ChooseClip(); video.Resize += (_, _) => player?.UpdateVideo();
        clock.Tick += (_, _) => Tick();
        EnableDrop(this); EnableDrop(desktop); EnableDrop(microphone); // the switches join the window later
        if (clip == null || !Open(clip)) ShowEmpty();
    }
    void Style(Button b, string text) { b.Text = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Line; b.BackColor = Black; b.ForeColor = White; b.AutoSize = true; b.Padding = new Padding(9, 4, 9, 4); b.MinimumSize = new Size(LogicalToDeviceUnits(96), LogicalToDeviceUnits(34)); b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false; }
    static void Check(CheckBox box) { box.Checked = true; box.AutoSize = true; box.FlatStyle = FlatStyle.Flat; box.FlatAppearance.BorderColor = Color.FromArgb(160, 160, 160); box.FlatAppearance.CheckedBackColor = Black; box.Margin = new Padding(0, 2, 18, 2); }

    // Opens another clip in place. Returns false (and keeps the current clip) when it cannot be read.
    public bool Open(string path)
    {
        ClipTrim.Media media;
        try { SharePolicy.CheckShareable(path); media = probe(path); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException or IOException)
        {
            note.Text = $"Cannot open {Path.GetFileName(path)}: {e.Message.TrimEnd('.')}."; return false;
        }
        player?.Dispose(); player = null; priming = false; play.Text = "Play";
        Clip = path; Media = media; clipBytes = new FileInfo(path).Length; Text = "Export clip · " + titleOf(path);
        loading = true;
        audio.Controls.Clear(); desktop.Checked = true; microphone.Checked = true;
        if (media.AudioTracks.Count == 0) audio.Controls.Add(new Label { Text = "No audio", AutoSize = true, ForeColor = Muted });
        else
        {
            bool split = ClipAudio.CanSplit(media.AudioTracks);
            desktop.Text = split ? "Game / desktop audio" : ClipAudio.SingleLabel(media.AudioTracks); audio.Controls.Add(desktop);
            if (split) { microphone.Text = "Microphone"; audio.Controls.Add(microphone); }
        }
        loading = false;
        bar.Duration = media.Duration; bar.Enabled = play.Enabled = share.Enabled = save.Enabled = true; empty.Visible = false;
        note.Text = LosslessNote; UpdateTimes();
        if (IsHandleCreated && Visible) StartPreview();
        return true;
    }
    void ShowEmpty()
    {
        empty.Visible = true; bar.Enabled = play.Enabled = share.Enabled = save.Enabled = false; times.Text = "";
        if (string.IsNullOrEmpty(note.Text)) note.Text = "Drop an MKV or MP4 here, or use Choose clip…";
    }
    void ChooseClip()
    {
        Pause();
        var folder = Clip != null && Path.GetDirectoryName(Clip) is { } current && Directory.Exists(current) ? current : clipsFolder;
        using var dialog = new OpenFileDialog { Title = "Choose a clip", Filter = "Videos (*.mkv;*.mp4)|*.mkv;*.mp4", InitialDirectory = Directory.Exists(folder) ? folder : "", CheckFileExists = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) Open(dialog.FileName);
    }
    // A single MKV/MP4 dropped anywhere on the window replaces the clip. Opened after the drop has finished,
    // so Explorer is not kept waiting.
    void EnableDrop(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, e) => e.Effect = DroppedFile(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
        control.DragDrop += (_, e) => { if (DroppedFile(e.Data) is { } file) BeginInvoke(() => Open(file)); };
        foreach (Control child in control.Controls) EnableDrop(child);
    }
    static string? DroppedFile(IDataObject? data) => data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && Path.GetExtension(files[0]).ToLowerInvariant() is ".mkv" or ".mp4" ? files[0] : null;
    internal void DropFile(string file) => Open(file);

    // Audio track for the current switches (a single switch stands for both).
    int? Track => Media is not { } m ? null : ClipAudio.Choose(m.AudioTracks, desktop.Checked, ClipAudio.CanSplit(m.AudioTracks) ? microphone.Checked : desktop.Checked);
    uint? TrackStream => Track is int t && Media is { } m ? m.AudioStreams[t] : null;
    // The edit Share or Save would produce right now.
    public ClipEdit CurrentEdit => new(bar.Start, bar.End, Track);

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SettingsForm.DarkTitleBar(Handle); }
    protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); if (Clip != null) StartPreview(); clock.Start(); }
    void StartPreview()
    {
        if (!preview || Clip == null || Media == null) return;
        foreach (var old in video.Controls.OfType<Label>().Where(l => l != empty).ToList()) { video.Controls.Remove(old); old.Dispose(); }
        try
        {
            player = new ClipPlayer(Clip, video.Handle, Media.AudioStreams, TrackStream);
            // A clip shows its first frame only after playing: start muted, pause on the first tick that plays.
            player.Muted = true; player.Play(); priming = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            player?.Dispose(); player = null; play.Enabled = false;
            video.Controls.Add(new Label { Text = "Preview not available. Trimming still works.", ForeColor = Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        }
    }
    void Tick()
    {
        if (player == null) return;
        if (priming)
        {
            if (!player.IsPlaying) return;
            player.Pause(); player.Muted = false; player.Position = bar.Start; priming = false; repaint = 5; return;
        }
        if (player.IsPlaying)
        {
            var at = player.Position; bar.Playhead = at;
            // Plays the chosen range only, then waits at its start.
            if (at >= bar.End) { player.Pause(); player.Position = bar.Start; bar.Playhead = bar.Start; repaint = 5; play.Text = "Play"; }
        }
        else if (repaint > 0) { player.UpdateVideo(); repaint--; }
    }
    void TogglePlay()
    {
        if (player == null || priming) return;
        if (player.IsPlaying) { Pause(); return; }
        if (bar.Playhead < bar.Start || bar.Playhead >= bar.End - TimeSpan.FromMilliseconds(100)) { bar.Playhead = bar.Start; player.Position = bar.Start; }
        player.Play(); play.Text = "Pause";
    }
    void Pause() { if (player == null || priming) return; if (player.IsPlaying) player.Pause(); play.Text = "Play"; repaint = 5; }
    void SeekPlayer(TimeSpan at) { if (player == null || priming) return; player.Position = at; repaint = 5; }
    void AudioChanged() { if (loading) return; if (player != null && !priming) { player.SelectAudio(TrackStream); repaint = 5; } UpdateTimes(); }
    void UpdateTimes()
    {
        if (Media == null) return;
        var edit = CurrentEdit; var mb = edit.EstimateBytes(clipBytes, Media.Duration) / (1024.0 * 1024);
        times.Text = $"{Format(edit.Start)} – {Format(edit.End)}  ·  {edit.Length.TotalSeconds:0.0} s  ·  ≈ {mb:0} MB";
    }
    static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 100}";
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Space) { TogglePlay(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    void ChooseFile()
    {
        if (Clip == null) return;
        Pause();
        using var dialog = new SaveFileDialog { Title = "Save the edited clip", Filter = "MP4 video (*.mp4)|*.mp4", DefaultExt = "mp4", AddExtension = true, OverwritePrompt = true, InitialDirectory = exportFolder, FileName = Path.GetFileNameWithoutExtension(Clip) + "-edited.mp4" };
        if (dialog.ShowDialog(this) == DialogResult.OK) Finish(dialog.FileName);
    }
    void Finish(string? savePath) { if (Clip == null) return; Edit = CurrentEdit; SavePath = savePath; DialogResult = DialogResult.OK; Close(); }
    protected override void OnFormClosed(FormClosedEventArgs e) { clock.Stop(); clock.Dispose(); player?.Dispose(); player = null; base.OnFormClosed(e); }
}
