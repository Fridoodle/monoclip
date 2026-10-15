using MonoClip.Core;
using MonoClip.Windows.UI;
using static UiTest;

// Sharing and exporting clips from the tray, with a fake sharer and a fake clipboard.
internal static class TraySharingTests
{
    const string Link = "https://fake-host.trycloudflare.com/t/clip.mp4";
    // Just enough of an MKV header to pass the file checks.
    static readonly byte[] MkvHeader = [0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0, 0, 0, 0, 0];

    public static void Run()
    {
        Test("Auto-share shares each new clip, copies the link and plays clip, upload, link", AutoShareSharesNewClips);
        Test("Manually chosen files are checked before anything is shared", ChosenFilesAreChecked);
        Test("Tray shares the newest clip, copies the link and stops on demand", SharesNewestClip);
        Test("Export clip opens the editor, then shares or saves the edit; cancelling does nothing", ExportGoesThroughEditor);
    }

    static void AutoShareSharesNewClips()
    {
        using var temp = new TempFolder("MonoClip-auto-");
        var dir = temp.Path;
        var sharer = new FakeSharer();
        var played = new List<Sound>();
        string? copied = null;
        var clip = Path.Combine(dir, "auto.mkv");
        ClipRecord NewClip() => new(clip, "Desktop", DateTimeOffset.Now, 5, 1920, 1080, 60, "test", new("Desktop", "", "", "desktop"));

        var autoShare = new AppSettings
        {
            AutoShare = true, ShareMinutes = 10, Hotkey = "Ctrl+Alt+Shift+F6", StartBufferOnLaunch = false, ClipsDirectory = dir,
        };
        using var ctx = new TrayAppContext(new FakeEngine(), autoShare, Path.Combine(dir, "settings.json"), false, played.Add, sharer)
        {
            CopyText = t => copied = t,
        };
        File.WriteAllBytes(clip, MkvHeader);
        RaiseClipSaved(ctx, NewClip());
        Application.DoEvents();
        Equal(clip, sharer.SharedClip);
        Equal(TimeSpan.FromMinutes(10), sharer.Duration);
        Equal(Link, copied);
        Equal("Clip,Upload,Link", string.Join(",", played));

        var manual = new AppSettings { Hotkey = "Ctrl+Alt+Shift+F5", StartBufferOnLaunch = false, ClipsDirectory = dir };
        using var off = new TrayAppContext(new FakeEngine(), manual, Path.Combine(dir, "b.json"), false, _ => { }, sharer);
        sharer.Stop();
        sharer.SharedClip = null;
        RaiseClipSaved(off, NewClip());
        Equal<string?>(null, sharer.SharedClip);

        // Copying can be turned off: the link is shared, the clipboard stays untouched.
        copied = null;
        var noCopy = new AppSettings
        {
            AutoShare = true, CopyShareLink = false, Hotkey = "Ctrl+Alt+Shift+F7", StartBufferOnLaunch = false, ClipsDirectory = dir,
        };
        using var quiet = new TrayAppContext(new FakeEngine(), noCopy, Path.Combine(dir, "c.json"), false, _ => { }, sharer)
        {
            CopyText = t => copied = t,
        };
        RaiseClipSaved(quiet, NewClip());
        Application.DoEvents();
        Equal(clip, sharer.SharedClip);
        Equal<string?>(null, copied);
    }

    static void ChosenFilesAreChecked()
    {
        using var temp = new TempFolder("MonoClip-pick-");
        var dir = temp.Path;
        var sharer = new FakeSharer();
        var played = new List<Sound>();
        string? opened = null;
        var settings = new AppSettings { Hotkey = "Ctrl+Alt+Shift+F4", StartBufferOnLaunch = false, ClipsDirectory = dir };
        using var ctx = new TrayAppContext(new FakeEngine(), settings, Path.Combine(dir, "settings.json"), false, played.Add, sharer)
        {
            CopyText = _ => { },
            EditClip = c => { opened = c; return new(new(TimeSpan.Zero, TimeSpan.FromSeconds(5)), null); },
        };

        var fake = Path.Combine(dir, "notes.mp4");
        File.WriteAllText(fake, "definitely not a video, just text");
        ctx.ExportFile(fake);
        Application.DoEvents();
        Equal<string?>(null, opened);
        Equal<string?>(null, sharer.SharedClip);
        Equal("Error", string.Join(",", played));
        var tray = Tray(ctx);
        if (!tray.Text.Contains("Not a valid")) throw new Exception("error reason not shown in tooltip: " + tray.Text);

        var real = Path.Combine(dir, "real.mp4");
        File.WriteAllBytes(real, [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m']);
        played.Clear();
        ctx.ExportFile(real);
        Application.DoEvents();
        Equal(real, opened);
        Equal(real, sharer.SharedClip);
        Equal("Upload,Link", string.Join(",", played));
    }

    static void SharesNewestClip()
    {
        using var temp = new TempFolder("MonoClip-share-ui-");
        var dir = temp.Path;
        Directory.CreateDirectory(Path.Combine(dir, "Game"));
        var clip = Path.Combine(dir, "Game", "newest.mkv");
        File.WriteAllText(clip, "fixture");
        var sharer = new FakeSharer();
        string? copied = null;

        var plainSettings = new AppSettings { Hotkey = "Ctrl+Alt+Shift+F7", StartBufferOnLaunch = false, ClipsDirectory = dir };
        using (var plain = new TrayAppContext(new FakeEngine(), plainSettings, Path.Combine(dir, "a.json")))
        {
            if (MenuItems(plain).Any(i => i.Text!.Contains("Share") && i.Available)) throw new Exception("share item shown without a sharer");
        }

        var settings = new AppSettings { Hotkey = "Ctrl+Alt+Shift+F8", StartBufferOnLaunch = false, ClipsDirectory = dir };
        using var ctx = new TrayAppContext(new FakeEngine(), settings, Path.Combine(dir, "settings.json"), false, _ => { }, sharer)
        {
            CopyText = text => copied = text,
        };
        var items = MenuItems(ctx);
        var share = ShareItem(ctx);
        Equal("Quick share last clip (15 min)", share.Text);
        var edit = MenuItem(ctx, "Export clip");
        if (!edit.Available || items.IndexOf(edit) != items.IndexOf(share) + 1) throw new Exception("editor item missing or misplaced");
        if (items.IndexOf(share) < 1 || items[^1].Text != "Exit") throw new Exception("share item misplaced");

        share.PerformClick();
        Application.DoEvents();
        Equal(clip, sharer.SharedClip);
        Equal(TimeSpan.FromMinutes(15), sharer.Duration);
        Equal("Game clip", sharer.Options!.Title);
        Equal(false, sharer.Options.DirectLink);
        Equal("MonoClip clip", ctx.ClipTitle(Path.Combine(dir, "flat.mkv")));
        Equal("MonoClip clip", ctx.ClipTitle(@"C:\Elsewhere\Game\x.mkv"));
        Equal(Link, copied);
        if (!share.Text!.StartsWith("Stop sharing (15 min left)")) throw new Exception("active share not shown: " + share.Text);
        if (!edit.Available) throw new Exception("export hidden while a link is online");

        var again = MenuItem(ctx, "Copy link");
        copied = null;
        again.PerformClick();
        Equal(Link, copied);

        share.PerformClick();
        Application.DoEvents();
        Equal<object?>(null, sharer.Current);
        if (!share.Text!.StartsWith("Quick share last clip") || !edit.Available) throw new Exception("stopped share still shown");
    }

    static void ExportGoesThroughEditor()
    {
        using var temp = new TempFolder("MonoClip-edit-");
        var dir = temp.Path;
        var clip = Path.Combine(dir, "c.mkv");
        File.WriteAllBytes(clip, MkvHeader);
        var sharer = new FakeSharer();
        string? opened = null;
        TrayAppContext.ExportChoice? answer = null;
        var played = new List<Sound>();
        var settings = new AppSettings { Hotkey = "Ctrl+Alt+Shift+F2", StartBufferOnLaunch = false, ClipsDirectory = dir };
        using var ctx = new TrayAppContext(new FakeEngine(), settings, Path.Combine(dir, "s.json"), false, played.Add, sharer)
        {
            CopyText = _ => { },
            EditClip = c => { opened = c; return answer; },
        };
        var edit = MenuItem(ctx, "Export clip");

        // Cancelled in the editor: nothing is shared.
        edit.PerformClick();
        Application.DoEvents();
        Equal(clip, opened);
        Equal<string?>(null, sharer.SharedClip);
        Equal(0, played.Count);

        var trim = new ClipEdit(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(7), 2);
        answer = new(trim, null);
        edit.PerformClick();
        Application.DoEvents();
        Equal(clip, sharer.SharedClip);
        Equal(trim, sharer.Options!.Edit);

        // Save goes to the file instead of sharing (this fake clip cannot be trimmed, so it reports an error).
        sharer.Stop();
        sharer.SharedClip = null;
        played.Clear();
        answer = new(trim, Path.Combine(dir, "out.mp4"));
        edit.PerformClick();
        PumpUntil(() => played.Count > 0, 5000);
        Equal<string?>(null, sharer.SharedClip);
        Equal("Error", string.Join(",", played));

        // Dropped and picked files go through the editor too, after the usual checks.
        answer = new(trim, null);
        opened = null;
        ctx.ExportFile(Path.Combine(dir, "notes.txt"));
        Equal<string?>(null, opened);
        ctx.ExportFile(clip);
        Equal(clip, opened);
        Equal(clip, sharer.SharedClip);

        // A clip picked inside the editor is the one exported.
        var other = Path.Combine(dir, "other.mkv");
        File.Copy(clip, other);
        sharer.Stop();
        answer = new(trim, null, other);
        edit.PerformClick();
        Application.DoEvents();
        Equal(other, sharer.SharedClip);

        // Without any clip the editor still opens, empty.
        File.Delete(clip);
        File.Delete(other);
        opened = "unset";
        answer = null;
        edit.PerformClick();
        Application.DoEvents();
        Equal<string?>(null, opened);
    }
}
