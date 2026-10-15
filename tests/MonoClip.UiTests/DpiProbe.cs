using System.Reflection;

// Opt-in (--dpi <dir>): moves a real settings popup between monitors with different scaling and renders both
// pages after each move. Needs two monitors with different DPI; checks the layout afterwards.
internal static class DpiProbe
{
    public static int Run(string dir)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Directory.CreateDirectory(dir);
        var screens = Screen.AllScreens;
        if (screens.Length < 2) { Console.WriteLine("SKIP needs two monitors"); return 0; }
        using var form = new MonoClip.Windows.UI.SettingsForm(new MonoClip.Core.AppSettings(), new FakeEngine(), _ => { }, () => { }, () => { });
        int failures = 0, step = 0;
        foreach (var screen in screens.Concat(screens))
        {
            var area = screen.WorkingArea; form.ShowNear(new Point(area.Right - 40, area.Bottom - 5));
            Pump(1200); step++;
            foreach (var page in new[] { "home", "more" })
            {
                form.GetType().GetMethod(page == "home" ? "ShowHome" : "ShowMore", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null); Pump(300);
                var problems = Check(form, Field<Control>(form, page));
                if (page == "home" && !area.Contains(form.Bounds)) problems.Add($"popup {form.Bounds} outside the work area {area}");
                Console.WriteLine($"{(problems.Count == 0 ? "PASS" : "FAIL")} step {step} {page}: {screen.DeviceName} dpi={form.DeviceDpi} size={form.Size} {string.Join("; ", problems.Take(6))}");
                if (problems.Count > 0) failures++;
                using var image = new Bitmap(form.Width, form.Height); form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                image.Save(Path.Combine(dir, $"dpi-{step}-{page}-{form.DeviceDpi}.png"));
            }
        }
        form.Close();
        return failures == 0 ? 0 : 1;
    }
    static void Pump(int ms) { var clock = System.Diagnostics.Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(15); } }
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    static IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(x => All(x).Prepend(x));
    // Layout sanity at the current DPI: blocks share one scaled height and do not overlap, their contents fit and
    // are centred, inputs are tall enough for their text, nothing is clipped at the right.
    static List<string> Check(Form form, Control page)
    {
        var problems = new List<string>(); float scale = form.DeviceDpi / 96f;
        var items = page.Controls.Cast<Control>().Where(c => c.Visible && c.Height > 0).ToList();
        foreach (var c in items) if (c.Right > page.ClientSize.Width + 1) problems.Add($"{Name(c)} clipped right ({c.Right}>{page.ClientSize.Width})");
        for (int i = 0; i < items.Count; i++) for (int j = i + 1; j < items.Count; j++)
            if (items[i].Bounds.IntersectsWith(items[j].Bounds)) problems.Add($"{Name(items[i])} overlaps {Name(items[j])}");
        var blocks = items.Where(c => c.GetType().Name is "Card" or "ButtonRow").ToList();
        foreach (var h in blocks.Select(b => b.Height).Distinct()) if (Math.Abs(h - 38 * scale) > 2) problems.Add($"block height {h}, expected {38 * scale:0}");
        foreach (var b in blocks) foreach (var inner in b.Controls.Cast<Control>().Where(c => c.Visible))
        {
            if (inner.Left < 0 || inner.Top < 0 || inner.Right > b.Width || inner.Bottom > b.Height) problems.Add($"{Name(inner)} sticks out of its block");
            if (b.GetType().Name == "Card" && Math.Abs(inner.Top + inner.Height / 2 - b.Height / 2) > 2 * scale) problems.Add($"{Name(inner)} off centre in its block");
        }
        foreach (var input in All(page).Where(c => c.Visible && c is ComboBox or TextBox or NumericUpDown))
        {
            var need = TextRenderer.MeasureText("Ag", input.Font).Height;
            if (input.Height < need) problems.Add($"{input.GetType().Name} too short for its text ({input.Height}<{need})");
        }
        return problems;
    }
    static string Name(Control c) => string.IsNullOrEmpty(c.Text) ? c.GetType().Name : c.GetType().Name + " '" + (c.Text.Length > 18 ? c.Text[..18] : c.Text) + "'";
}
