using MonoClip.Core;
using MonoClip.Windows.Share;
namespace MonoClip.Windows.UI;

// The "Export…" window: watch the clip, trim its start and end, choose the audio, then share it or save it as
// an MP4. Space plays and pauses, Enter shares, Esc cancels. Trimming is lossless, so the start can move back
// to the previous keyframe.
internal sealed class ShareEditorForm : Form
{
    static readonly Color Black = Color.FromArgb(12, 12, 12), White = Color.FromArgb(240, 240, 240), Muted = Color.FromArgb(160, 160, 160), Line = Color.FromArgb(48, 48, 48);
    readonly ClipTrim.Media media; readonly long clipBytes; readonly bool preview;
    readonly Panel video = new() { Dock = DockStyle.Fill, BackColor = Color.Black, Margin = new Padding(0) };
    readonly TrimBar bar = new() { Dock = DockStyle.Fill };
    readonly Label times = new() { AutoSize = true, Anchor = AnchorStyles.Left }, note = new() { AutoSize = true };
    readonly Button play = new MonoButton(), share = new MonoButton(), save = new MonoButton(), cancel = new MonoButton();
    readonly CheckBox desktop = new(), microphone = new();
    readonly System.Windows.Forms.Timer clock = new() { Interval = 40 };
    readonly string clip, exportFolder; ClipPlayer? player; bool priming; int repaint;
    // The chosen edit after the user pressed Share or Save; null when cancelled.
    public ClipEdit? Edit { get; private set; }
    // Where to save the edited clip; null when it is to be shared.
    public string? SavePath { get; private set; }

    public ShareEditorForm(string clip, string title, ClipTrim.Media media, long clipBytes, string exportFolder, bool preview = true)
    {
        this.clip = clip; this.media = media; this.clipBytes = clipBytes; this.exportFolder = exportFolder; this.preview = preview;
        Text = "Export · " + title; BackColor = Black; ForeColor = White; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi; Icon = SettingsForm.AppIcon();
        StartPosition = FormStartPosition.CenterScreen; MinimizeBox = false; MaximizeBox = false; KeyPreview = true; ShowInTaskbar = true;
        ClientSize = new Size(LogicalToDeviceUnits(640), LogicalToDeviceUnits(590)); MinimumSize = new Size(LogicalToDeviceUnits(420), LogicalToDeviceUnits(420));

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(LogicalToDeviceUnits(16)), BackColor = Black };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(52)));
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(video, 0, 0);
        bar.BackColor = Black; bar.ForeColor = White; bar.Margin = new Padding(0, LogicalToDeviceUnits(8), 0, 0); layout.Controls.Add(bar, 0, 1);

        var transport = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, LogicalToDeviceUnits(6), 0, 0) };
        Style(play, "Play"); play.Click += (_, _) => TogglePlay(); transport.Controls.Add(play);
        times.Margin = new Padding(LogicalToDeviceUnits(10), LogicalToDeviceUnits(8), 0, 0); transport.Controls.Add(times); layout.Controls.Add(transport, 0, 2);

        var audio = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, LogicalToDeviceUnits(8), 0, 0) };
        if (media.AudioTracks.Count == 0) audio.Controls.Add(new Label { Text = "No audio", AutoSize = true, ForeColor = Muted });
        else
        {
            bool split = ClipAudio.CanSplit(media.AudioTracks);
            Check(desktop, split ? "Game / desktop audio" : ClipAudio.SingleLabel(media.AudioTracks)); audio.Controls.Add(desktop);
            if (split) { Check(microphone, "Microphone"); audio.Controls.Add(microphone); }
        }
        layout.Controls.Add(audio, 0, 3);
        note.ForeColor = Muted; note.Font = new Font("Segoe UI", 9); note.Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0);
        note.Text = "Lossless cut: the shared clip can start up to a second before the start mark."; layout.Controls.Add(note, 0, 4);

        var footer = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0) };
        Style(share, "Share"); share.BackColor = White; share.ForeColor = Black; share.Click += (_, _) => Finish(null); footer.Controls.Add(share);
        Style(save, "Save…"); save.Click += (_, _) => ChooseFile(); footer.Controls.Add(save);
        Style(cancel, "Cancel"); cancel.DialogResult = DialogResult.Cancel; footer.Controls.Add(cancel);
        layout.Controls.Add(footer, 0, 5);
        Controls.Add(layout); AcceptButton = share; CancelButton = cancel;

        bar.Duration = media.Duration;
        bar.RangeChanged += isStart => { Pause(); var at = isStart ? bar.Start : bar.End - TimeSpan.FromMilliseconds(40); bar.Playhead = at; SeekPlayer(at); UpdateTimes(); };
        bar.Seek += SeekPlayer;
        desktop.CheckedChanged += (_, _) => AudioChanged(); microphone.CheckedChanged += (_, _) => AudioChanged();
        video.Click += (_, _) => TogglePlay(); video.Resize += (_, _) => player?.UpdateVideo();
        clock.Tick += (_, _) => Tick();
        UpdateTimes();
    }
    void Style(Button b, string text) { b.Text = text; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Line; b.BackColor = Black; b.ForeColor = White; b.AutoSize = true; b.Padding = new Padding(9, 4, 9, 4); b.MinimumSize = new Size(LogicalToDeviceUnits(96), LogicalToDeviceUnits(34)); b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false; }
    static void Check(CheckBox box, string text) { box.Text = text; box.Checked = true; box.AutoSize = true; box.FlatStyle = FlatStyle.Flat; box.FlatAppearance.BorderColor = Muted; box.FlatAppearance.CheckedBackColor = Black; box.Margin = new Padding(0, 2, 18, 2); }

    // Audio track for the current switches (a single switch stands for both).
    int? Track => ClipAudio.Choose(media.AudioTracks, desktop.Checked, ClipAudio.CanSplit(media.AudioTracks) ? microphone.Checked : desktop.Checked);
    uint? TrackStream => Track is int t ? media.AudioStreams[t] : null;
    // The edit the Share button would produce right now.
    public ClipEdit CurrentEdit => new(bar.Start, bar.End, Track);

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SettingsForm.DarkTitleBar(Handle); }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e); Activate();
        if (!preview) return;
        try
        {
            player = new ClipPlayer(clip, video.Handle, media.AudioStreams, TrackStream);
            // A clip shows its first frame only after playing: start muted, pause on the first tick that plays.
            player.Muted = true; player.Play(); priming = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            player?.Dispose(); player = null; play.Enabled = false;
            video.Controls.Add(new Label { Text = "Preview not available. Trimming still works.", ForeColor = Muted, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter });
        }
        clock.Start();
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
    void AudioChanged() { if (player != null && !priming) { player.SelectAudio(TrackStream); repaint = 5; } UpdateTimes(); }
    void UpdateTimes()
    {
        var edit = CurrentEdit; var mb = edit.EstimateBytes(clipBytes, media.Duration) / (1024.0 * 1024);
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
        Pause();
        using var dialog = new SaveFileDialog { Title = "Save the edited clip", Filter = "MP4 video (*.mp4)|*.mp4", DefaultExt = "mp4", AddExtension = true, OverwritePrompt = true, InitialDirectory = exportFolder, FileName = Path.GetFileNameWithoutExtension(clip) + "-edited.mp4" };
        if (dialog.ShowDialog(this) == DialogResult.OK) Finish(dialog.FileName);
    }
    void Finish(string? savePath) { Edit = CurrentEdit; SavePath = savePath; DialogResult = DialogResult.OK; Close(); }
    protected override void OnFormClosed(FormClosedEventArgs e) { clock.Stop(); clock.Dispose(); player?.Dispose(); player = null; base.OnFormClosed(e); }
}
