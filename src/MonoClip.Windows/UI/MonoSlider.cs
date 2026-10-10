namespace MonoClip.Windows.UI;

// Monochrome stepped slider. The stock TrackBar paints its thumb in the system accent color.
internal sealed class MonoSlider : Control
{
    static readonly Color Track = Color.FromArgb(70, 70, 70), Muted = Color.FromArgb(160, 160, 160);
    readonly string[] labels; int value;
    public event EventHandler? ValueChanged;
    public MonoSlider(params string[] labels)
    {
        if (labels.Length < 2) throw new ArgumentException("At least two steps required.", nameof(labels));
        this.labels = labels; TabStop = true; AccessibleRole = AccessibleRole.Slider;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => value;
        set { var next = Math.Clamp(value, 0, labels.Length - 1); if (next == this.value) return; this.value = next; AccessibleDescription = labels[next]; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    public string ValueText => labels[value];
    int Thumb => Math.Max(7, Font.Height / 2);
    int TrackY => Thumb + 3;
    float StopX(int i) => Thumb + 2 + i * (Width - 2f * (Thumb + 2)) / (labels.Length - 1);
    protected override Size DefaultSize => new(300, 58);
    public override Size GetPreferredSize(Size proposed) => new(Math.Max(proposed.Width, 200), TrackY + Thumb + Font.Height + 10);
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Height = GetPreferredSize(Size.Empty).Height; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.Clear(BackColor);
        using (var line = new Pen(Track, 2)) g.DrawLine(line, StopX(0), TrackY, StopX(labels.Length - 1), TrackY);
        using (var active = new Pen(ForeColor, 2)) g.DrawLine(active, StopX(0), TrackY, StopX(value), TrackY);
        for (int i = 0; i < labels.Length; i++)
        {
            float x = StopX(i); using (var stop = new SolidBrush(i <= value ? ForeColor : Track)) g.FillEllipse(stop, x - 3, TrackY - 3, 6, 6);
            var size = TextRenderer.MeasureText(labels[i], Font); int left = i == 0 ? 0 : i == labels.Length - 1 ? Width - size.Width : (int)(x - size.Width / 2f);
            TextRenderer.DrawText(g, labels[i], Font, new Point(left, TrackY + Thumb + 4), i == value ? ForeColor : Muted);
        }
        float tx = StopX(value); using (var thumb = new SolidBrush(ForeColor)) g.FillEllipse(thumb, tx - Thumb, TrackY - Thumb, Thumb * 2, Thumb * 2);
        if (Focused && ShowFocusCues) using (var focus = new Pen(ForeColor) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot }) g.DrawEllipse(focus, tx - Thumb - 3, TrackY - Thumb - 3, Thumb * 2 + 6, Thumb * 2 + 6);
    }
    int Nearest(int x) { int best = 0; for (int i = 1; i < labels.Length; i++) if (Math.Abs(StopX(i) - x) < Math.Abs(StopX(best) - x)) best = i; return best; }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left) Value = Nearest(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) Value = Nearest(e.X); }
    protected override bool IsInputKey(Keys key) => key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(key);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode) { case Keys.Left or Keys.Down: Value--; break; case Keys.Right or Keys.Up: Value++; break; case Keys.Home: Value = 0; break; case Keys.End: Value = labels.Length - 1; break; default: return; }
        e.Handled = true;
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
