using MonoClip.Core;
using MonoClip.Windows.UI;
using static UiTest;

internal static class SettingsFormTests
{
    public static void Run()
    {
        Test("Settings form validates values and closes to hidden instead of exiting", ValidatesAndClosesToHidden);
        Test("Beep checkbox is configurable in saved UI values", BeepCheckboxIsSaved);
        Test("Home shows the basics, the gear opens all other settings and back returns", HomeAndSettingsPages);
        Test("Settings window shares, copies the link and shows remaining minutes", SharingControls);
        Test("Mouse wheel scrolls the page instead of changing lists and numbers", WheelScrollsPage);
        Test("Dropping a single clip on the window shares it", DroppedClipIsShared);
        Test("Sort clips by application is an advanced setting, on by default", SortByAppIsAdvanced);
        Test("Player page is the default, direct video link is an advanced option", DirectLinkIsAdvanced);
        Test("Estimated size formatting", FormatsEstimatedSize);
        Test("Screenshot hotkey and folder are full settings; the folder defaults to the clip folder's Screenshots", ScreenshotSettings);
        Test("Output and mic follow the Windows default unless chosen; volumes with a level preview; an unplugged device stays chosen", AudioDeviceSettings);
    }

    static void ValidatesAndClosesToHidden()
    {
        using var form = NewSettingsForm();
        form.Show();
        Application.DoEvents();
        if (!form.Visible) throw new Exception("settings not visible");
        if (Field<CheckBox>(form, "desktop").FlatStyle != FlatStyle.Flat) throw new Exception("system accent color leaks into monochrome controls");
        if (Field<ComboBox>(form, "resolution").DrawMode != DrawMode.OwnerDrawFixed) throw new Exception("system accent leaks into dropdown selections");
        var disabled = Field<Button>(form, "clipButton");
        using (var image = new Bitmap(disabled.Width, disabled.Height))
        {
            disabled.DrawToBitmap(image, disabled.ClientRectangle);
            bool visibleText = false;
            for (int x = 5; x < image.Width - 5; x++)
                for (int y = 5; y < image.Height - 5; y++)
                    if (image.GetPixel(x, y).R > 90) visibleText = true;
            if (!visibleText) throw new Exception("disabled action label has no readable contrast");
        }
        var read = form.ReadSettings();
        read.Validate();
        Field<TextBox>(form, "directory").Text = "";
        Field<ComboBox>(form, "fps").SelectedIndex = 0;
        try
        {
            form.ReadSettings();
            throw new Exception("empty destination accepted");
        }
        catch (ArgumentException) { }
        form.Close();
        Application.DoEvents();
        if (form.IsDisposed || form.Visible) throw new Exception("closing exited or did not hide");
    }

    static void BeepCheckboxIsSaved()
    {
        using var form = NewSettingsForm(new AppSettings { ClipBeep = false });
        var box = Field<CheckBox>(form, "clipBeep");
        Equal(false, form.ReadSettings().ClipBeep);
        box.Checked = true;
        Equal(true, form.ReadSettings().ClipBeep);
    }

    static void HomeAndSettingsPages()
    {
        using var form = NewSettingsForm(new AppSettings { Quality = QualityPreset.Performance });
        form.Show();
        Application.DoEvents();
        Control F(string name) => Field<Control>(form, name);
        string[] basic = ["duration", "hotkey", "autostart", "directory", "pickButton", "toggleButton", "clipButton"];
        string[] advanced = ["resolution", "fps", "quality", "mic", "desktop", "clipBeep", "minimized", "bufferOnLaunch", "games",
            "shareMinutes", "autoShare", "copyLink", "directLink", "sortByApp", "stretchToFill", "desktopDevice", "micDevice", "desktopVolume", "micVolume"];
        foreach (var name in basic) if (!F(name).Visible) throw new Exception(name + " missing on the home page");
        foreach (var name in advanced) if (F(name).Visible) throw new Exception(name + " visible on the home page");
        var budget = (Label)F("budget");
        if (!budget.Visible || !budget.Text.Contains("MB")) throw new Exception("estimated file size missing: " + budget.Text);

        ((Button)F("gear")).PerformClick();
        Application.DoEvents();
        foreach (var name in advanced) if (!F(name).Visible) throw new Exception(name + " hidden on the settings page");
        if (F("duration").Visible) throw new Exception("home still shown behind the settings page");
        Equal(1920, form.ReadSettings().Width);
        Equal(60, form.ReadSettings().Fps);
        var detail = (Label)F("budgetDetail");
        if (!detail.Visible || !detail.Text.Contains("Mbit/s")) throw new Exception("settings estimate lacks bitrate: " + detail.Text);
        var slider = Field<MonoSlider>(form, "quality");
        Equal(QualityPreset.Performance, form.ReadSettings().Quality);
        var before = detail.Text;
        slider.Value = 2;
        Equal(QualityPreset.Quality, form.ReadSettings().Quality);
        if (detail.Text == before) throw new Exception("estimate ignores quality");

        // The settings page has the basics too, in step with home's.
        foreach (var name in new[] { "duration2", "hotkey2", "directory2", "autostart2" })
            if (!F(name).Visible) throw new Exception(name + " missing on the settings page");
        ((NumericUpDown)F("duration2")).Value = 75;
        Equal(75, form.ReadSettings().ClipSeconds);
        ((CheckBox)F("autostart2")).Checked = true;
        Equal(true, form.ReadSettings().StartWithWindows);
        Equal(true, form.ReadSettings().StretchToFill);
        ((CheckBox)F("stretchToFill")).Checked = false;
        Equal(false, form.ReadSettings().StretchToFill);
        ((NumericUpDown)F("duration")).Value = 40;
        Equal(40m, ((NumericUpDown)F("duration2")).Value);
        ((TextBox)F("hotkey")).Text = "Ctrl+Alt+F7";
        Equal("Ctrl+Alt+F7", ((TextBox)F("hotkey2")).Text);

        ((Button)F("back")).PerformClick();
        Application.DoEvents();
        if (!F("duration").Visible || F("resolution").Visible) throw new Exception("back did not return home");
        using (var image = new Bitmap(slider.Width, slider.Height))
        {
            slider.DrawToBitmap(image, slider.ClientRectangle);
            for (int x = 0; x < image.Width; x++)
                for (int y = 0; y < image.Height - slider.Font.Height - 6; y++)
                {
                    var c = image.GetPixel(x, y);
                    if (Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B)) > 12) throw new Exception("slider is not monochrome");
                }
        }
        form.Close();
    }

    static void SharingControls()
    {
        var sharer = new FakeSharer();
        int toggles = 0, copies = 0;
        using var form = NewSettingsForm(new AppSettings { ShareMinutes = 20 });
        form.Show();
        Application.DoEvents();
        Control F(string name) => Field<Control>(form, name);
        if (F("shareButton").Visible) throw new Exception("share button shown without sharing support");
        string? picked = null;
        int edits = 0;
        form.AttachSharing(sharer, () => toggles++, () => edits++, () => copies++, path => picked = path);
        var editShare = (Button)F("editButton");
        if (!F("pickButton").Enabled) throw new Exception("pick button not enabled with sharing support");
        // "Export a file…" sits next to "Open folder" and is part of the simple settings.
        var pick = F("pickButton");
        if (!pick.Visible || pick.Parent == null || pick.Parent.Controls.IndexOf(pick) != 1 || pick.Parent.Controls[0].Text != "Open folder")
            throw new Exception("export button not next to Open folder in simple mode");
        var share = (Button)F("shareButton");
        var copy = (Button)F("copyLinkButton");
        if (!share.Visible || share.Text != "Quick share (20 min)" || copy.Visible || !editShare.Visible || editShare.Text != "Export clip")
            throw new Exception("idle share controls wrong: " + share.Text);
        share.PerformClick();
        Equal(1, toggles);
        editShare.PerformClick();
        Equal(1, edits);

        sharer.ShareAsync("clip.mkv", TimeSpan.FromMinutes(20));
        form.UpdateStatus();
        var status = (Label)F("shareStatus");
        if (!copy.Visible || share.Text != "Stop sharing" || !status.Visible
            || !status.Text.Contains("20 min left") || !status.Text.Contains("fake-host.trycloudflare.com"))
            throw new Exception("active share not shown: " + status.Text);
        copy.PerformClick();
        Equal(1, copies);

        Equal(20, form.ReadSettings().ShareMinutes);
        ((NumericUpDown)F("shareMinutes")).Value = 30;
        Equal(30, form.ReadSettings().ShareMinutes);
        Equal(false, form.ReadSettings().AutoShare);
        ((CheckBox)F("autoShare")).Checked = true;
        Equal(true, form.ReadSettings().AutoShare);
        Equal(true, form.ReadSettings().CopyShareLink);
        ((CheckBox)F("copyLink")).Checked = false;
        Equal(false, form.ReadSettings().CopyShareLink);
        form.Close();
    }

    static void WheelScrollsPage()
    {
        using var form = NewSettingsForm(new AppSettings { AdvancedMode = true });
        form.Show();
        Application.DoEvents();
        var resolution = Field<ComboBox>(form, "resolution");
        var fps = Field<ComboBox>(form, "fps");
        var duration = Field<NumericUpDown>(form, "duration");
        var minutes = Field<NumericUpDown>(form, "shareMinutes");
        var volume = Field<VolumeSlider>(form, "micVolume");
        int loudness = volume.Value;
        int res = resolution.SelectedIndex, rate = fps.SelectedIndex;
        decimal length = duration.Value, life = minutes.Value;
        var targets = new Control[] { resolution, fps, duration, minutes, volume }
            .Concat(duration.Controls.Cast<Control>())
            .Concat(minutes.Controls.Cast<Control>());
        foreach (var delta in new[] { 120, -120 })
            foreach (var target in targets)
            {
                var center = target.PointToScreen(new Point(target.Width / 2, target.Height / 2));
                Wheel.Send(target.Handle, delta, center);
                Application.DoEvents();
            }
        Equal(res, resolution.SelectedIndex);
        Equal(rate, fps.SelectedIndex);
        Equal(length, duration.Value);
        Equal(life, minutes.Value);
        Equal(loudness, volume.Value);
        form.Close();
    }

    static void DroppedClipIsShared()
    {
        using var form = NewSettingsForm();
        var shared = new List<string>();
        form.DropFiles(["C:\\clip.mkv"]);
        Equal(0, shared.Count);
        form.AttachSharing(new FakeSharer(), () => { }, () => { }, () => { }, shared.Add);
        form.DropFiles(["C:\\a.mkv", "C:\\b.mkv"]);
        Equal(0, shared.Count);
        form.DropFiles(["C:\\clip.mkv"]);
        Equal("C:\\clip.mkv", shared.Single());
        if (!form.AllowDrop) throw new Exception("window does not accept drops");
        if (All(form).FirstOrDefault(c => !c.AllowDrop) is { } blocking) throw new Exception(blocking.GetType().Name + " blocks drops");
    }

    static void SortByAppIsAdvanced()
    {
        using var form = NewSettingsForm();
        var box = Field<CheckBox>(form, "sortByApp");
        Equal(true, form.ReadSettings().SortByApp);
        box.Checked = false;
        Equal(false, form.ReadSettings().SortByApp);
        form.Show();
        Application.DoEvents();
        if (box.Visible) throw new Exception("visible in simple mode");
        form.Close();
    }

    static void DirectLinkIsAdvanced()
    {
        using var form = NewSettingsForm();
        var box = Field<CheckBox>(form, "directLink");
        Equal(false, form.ReadSettings().DirectLink);
        box.Checked = true;
        Equal(true, form.ReadSettings().DirectLink);
        form.Show();
        Application.DoEvents();
        if (box.Visible) throw new Exception("visible in simple mode");
        form.Close();
    }

    static void FormatsEstimatedSize()
    {
        Equal("5.0 MB", SettingsForm.FormatSize(5).Replace(',', '.'));
        Equal("58 MB", SettingsForm.FormatSize(57.6));
        Equal("1.5 GB", SettingsForm.FormatSize(1536).Replace(',', '.'));
    }

    static void ScreenshotSettings()
    {
        using var form = NewSettingsForm();
        form.Show();
        Application.DoEvents();
        var key = Field<TextBox>(form, "screenshotHotkey");
        var folder = Field<TextBox>(form, "screenshotDirectory");
        if (key.Visible || folder.Visible) throw new Exception("screenshot settings on the home page");
        Field<Button>(form, "gear").PerformClick();
        Application.DoEvents();
        if (!key.Visible || !folder.Visible || !Field<Button>(form, "defaultScreenshots").Visible) throw new Exception("screenshot settings missing on the settings page");

        var read = form.ReadSettings();
        Equal("Ctrl+Shift+F10", read.ScreenshotHotkey);
        Equal("", read.ScreenshotsDirectory);
        Equal(Path.Combine(read.ClipsDirectory, "Screenshots"), read.ScreenshotFolder());
        Equal(read.ScreenshotFolder(), folder.Text); // the field shows the folder in use, also the default one

        key.Text = "Ctrl+Alt+F8";
        Equal("Ctrl+Alt+F8", form.ReadSettings().ScreenshotHotkey);
        key.Text = form.ReadSettings().Hotkey;
        try
        {
            form.ReadSettings();
            throw new Exception("screenshot hotkey equal to the save hotkey accepted");
        }
        catch (ArgumentException) { }
        key.Text = "Ctrl+Alt+F8";

        folder.Text = @"D:\Shots";
        Equal(@"D:\Shots", form.ReadSettings().ScreenshotFolder());
        Field<Button>(form, "defaultScreenshots").PerformClick();
        Equal("", form.ReadSettings().ScreenshotsDirectory);
        Equal(read.ScreenshotFolder(), folder.Text);
        form.Close();
    }

    static void AudioDeviceSettings()
    {
        using var form = NewSettingsForm();
        form.Show();
        Application.DoEvents();
        var output = Field<ComboBox>(form, "desktopDevice");
        var input = Field<ComboBox>(form, "micDevice");
        if (output.Visible || input.Visible) throw new Exception("device choice on the home page");
        Field<Button>(form, "gear").PerformClick();
        Application.DoEvents();
        foreach (var combo in new[] { output, input })
        {
            if (!combo.Visible) throw new Exception("device choice missing on the settings page");
            Equal("Windows default", combo.Items[0]);
            Equal(0, combo.SelectedIndex);
        }
        var read = form.ReadSettings();
        Equal("default", read.DesktopDevice);
        Equal("default", read.MicrophoneDevice);
        Equal(100, read.DesktopVolume);
        Equal(100, read.MicrophoneVolume);

        // A connected device can be chosen (only where the PC has one).
        if (output.Items.Count > 1)
        {
            output.SelectedIndex = 1;
            if (form.ReadSettings().DesktopDevice == "default") throw new Exception("chosen output not applied");
            output.SelectedIndex = 0;
            Equal("default", form.ReadSettings().DesktopDevice);
        }
        Field<VolumeSlider>(form, "micVolume").Value = 150;
        Equal(150, form.ReadSettings().MicrophoneVolume);
        Field<VolumeSlider>(form, "desktopVolume").Value = 999;
        Equal(VolumeSlider.Max, form.ReadSettings().DesktopVolume);
        form.Close();

        // An unplugged device stays chosen, shown as unavailable.
        using var unplugged = NewSettingsForm(new AppSettings { MicrophoneDevice = "{0.0.1.00000000}.{gone}" });
        var mic = Field<ComboBox>(unplugged, "micDevice");
        Equal("Unavailable device", mic.Text);
        Equal("{0.0.1.00000000}.{gone}", unplugged.ReadSettings().MicrophoneDevice);

        // The preview: the level with the volume applied, on a -60..0 dB scale.
        Equal(1f, VolumeSlider.MeterFraction(1, 100));
        Equal(0f, VolumeSlider.MeterFraction(0, 100));
        Equal(0f, VolumeSlider.MeterFraction(1, 0));
        Equal(1f, VolumeSlider.MeterFraction(0.5f, 200));
        if (Math.Abs(VolumeSlider.MeterFraction(0.5f, 100) - 0.9f) > 0.01f) throw new Exception("-6 dB not at 90 %");
    }

    // Sends a real WM_MOUSEWHEEL, as the mouse would, to the control under the cursor.
    static class Wheel
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        public static void Send(IntPtr window, int delta, Point screen) =>
            SendMessage(window, 0x020A, (IntPtr)(delta << 16), (IntPtr)((screen.Y << 16) | (screen.X & 0xFFFF)));
    }
}
