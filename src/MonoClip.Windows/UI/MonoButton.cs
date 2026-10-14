namespace MonoClip.Windows.UI;

// Slightly rounded flat button; a little lighter under the mouse. Without a border colour it is borderless.
internal sealed class MonoButton : Button
{
    bool hover;
    public MonoButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Look.Behind(this)); float radius = LogicalToDeviceUnits(Look.Radius);
        bool light = BackColor.GetBrightness() > 0.5f;
        var fill = !Enabled ? Look.Field : hover ? Look.Shade(BackColor, light ? -22 : 16) : BackColor;
        Look.Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), radius, fill);
        if (Enabled && !light && FlatAppearance.BorderSize > 0 && FlatAppearance.BorderColor != Color.Empty && FlatAppearance.BorderColor != BackColor)
            using (var pen = new Pen(FlatAppearance.BorderColor)) using (var path = Look.Rounded(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), radius)) g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(140, 140, 140), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, fill);
    }
}
