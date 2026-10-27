namespace MonoClip.Windows.UI;

// Volume 0–200 % with a live level bar under it: the device's sound as a clip will have it (volume applied), on a mixer's
// dB scale, so the volume can be set by watching it. The wheel scrolls the page, as everywhere in the popup.
internal sealed class VolumeSlider : Control
{
    public const int Max = 200;
    int value = 100; float level;
    public event EventHandler? ValueChanged;
    public VolumeSlider()
    {
        TabStop = true; AccessibleRole = AccessibleRole.Slider; AccessibleDescription = "100 %"; Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => value;
        set { var next = Math.Clamp(value, 0, Max); if (next == this.value) return; this.value = next; AccessibleDescription = next + " %"; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    // The device's peak (0–1) before the volume.
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public float Level { get => level; set { if (Math.Abs(value - level) < 0.002f) return; level = value; Invalidate(); } }
    // Share of the bar lit: the peak with the volume applied, from -60 dB (empty) to 0 dB (full: louder would clip).
    internal static float MeterFraction(float peak, int volume)
    {
        double gained = peak * volume / 100.0;
        return gained <= 0.001 ? 0 : (float)Math.Clamp(1 + 20 * Math.Log10(gained) / 60, 0, 1);
    }
    public override Size GetPreferredSize(Size proposed) => new(LogicalToDeviceUnits(200), LogicalToDeviceUnits(Look.RowHeight - 10));
    RectangleF Track()
    {
        float thumb = LogicalToDeviceUnits(6), text = TextRenderer.MeasureText("200 %", Font).Width + LogicalToDeviceUnits(6);
        return new RectangleF(thumb, Height / 2f - LogicalToDeviceUnits(4), Math.Max(1, Width - text - thumb * 2), LogicalToDeviceUnits(4));
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(Look.Behind(this)); var track = Track(); float x = track.X + track.Width * value / Max;
        Look.Fill(g, track, track.Height / 2, Look.Field);
        Look.Fill(g, new RectangleF(track.X, track.Y, Math.Max(track.Height, x - track.X), track.Height), track.Height / 2, Enabled ? Look.Muted : Look.Off);
        // The level: under the track, lit in white only when it reaches the top (the clip would clip).
        float meter = MeterFraction(level, value), bar = LogicalToDeviceUnits(3);
        if (meter > 0) Look.Fill(g, new RectangleF(track.X, track.Bottom + LogicalToDeviceUnits(4), Math.Max(bar, track.Width * meter), bar), bar / 2, meter >= 0.999f ? Look.Text : Look.Muted);
        // 100 % mark, then the thumb and the value.
        float mark = track.X + track.Width * 100 / Max; using (var pen = new Pen(Look.Page, Math.Max(1, LogicalToDeviceUnits(1)))) g.DrawLine(pen, mark, track.Y, mark, track.Bottom);
        float r = LogicalToDeviceUnits(6); Look.Fill(g, new RectangleF(x - r, track.Y + track.Height / 2 - r, r * 2, r * 2), r, Enabled ? Look.Text : Look.Off);
        TextRenderer.DrawText(g, value + " %", Font, new Rectangle((int)(track.Right + r), 0, Width - (int)(track.Right + r), Height), Look.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, ClientRectangle, Look.Page, Look.Text);
    }
    // 100 % is easy to hit again: the thumb snaps to it nearby.
    void SetFromMouse(int mouseX)
    {
        var track = Track(); int next = (int)Math.Round(Math.Clamp((mouseX - track.X) / track.Width, 0, 1) * Max);
        Value = Math.Abs(next - 100) <= 4 ? 100 : next;
    }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left) SetFromMouse(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (e.Button == MouseButtons.Left) SetFromMouse(e.X); }
    protected override bool IsInputKey(Keys key) => key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(key);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode) { case Keys.Left or Keys.Down: Value -= 5; break; case Keys.Right or Keys.Up: Value += 5; break; case Keys.Home: Value = 0; break; case Keys.End: Value = Max; break; default: return; }
        e.Handled = true;
    }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void WndProc(ref Message m) { if (WheelToPage.Forward(this, ref m)) return; base.WndProc(ref m); }
}
