using System.Reflection;

// Opt-in (--dpi <dir>): moves a real settings window between monitors with different scaling and
// renders it after each move. Needs two monitors with different DPI; checks the layout afterwards.
internal static class DpiProbe
{
    public static int Run(string dir)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Directory.CreateDirectory(dir);
        var screens = Screen.AllScreens;
        if (screens.Length < 2) { Console.WriteLine("SKIP needs two monitors"); return 0; }
        using var form = new MonoClip.Windows.UI.SettingsForm(new MonoClip.Core.AppSettings { AdvancedMode = true }, new FakeEngine(), _ => { }, () => { }, () => { });
        form.StartPosition = FormStartPosition.Manual;
        int failures = 0, step = 0;
        foreach (var screen in screens.Concat(screens))
        {
            form.Location = new Point(screen.WorkingArea.Left + 40, screen.WorkingArea.Top + 40);
            if (!form.Visible) form.Show();
            Pump(1200);
            var problems = Check(form);
            Console.WriteLine($"{(problems.Count == 0 ? "PASS" : "FAIL")} step {++step}: {screen.DeviceName} dpi={form.DeviceDpi} size={form.Size} {string.Join("; ", problems.Take(6))}");
            if (problems.Count > 0) failures++;
            var table = Field<Control>(form, "table");
            using var image = new Bitmap(table.Width, table.Height); table.DrawToBitmap(image, new Rectangle(Point.Empty, table.Size));
            image.Save(Path.Combine(dir, $"dpi-{step}-{form.DeviceDpi}.png"));
        }
        form.Close();
        return failures == 0 ? 0 : 1;
    }
    static void Pump(int ms) { var clock = System.Diagnostics.Stopwatch.StartNew(); while (clock.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(15); } }
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    // Layout sanity at the current DPI: text fits its control, rows do not overlap, nothing is clipped
    // horizontally, fonts and buttons are scaled for this DPI.
    static List<string> Check(Form form)
    {
        var problems = new List<string>(); float scale = form.DeviceDpi / 96f;
        var table = Field<Control>(form, "table");
        var visible = table.Controls.Cast<Control>().Where(c => c.Visible).ToList();
        foreach (var c in visible)
        {
            if (c.Right > table.ClientSize.Width + 1) problems.Add($"{Name(c)} clipped right ({c.Right}>{table.ClientSize.Width})");
            if (c is Label or CheckBox or Button && !string.IsNullOrEmpty(c.Text))
            {
                var need = TextRenderer.MeasureText(c.Text.Split('\n')[0], c.Font);
                if (c.Height < need.Height - 2) problems.Add($"{Name(c)} too short ({c.Height}<{need.Height})");
            }
        }
        // Inputs (drop-downs, text and number boxes) must be tall enough for their text at this DPI.
        foreach (var input in Inputs(table).Where(c => c.Visible))
        {
            var need = TextRenderer.MeasureText("Ag", input.Font).Height;
            if (input.Height < need) problems.Add($"{input.GetType().Name} too short for its text ({input.Height}<{need})");
        }
        for (int i = 0; i < visible.Count; i++) for (int j = i + 1; j < visible.Count; j++)
            if (visible[i].Bounds.IntersectsWith(visible[j].Bounds)) problems.Add($"{Name(visible[i])} overlaps {Name(visible[j])}");
        // Side-by-side rows: label and field share a vertical centre; rows are not inflated.
        foreach (var row in (System.Collections.IEnumerable)Field<object>(form, "rows"))
        {
            var t = (System.Runtime.CompilerServices.ITuple)row; var label = (Control)t[0]!; var input = (Control)t[1]!;
            if (!label.Visible || label.Left >= input.Left) continue;
            int offset = Math.Abs((label.Top + label.Height / 2) - (input.Top + input.Height / 2));
            if (offset > Math.Max(4 * scale, input.Height * 0.3)) problems.Add($"'{label.Text}' is {offset}px off its field");
            int gap = input.Height + input.Margin.Vertical; if (label.Height + label.Margin.Vertical > Math.Max(gap, 0) + 24 * scale) problems.Add($"'{label.Text}' row inflated ({label.Margin.Vertical}px margins)");
        }
        // Fonts follow the monitor: the title must be clearly larger than body text at every DPI.
        var title = visible.First(c => c.Text == "MONOCLIP"); if (title.Height < 30 * scale * 0.8) problems.Add($"title not scaled ({title.Height}px)");
        var save = Field<Button>(form, "clipButton"); if (save.Height < 30 * scale * 0.8) problems.Add($"buttons not scaled ({save.Height}px)");
        return problems;
    }
    static IEnumerable<Control> Inputs(Control root) { foreach (Control c in root.Controls) { if (c is ComboBox or TextBox or NumericUpDown) yield return c; else foreach (var inner in Inputs(c)) yield return inner; } }
    static string Name(Control c) => string.IsNullOrEmpty(c.Text) ? c.GetType().Name : c.GetType().Name + " '" + (c.Text.Length > 18 ? c.Text[..18] : c.Text) + "'";
}
