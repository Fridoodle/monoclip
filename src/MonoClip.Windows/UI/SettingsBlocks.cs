using System.Drawing.Drawing2D;
namespace MonoClip.Windows.UI;

// Building blocks of the tray popup: a near-black page of slightly rounded blocks that all share one height.
internal static class Look
{
    public static readonly Color Page = Color.FromArgb(14, 14, 14), Card = Color.FromArgb(27, 27, 27), Field = Color.FromArgb(40, 40, 40),
        Line = Color.FromArgb(52, 52, 52), Text = Color.FromArgb(238, 238, 238), Muted = Color.FromArgb(150, 150, 150), Off = Color.FromArgb(72, 72, 72);
    // Logical (96 DPI) sizes: every card and button row is RowHeight tall.
    public const int RowHeight = 38, Radius = 6, Gap = 6, SectionGap = 16, Pad = 14, Inset = 12;
    public static Font Body() => new("Segoe UI", 9);
    public static Font Strong(float points) => new("Segoe UI Semibold", points);
    public static Font Icons(float points) => new(FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", points);
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
        return path;
    }
    // Half-pixel offset: GDI+ otherwise samples shapes at pixel corners, which leaves a lighter step right after the
    // top corners.
    public static void Fill(Graphics g, RectangleF r, float radius, Color color) { g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; using var path = Rounded(r, radius); using var brush = new SolidBrush(color); g.FillPath(brush, path); }
    // The colour behind a control's rounded corners: the nearest opaque parent.
    public static Color Behind(Control control) { for (var p = control.Parent; p != null; p = p.Parent) if (p.BackColor.A == 255) return p.BackColor; return Page; }
    public static Color Shade(Color c, int delta) => Color.FromArgb(c.A, Math.Clamp(c.R + delta, 0, 255), Math.Clamp(c.G + delta, 0, 255), Math.Clamp(c.B + delta, 0, 255));
    // Whether a block is meant to be shown. Visible itself reads false while the window is still hidden, but the
    // popup measures its pages before it appears; so blocks are shown and hidden through Show().
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> hidden = new();
    public static void Show(Control c, bool shown) { if (shown) hidden.Remove(c); else hidden.AddOrUpdate(c, c); c.Visible = shown; c.Parent?.PerformLayout(); }
    public static bool IsShown(Control c) => !hidden.TryGetValue(c, out _);
}

// One rounded block: a caption on the left, an optional control on the right (or stretched across the rest).
// Clicking the block toggles a switch in it, or focuses its control.
internal sealed class Card : Panel
{
    readonly int controlWidth; readonly bool stretch;
    public Label Caption { get; } = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
    public Control? Control { get; }
    public Card(string caption, Control? control = null, int logicalControlWidth = 0, bool stretch = false)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Look.Card; ForeColor = Look.Text; controlWidth = logicalControlWidth; this.stretch = stretch; Control = control;
        Caption.Text = caption; Caption.BackColor = Look.Card; Controls.Add(Caption);
        if (control != null) { Controls.Add(control); control.AccessibleName ??= caption; }
        Caption.Click += (_, _) => Activate(); Click += (_, _) => Activate(); Cursor = control is CheckBox ? Cursors.Hand : Cursors.Default;
    }
    void Activate() { if (Control is CheckBox box && box.Enabled) box.Checked = !box.Checked; else Control?.Focus(); }
    public override Size GetPreferredSize(Size proposed) => new(proposed.Width, LogicalToDeviceUnits(Look.RowHeight));
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int inset = LogicalToDeviceUnits(Look.Inset), right = Width - LogicalToDeviceUnits(8);
        if (Control is { } c)
        {
            int w = stretch ? Math.Max(LogicalToDeviceUnits(40), right - TextRenderer.MeasureText(Caption.Text, Caption.Font).Width - inset * 2)
                : controlWidth > 0 ? LogicalToDeviceUnits(controlWidth) : c.GetPreferredSize(Size.Empty).Width;
            int h = Math.Min(c is CheckBox ? c.Height : Math.Max(c.Height, c.GetPreferredSize(Size.Empty).Height), Height - LogicalToDeviceUnits(8));
            c.SetBounds(right - w, (Height - h) / 2, w, h);
            Caption.SetBounds(inset, 0, Math.Max(0, c.Left - inset - LogicalToDeviceUnits(6)), Height);
        }
        else Caption.SetBounds(inset, 0, Width - inset * 2, Height);
    }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Look.Behind(this)); Look.Fill(e.Graphics, new RectangleF(0, 0, Width, Height), LogicalToDeviceUnits(Look.Radius), BackColor); }
}

// Buttons side by side, sharing the width evenly; only the visible ones take part.
internal sealed class ButtonRow : Panel
{
    public ButtonRow(params Button[] buttons) { BackColor = Look.Page; foreach (var b in buttons) Controls.Add(b); }
    public override Size GetPreferredSize(Size proposed) => new(proposed.Width, LogicalToDeviceUnits(Look.RowHeight));
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var shown = Controls.Cast<Control>().Where(Look.IsShown).ToList();
        int gap = LogicalToDeviceUnits(Look.Gap), x = 0, count = Math.Max(1, shown.Count), width = (Width - gap * (count - 1)) / count;
        foreach (var c in shown) { c.SetBounds(x, 0, width, Height); x += width + gap; }
    }
}

// The page: a scrolling column of blocks with even gaps; section titles get a little more room above.
internal sealed class Page : Panel
{
    readonly List<(Control Control, int Gap)> items = [];
    public int ContentHeight { get; private set; }
    public Page() { AutoScroll = true; BackColor = Look.Page; Dock = DockStyle.Fill; }
    public T Add<T>(T control, int logicalGap = Look.Gap) where T : Control
    {
        items.Add((control, logicalGap)); Controls.Add(control);
        if (control is Label) control.TextChanged += (_, _) => PerformLayout(); // notes wrap to more or fewer lines
        return control;
    }
    public Label Section(string text) => Add(new Label { Text = text, AutoSize = false, ForeColor = Look.Muted, Font = Look.Strong(8), UseMnemonic = false }, Look.SectionGap);
    public Label Note(string text, int logicalGap = 4) => Add(new Label { Text = text, AutoSize = false, ForeColor = Look.Muted, UseMnemonic = false }, logicalGap);
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int pad = LogicalToDeviceUnits(Look.Pad), width = Math.Max(LogicalToDeviceUnits(100), ClientSize.Width - pad * 2), y = pad; bool first = true;
        foreach (var (c, gap) in items)
        {
            if (!Look.IsShown(c)) continue;
            if (c is Label { Text.Length: 0 }) { c.SetBounds(pad, y + AutoScrollPosition.Y, width, 0); continue; }
            if (!first) y += LogicalToDeviceUnits(gap); first = false;
            int h = c is Label l ? TextRenderer.MeasureText(l.Text, l.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height : c.GetPreferredSize(new Size(width, 0)).Height;
            c.SetBounds(pad, y + AutoScrollPosition.Y, width, h); y += h;
        }
        ContentHeight = y + pad;
        if (AutoScrollMinSize.Height != ContentHeight) AutoScrollMinSize = new Size(0, ContentHeight);
    }
}

// A title on the left and a round icon button on the right (the settings gear, or back).
internal sealed class Header : Panel
{
    public Label Title { get; }
    public Header(string title, Button? icon, Button? back = null)
    {
        BackColor = Look.Page; Title = new Label { Text = title, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = Look.Strong(back == null ? 12 : 11), UseMnemonic = false };
        Controls.Add(Title); if (icon != null) Controls.Add(icon); if (back != null) Controls.Add(back);
    }
    public override Size GetPreferredSize(Size proposed) => new(proposed.Width, LogicalToDeviceUnits(34));
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e); int size = LogicalToDeviceUnits(32), left = 0;
        foreach (var b in Controls.OfType<Button>()) { if (b.Tag is "back") { b.SetBounds(0, (Height - size) / 2, size, size); left = size + LogicalToDeviceUnits(6); } else b.SetBounds(Width - size, (Height - size) / 2, size, size); }
        Title.SetBounds(left, 0, Width - left - size - LogicalToDeviceUnits(6), Height);
    }
}

// On/off switch drawn as a small pill (monochrome; no system accent colour). Still a CheckBox for its value.
internal sealed class MonoToggle : CheckBox
{
    public MonoToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; AutoSize = false; Cursor = Cursors.Hand; Size = new Size(36, 20);
    }
    public override Size GetPreferredSize(Size proposed) => new(LogicalToDeviceUnits(36), LogicalToDeviceUnits(20));
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Look.Behind(this)); float h = Height - 1, w = Math.Min(Width - 1, h * 1.8f), x = Width - 1 - w;
        Look.Fill(g, new RectangleF(x, 0, w, h), h / 2, !Enabled ? Look.Field : Checked ? Look.Text : Look.Off);
        float knob = h - LogicalToDeviceUnits(6), kx = Checked ? x + w - knob - LogicalToDeviceUnits(3) : x + LogicalToDeviceUnits(3);
        using (var brush = new SolidBrush(Checked ? Look.Page : Color.FromArgb(190, 190, 190))) g.FillEllipse(brush, kx, LogicalToDeviceUnits(3), knob, knob);
        if (Focused && ShowFocusCues) using (var pen = new Pen(Look.Muted) { DashStyle = DashStyle.Dot }) using (var path = Look.Rounded(new RectangleF(x - 2, -0.5f, w + 3, h + 1), h / 2 + 1)) g.DrawPath(pen, path);
    }
}

// A rounded input field around a borderless text box (the hotkey, the clip folder).
internal sealed class Field : Panel
{
    public TextBox Box { get; }
    public Field(TextBox box)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Box = box; BackColor = Look.Field; box.BorderStyle = BorderStyle.None; box.BackColor = Look.Field; box.ForeColor = Look.Text; Controls.Add(box); Click += (_, _) => box.Focus();
    }
    public override Size GetPreferredSize(Size proposed) => new(LogicalToDeviceUnits(120), LogicalToDeviceUnits(Look.RowHeight - 10));
    protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); int pad = LogicalToDeviceUnits(8); Box.SetBounds(pad, (Height - Box.Height) / 2, Math.Max(0, Width - pad * 2), Box.Height); }
    protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Look.Behind(this)); Look.Fill(e.Graphics, new RectangleF(0, 0, Width, Height), LogicalToDeviceUnits(Look.Radius - 2), BackColor); }
}

// Number field with − and + at its ends instead of the system spinner (which ignores the dark colours). The − and +
// are drawn into the rounded field itself; the number box is clipped to its text area and centred.
internal sealed class Stepper : Panel
{
    public NumericUpDown Number { get; }
    int hover; // -1: over −, 1: over +
    public Stepper(NumericUpDown number)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Number = number; BackColor = Look.Field; Cursor = Cursors.Hand;
        number.BorderStyle = BorderStyle.None; number.BackColor = Look.Field; number.ForeColor = Look.Text; number.TextAlign = HorizontalAlignment.Center; number.Cursor = Cursors.IBeam;
        if (number.Controls.Count > 0) number.Controls[0].Visible = false; // the spinner arrows
        number.ValueChanged += (_, _) => Invalidate();
        Controls.Add(number);
    }
    int Zone => Height;
    int At(int x) => x < Zone ? -1 : x >= Width - Zone ? 1 : 0;
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); int step = At(e.X); if (e.Button != MouseButtons.Left || step == 0) return;
        Number.Value = Math.Clamp(Number.Value + step * Number.Increment, Number.Minimum, Number.Maximum);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (At(e.X) != hover) { hover = At(e.X); Invalidate(); } }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = 0; Invalidate(); }
    public override Size GetPreferredSize(Size proposed) => new(LogicalToDeviceUnits(112), LogicalToDeviceUnits(Look.RowHeight - 10));
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int middle = Math.Max(0, Width - Zone * 2);
        Number.SetBounds(Zone, 0, middle, Number.Height);
        if (Number.Controls.Count < 2) return;
        // The edit box sits at the top of NumericUpDown, left of the hidden spinner: widen the control by the
        // spinner's room, centre the edit box vertically and show only it.
        var edit = Number.Controls[1]; int spinner = Number.Width - edit.Width - edit.Left;
        Number.SetBounds(Zone, (Height - edit.Font.Height) / 2 - edit.Top, middle + spinner, Number.Height); // the text sits at the top of the edit box
        Number.Region = new Region(new Rectangle(edit.Left, edit.Top, middle - edit.Left, edit.Height));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Look.Behind(this)); float radius = LogicalToDeviceUnits(Look.Radius - 2);
        Look.Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), radius, BackColor);
        foreach (var (step, glyph) in new[] { (-1, "−"), (1, "+") })
        {
            var zone = new Rectangle(step < 0 ? 0 : Width - Zone, 0, Zone, Height);
            bool off = step < 0 ? Number.Value <= Number.Minimum : Number.Value >= Number.Maximum;
            if (hover == step && !off) { var r = (RectangleF)zone; r.Inflate(-LogicalToDeviceUnits(3), -LogicalToDeviceUnits(3)); Look.Fill(g, r, radius, Look.Shade(BackColor, 16)); }
            TextRenderer.DrawText(g, glyph, Font, zone, off ? Look.Off : Look.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
