using System.Globalization;
using MonoClip.Core;
using MonoClip.Windows.Share;
using MonoClip.Windows.UI;
using static UiTest;

// Editor tests use real files (they pass the MKV/MP4 checks) with made-up media facts instead of Media Foundation.
internal static class ExportEditorTests
{
    static string editorDir = "";
    static readonly Dictionary<string, ClipTrim.Media> facts = new(StringComparer.OrdinalIgnoreCase);

    public static void Run()
    {
        editorDir = Path.Combine(Path.GetTempPath(), "MonoClip-editor-" + Guid.NewGuid());
        Directory.CreateDirectory(editorDir);
        Test("Share editor starts with the whole clip and picks audio by track name", StartsWithWholeClip);
        Test("Editor switches clips by choosing or dropping, keeps the clip when a file cannot be opened, and can start empty", SwitchesClips);
        Test("Trim bar keeps at least a second and moves the playhead with the keyboard", TrimBarLimitsAndKeys);
    }

    static string FakeClip(string dir, string name, int megabytes = 1)
    {
        var path = Path.Combine(dir, name);
        var bytes = new byte[megabytes * 1024 * 1024];
        if (name.EndsWith(".mkv")) new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }.CopyTo(bytes, 0);
        else "ftypisom"u8.CopyTo(bytes.AsSpan(4));
        File.WriteAllBytes(path, bytes);
        return path;
    }

    static ClipTrim.Media Probe(string path) =>
        facts.TryGetValue(Path.GetFileName(path), out var m) ? m : throw new InvalidDataException("not a video");

    static void StartsWithWholeClip()
    {
        facts["c.mkv"] = new(TimeSpan.FromSeconds(10), ["Microphone", "Desktop", "Playback mix"], [3u, 2u, 1u]);
        using var form = new ShareEditorForm(FakeClip(editorDir, "c.mkv", 100), editorDir, Path.GetTempPath(), _ => "Game clip", Probe, preview: false);
        form.Show();
        Application.DoEvents();
        if (form.Text != "Export clip · Game clip" || Field<Button>(form, "save").Text != "Save…")
            throw new Exception("export window without a save button");
        var bar = Field<TrimBar>(form, "bar");
        var desktop = Field<CheckBox>(form, "desktop");
        var mic = Field<CheckBox>(form, "microphone");
        var times = Field<Label>(form, "times");
        Equal(new ClipEdit(TimeSpan.Zero, TimeSpan.FromSeconds(10), 2), form.CurrentEdit);
        Equal("Game / desktop audio", desktop.Text);
        if (mic.Parent == null) throw new Exception("microphone switch missing");

        bar.Start = TimeSpan.FromSeconds(2);
        bar.End = TimeSpan.FromSeconds(6);
        mic.Checked = false;
        Equal(new ClipEdit(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6), 1), form.CurrentEdit);
        if (!times.Text.Contains("0:02.0 – 0:06.0") || !times.Text.Contains("4.0 s") && !times.Text.Contains("4,0 s") || !times.Text.Contains("40 MB"))
            throw new Exception("times label: " + times.Text);
        mic.Checked = true;
        desktop.Checked = false;
        Equal<int?>(0, form.CurrentEdit.AudioTrack);
        mic.Checked = false;
        Equal<int?>(null, form.CurrentEdit.AudioTrack);
        Field<Button>(form, "share").PerformClick();
        Equal(DialogResult.OK, form.DialogResult);
        Equal(form.CurrentEdit, form.Edit);
        Equal<string?>(null, form.SavePath);

        // Without a separate microphone track there is a single audio switch.
        facts["c.mp4"] = new(TimeSpan.FromSeconds(5), ["Desktop", "Playback mix"], [2u, 1u]);
        using var single = new ShareEditorForm(FakeClip(editorDir, "c.mp4"), editorDir, Path.GetTempPath(), _ => "Clip", Probe, preview: false);
        Equal("Desktop audio", Field<CheckBox>(single, "desktop").Text);
        if (Field<CheckBox>(single, "microphone").Parent != null) throw new Exception("split switch for a clip without separate tracks");
        Equal<int?>(1, single.CurrentEdit.AudioTrack);
        Field<CheckBox>(single, "desktop").Checked = false;
        Equal<int?>(null, single.CurrentEdit.AudioTrack);
    }

    static void SwitchesClips()
    {
        facts["first.mkv"] = new(TimeSpan.FromSeconds(30), ["Playback mix", "Desktop", "Microphone"], [1u, 2u, 3u]);
        facts["second.mp4"] = new(TimeSpan.FromSeconds(8), [], []);
        var first = FakeClip(editorDir, "first.mkv");
        var second = FakeClip(editorDir, "second.mp4");
        var broken = FakeClip(editorDir, "broken.mkv");
        using var form = new ShareEditorForm(null, editorDir, Path.GetTempPath(), p => Path.GetFileNameWithoutExtension(p), Probe, preview: false);
        form.Show();
        Application.DoEvents();
        var share = Field<Button>(form, "share");
        var save = Field<Button>(form, "save");
        var choose = Field<Button>(form, "choose");
        var note = Field<Label>(form, "note");
        var bar = Field<TrimBar>(form, "bar");
        Equal<string?>(null, form.Clip);
        if (share.Enabled || save.Enabled || !choose.Visible || choose.Text != "Choose clip…") throw new Exception("empty editor allows exporting");
        // "Choose clip…" sits bottom left, the actions bottom right.
        int X(Control c) => form.PointToClient(c.PointToScreen(Point.Empty)).X;
        if (X(choose) > form.ClientSize.Width / 3 || X(share) < form.ClientSize.Width / 2) throw new Exception("choose button not bottom left");

        if (!form.Open(first) || form.Clip != first || !share.Enabled || form.Text != "Export clip · first") throw new Exception("first clip not opened");
        Equal(TimeSpan.FromSeconds(30), bar.End);
        bar.Start = TimeSpan.FromSeconds(5);
        Field<CheckBox>(form, "microphone").Checked = false;
        if (form.Open(broken) || form.Clip != first || !note.Text.Contains("broken.mkv")) throw new Exception("broken file replaced the clip: " + note.Text);
        if (form.Open(Path.Combine(editorDir, "missing.mkv")) || form.Clip != first) throw new Exception("missing file accepted");

        form.DropFile(second);
        Equal(second, form.Clip);
        Equal(new ClipEdit(TimeSpan.Zero, TimeSpan.FromSeconds(8), null), form.CurrentEdit);
        if (Field<CheckBox>(form, "microphone").Parent != null || Field<CheckBox>(form, "desktop").Parent != null)
            throw new Exception("audio switches left from the previous clip");
        if (!form.AllowDrop || !Field<Control>(form, "video").AllowDrop) throw new Exception("editor does not accept drops");
        Field<Button>(form, "share").PerformClick();
        Equal(second, form.Clip);
        Equal(TimeSpan.FromSeconds(8), form.Edit!.End);
    }

    static void TrimBarLimitsAndKeys()
    {
        using var bar = new TrimBar { Width = 400, Height = 44 };
        bar.Duration = TimeSpan.FromSeconds(10);
        bar.Start = TimeSpan.FromSeconds(9.8);
        Equal(TimeSpan.FromSeconds(9), bar.Start);
        bar.End = TimeSpan.FromSeconds(9.5);
        Equal(TimeSpan.FromSeconds(10), bar.End);
        bar.Start = TimeSpan.FromSeconds(2);
        bar.End = TimeSpan.FromSeconds(2.5);
        Equal(TimeSpan.FromSeconds(3), bar.End);

        var seeks = new List<TimeSpan>();
        bar.Seek += seeks.Add;
        void Press(Keys keys) => Call(bar, "OnKeyDown", new KeyEventArgs(keys));
        Press(Keys.Home);
        Press(Keys.Right);
        Press(Keys.Right | Keys.Shift);
        Press(Keys.End);
        Equal("2,2.1,3.1,3", string.Join(",", seeks.Select(s => s.TotalSeconds.ToString(CultureInfo.InvariantCulture))));
    }
}
