using System.Runtime.InteropServices;
using MonoClip.Windows;
using MonoClip.Windows.UI;
using static UiTest;

// Opt-in checks that open real windows, play real sound or write images; never part of a plain run.
// --dpi lives in DpiProbe.
internal static class OptInModes
{
    public static void Run(string[] args)
    {
        if (args.Contains("--reveal")) Test("Clip folder opens in Explorer with the newest clip selected", RevealSelectsNewestClip);
        // Visual check: renders the tray symbols to PNG (the settings popup has its own --popup mode).
        if (args.Contains("--render")) Test("Render tray symbols", () => RenderTraySymbols(Program.ArgAfter(args, "--render")));
        if (args.Contains("--play-sounds")) Test("All sound cues play through Windows audio", PlayAllSounds);
    }

    static void RevealSelectsNewestClip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MonoClip-reveal-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(dir, "Game"));
        Directory.CreateDirectory(Path.Combine(dir, "Desktop"));
        var old = Path.Combine(dir, "Desktop", "old.mkv");
        File.WriteAllText(old, "fixture");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-1));
        var newest = Path.Combine(dir, "Game", "newest.mkv");
        File.WriteAllText(newest, "fixture");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
        string? selected = null;
        try
        {
            WindowsIntegration.OpenClipFolder(dir);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (selected == null && clock.ElapsedMilliseconds < 8000)
            {
                Application.DoEvents();
                Thread.Sleep(200);
                // Find the Explorer window showing the game folder and read its selection.
                foreach (dynamic w in shell.Windows())
                {
                    try
                    {
                        if (!string.Equals((string)w.Document.Folder.Self.Path, Path.GetDirectoryName(newest), StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (dynamic item in w.Document.SelectedItems()) selected = (string)item.Path;
                        if (selected != null) w.Quit();
                    }
                    catch (Exception) { }
                }
            }
            Equal(newest, selected);
        }
        finally
        {
            Thread.Sleep(300);
            Directory.Delete(dir, true);
        }
    }

    static void RenderTraySymbols(string dir)
    {
        Directory.CreateDirectory(dir);
        // Row per taskbar colour: recording, stopped, recording+online, creating (2 frames), error.
        var states = new (TrayIconKind Kind, bool Online, int Frame)[]
        {
            (TrayIconKind.Recording, false, 0), (TrayIconKind.Stopped, false, 0), (TrayIconKind.Recording, true, 0),
            (TrayIconKind.Busy, false, 0), (TrayIconKind.Busy, false, 4), (TrayIconKind.Error, false, 0),
        };
        using var sheet = new Bitmap(states.Length * 46 + 8, 140);
        using (var g = Graphics.FromImage(sheet))
        {
            g.FillRectangle(new SolidBrush(Color.FromArgb(32, 32, 32)), 0, 0, sheet.Width, 70);
            g.FillRectangle(new SolidBrush(Color.FromArgb(238, 238, 238)), 0, 70, sheet.Width, 70);
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < states.Length; i++)
                {
                    using var icon = TrayAppContext.MakeIcon(states[i].Kind, 32, states[i].Online, states[i].Frame);
                    g.DrawIcon(icon, 8 + i * 46, row * 70 + 6);
                    using var small = TrayAppContext.MakeIcon(states[i].Kind, 16, states[i].Online, states[i].Frame);
                    g.DrawIcon(small, 16 + i * 46, row * 70 + 46);
                }
        }
        sheet.Save(Path.Combine(dir, "tray.png"));
    }

    static void PlayAllSounds()
    {
        using var sounds = new SoundEffects();
        foreach (var sound in Enum.GetValues<Sound>()) sounds.Play(sound);
        Pump(2500, step: 20);
    }

    // --editor <clip>: opens the real share editor for a clip with a live preview, saves a screenshot after it has
    // loaded and prints the edit (--interactive keeps it open until closed).
    public static int EditorProbe(string clip, bool interactive)
    {
        using var form = new ShareEditorForm(clip, Path.GetDirectoryName(clip)!, Path.GetTempPath(), _ => "Test clip");
        if (interactive)
        {
            form.ShowDialog();
            Console.WriteLine("EDIT " + form.Edit + " SAVE " + form.SavePath);
            return 0;
        }
        form.Show();
        Pump(2500, step: 15);
        using var shot = new Bitmap(form.Width, form.Height);
        using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(form.Location, Point.Empty, form.Size);
        var file = Path.Combine(Path.GetTempPath(), "monoclip-editor.png");
        shot.Save(file);
        Console.WriteLine("SHOT " + file + " EDIT " + form.CurrentEdit);
        form.Close();
        return 0;
    }

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);

    // --popup <dir>: draws the settings popup (home idle, home sharing, the whole settings page) into PNGs. The window is
    // placed off-screen and shown without taking the focus, so nothing appears in front of the user.
    public static int PopupRender(string dir)
    {
        Directory.CreateDirectory(dir);
        void Shot(Control c, string name)
        {
            using var image = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(image, new Rectangle(Point.Empty, c.Size));
            image.Save(Path.Combine(dir, name));
            Console.WriteLine("SHOT " + Path.Combine(dir, name));
        }
        using var form = NewSettingsForm();
        var sharer = new FakeSharer();
        form.AttachSharing(sharer, () => { }, () => { }, () => { }, _ => { });
        form.Location = new Point(-20000, -20000);
        var handle = form.Handle;
        typeof(Control).GetMethod("CreateControl", Hidden, [typeof(bool)])!.Invoke(form, [true]);
        SetWindowPos(handle, IntPtr.Zero, -20000, -20000, form.Width, form.Height, 0x0040 | 0x0010 | 0x0004); // SHOWWINDOW | NOACTIVATE | NOZORDER
        var home = Field<Page>(form, "home");
        var more = Field<Page>(form, "more");
        // Sizes the window to the page's content, so the shot shows all of it.
        void Fit(Page page)
        {
            page.PerformLayout();
            form.ClientSize = new Size(form.ClientSize.Width, page.ContentHeight);
            Application.DoEvents();
            page.PerformLayout();
            Application.DoEvents();
        }
        Fit(home);
        Shot(form, "popup-home.png");
        sharer.ShareAsync("c.mkv", TimeSpan.FromMinutes(15));
        form.UpdateStatus();
        Fit(home);
        Shot(form, "popup-home-sharing.png");
        Call(form, "ShowMore");
        Fit(more);
        // Example levels, so the shot shows the volume preview (a quiet and a peaking source).
        Field<VolumeSlider>(form, "desktopVolume").Level = 0.25f;
        Field<VolumeSlider>(form, "micVolume").Level = 1f;
        Shot(form, "popup-settings.png");
        form.Close();
        return 0;
    }
}
