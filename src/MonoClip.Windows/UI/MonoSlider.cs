namespace MonoClip.Windows.UI;

// Monochrome segmented choice (Performance | Balanced | Quality): the chosen segment is filled. The stock
// TrackBar and radio buttons paint in the system accent colour.
internal sealed class MonoSlider : Control
{
    readonly string[] labels; int value;
    public event EventHandler? ValueChanged;
    public MonoSlider(params string[] labels)
    {
        if (labels.Length < 2) throw new ArgumentException("At least two steps required.", nameof(labels));
        this.labels = labels; TabStop = true; AccessibleRole = AccessibleRole.Slider; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => value;
        set { var next = Math.Clamp(value, 0, labels.Length - 1); if (next == this.value) return; this.value = next; AccessibleDescription = labels[next]; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    public string ValueText => labels[value];
    protected override Size DefaultSize => new(240, 28);
    public override Size GetPreferredSize(Size proposed) => new(LogicalToDeviceUnits(240), LogicalToDeviceUnits(Look.RowHeight - 10));
    RectangleF Segment(int i) { float w = (Width - 1f) / labels.Length; return new RectangleF(i * w, 0, w, Height - 1); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Look.Behind(this)); float radius = LogicalToDeviceUnits(Look.Radius - 2);
        Look.Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), radius, Look.Field);
        var inner = Segment(value); inner.Inflate(-LogicalToDeviceUnits(2), -LogicalToDeviceUnits(2));
        Look.Fill(g, inner, Math.Max(1, radius - 1), Enabled ? Look.Text : Look.Off);
        for (int i = 0; i < labels.Length; i++)
            TextRenderer.DrawText(g, labels[i], Font, Rectangle.Round(Segment(i)), i == value ? Look.Page : Look.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Round(inner), Look.Page, Look.Text);
    }
    int At(int x) => Math.Clamp((int)(x / Math.Max(1f, Width / (float)labels.Length)), 0, labels.Length - 1);
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left) Value = At(e.X); }
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
