using MonoClip.Core;
using MonoClip.Windows.UI;
using static UiTest;

// The settings window as a tray popup: applying changes on its own, placement by the tray, closing on focus loss, layout.
internal static class SettingsPopupTests
{
    public static void Run()
    {
        Test("Changes apply by themselves after a short pause, several at once, invalid ones never", ChangesApplyAfterPause);
        Test("Popup sits next to the taskbar by the tray, inside the screen", PopupBoundsByTray);
        Test("Popup closes when it loses focus, applying a pending change first; its own dialogs keep it open", ClosesOnFocusLoss);
        Test("Every block and button row has the same height, and its contents fit inside", BlocksShareOneHeight);
    }

    static void ChangesApplyAfterPause()
    {
        var saves = new List<AppSettings>();
        using var form = NewSettingsForm(save: saves.Add);
        form.Show();
        Application.DoEvents();
        Pump(700);
        Equal(0, saves.Count); // opening applies nothing
        Field<CheckBox>(form, "clipBeep").Checked = false;
        Field<NumericUpDown>(form, "duration").Value = 45;
        Equal(0, saves.Count);
        Pump(800);
        Equal(1, saves.Count);
        Equal(false, saves[0].ClipBeep);
        Equal(45, saves[0].ClipSeconds);
        // A bad value is reported and not applied; the rest stays as it was.
        Field<TextBox>(form, "directory").Text = "";
        Field<CheckBox>(form, "autoShare").Checked = true;
        Pump(800);
        Equal(1, saves.Count);
        if (Field<Label>(form, "feedback").Text.Length == 0) throw new Exception("invalid value not reported");
        form.Close();
    }

    static void PopupBoundsByTray()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        var size = new Size(360, 500);
        // Taskbar at the bottom: the work area ends above it.
        var bottom = new Rectangle(0, 0, 1920, 1040);
        Rectangle Bounds(Size popup, Point click, Rectangle workArea) => SettingsForm.PopupBounds(popup, click, screen, workArea, 12);
        Equal(new Rectangle(1548, 528, 360, 500), Bounds(size, new Point(1850, 1060), bottom)); // tray in the corner
        Equal(new Rectangle(820, 528, 360, 500), Bounds(size, new Point(1000, 1060), bottom)); // centred on the clicked icon
        Equal(new Rectangle(1548, 52, 360, 500), Bounds(size, new Point(1850, 20), new Rectangle(0, 40, 1920, 1040))); // taskbar at the top
        Equal(new Rectangle(1488, 568, 360, 500), Bounds(size, new Point(1900, 1000), new Rectangle(0, 0, 1860, 1080))); // taskbar on the right
        Equal(new Rectangle(72, 568, 360, 500), Bounds(size, new Point(20, 1000), new Rectangle(60, 0, 1860, 1080))); // taskbar on the left
        Equal(new Rectangle(1548, 12, 360, 1016), Bounds(new Size(360, 4000), new Point(1850, 1060), bottom)); // never taller than the screen
    }

    static void ClosesOnFocusLoss()
    {
        var saves = new List<AppSettings>();
        using var form = NewSettingsForm(save: saves.Add);
        void Deactivate() => Call(form, "OnDeactivate", EventArgs.Empty);
        var area = Screen.PrimaryScreen!.WorkingArea;
        form.ShowNear(new Point(area.Right - 40, area.Bottom - 5));
        Application.DoEvents();
        if (!form.Visible || !area.Contains(form.Bounds) || form.Bottom < area.Bottom - 40)
            throw new Exception("popup not by the tray: " + form.Bounds + " in " + area);

        SetField(form, "dialogs", 1);
        Deactivate();
        if (!form.Visible) throw new Exception("closed behind its own dialog");
        SetField(form, "dialogs", 0);

        Field<CheckBox>(form, "clipBeep").Checked = false;
        Deactivate();
        Application.DoEvents();
        if (form.Visible) throw new Exception("popup stayed open after losing focus");
        Equal(1, saves.Count);
        Equal(false, saves[0].ClipBeep);

        // Reopening always starts on the home page.
        Call(form, "ShowMore");
        form.ShowNear(null);
        Application.DoEvents();
        if (!Field<Control>(form, "home").Visible) throw new Exception("reopened on the settings page");
        form.Close();
    }

    static void BlocksShareOneHeight()
    {
        using var form = NewSettingsForm();
        form.AttachSharing(new FakeSharer(), () => { }, () => { }, () => { }, _ => { });
        form.Show();
        Application.DoEvents();
        foreach (var page in new[] { "home", "more" })
        {
            Call(form, page == "home" ? "ShowHome" : "ShowMore");
            Application.DoEvents();
            var blocks = All(Field<Control>(form, page)).Where(c => c.Visible && c is Card or ButtonRow).ToList();
            if (blocks.Count < 5) throw new Exception(page + ": only " + blocks.Count + " blocks");
            var heights = blocks.Select(b => b.Height).Distinct().ToList();
            if (heights.Count != 1) throw new Exception(page + ": block heights differ: " + string.Join(",", heights));
            foreach (var b in blocks)
                foreach (Control inner in b.Controls)
                    if (inner.Visible && (inner.Left < 0 || inner.Top < 0 || inner.Right > b.Width || inner.Bottom > b.Height))
                        throw new Exception($"{page}: {inner.GetType().Name} sticks out of its block ({inner.Bounds} in {b.Size})");
        }
        form.Close();
    }
}
