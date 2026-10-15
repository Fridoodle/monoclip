namespace MonoClip.Windows.UI;

// Dark drop-down and number box for the settings popup; the wheel scrolls the page instead of changing them.
internal sealed class MonoComboBox : ComboBox
{
    // Owner-drawn rows have a fixed height that WinForms does not rescale when the window moves to a
    // monitor with different scaling; recompute it whenever the DPI or the (rescaled) font changes.
    internal void FitItemHeight() { if (DrawMode != DrawMode.Normal) ItemHeight = Math.Max(LogicalToDeviceUnits(24), Font.Height + LogicalToDeviceUnits(6)); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitItemHeight(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); FitItemHeight(); }
    // Check the message first: DroppedDown itself sends a window message.
    protected override void WndProc(ref Message m) { if (m.Msg == WheelToPage.WmMouseWheel && !DroppedDown && WheelToPage.Forward(this, ref m)) return; base.WndProc(ref m); }
    // The closed box paints itself as a rounded field with a chevron: Windows draws a light border and arrow box even
    // when flat. The open list stays Windows' own (rows drawn by the owner's DrawItem).
    public MonoComboBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Look.Behind(this));
        var r = new RectangleF(0, 0, Width - 1, Height - 1); Look.Fill(g, r, LogicalToDeviceUnits(Look.Radius - 2), BackColor);
        if (Focused) using (var pen = new Pen(Look.Muted)) using (var path = Look.Rounded(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), LogicalToDeviceUnits(Look.Radius - 2))) g.DrawPath(pen, path);
        int arrow = LogicalToDeviceUnits(22), pad = LogicalToDeviceUnits(8);
        TextRenderer.DrawText(g, SelectedItem?.ToString() ?? "", Font, new Rectangle(pad, 0, Width - pad - arrow, Height), Enabled ? ForeColor : Look.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        using var icons = Look.Icons(7.5f);
        TextRenderer.DrawText(g, "\uE70D", icons, new Rectangle(Width - arrow - LogicalToDeviceUnits(2), 0, arrow, Height), Look.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine); // chevron down
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
}
// The wheel also reaches this control from its inner text box, which passes unhandled wheel messages up.
internal sealed class MonoNumeric : NumericUpDown
{
    protected override void WndProc(ref Message m) { if (WheelToPage.Forward(this, ref m)) return; base.WndProc(ref m); }
}
