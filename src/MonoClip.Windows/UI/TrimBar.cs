using MonoClip.Core;
namespace MonoClip.Windows.UI;

// Timeline of the share editor: drag the two handles to choose start and end, click or drag elsewhere to move
// the playhead. Arrow keys move the playhead; Home/End jump to the start and end marks.
internal sealed class TrimBar : Control
{
    static readonly Color Track = Color.FromArgb(48, 48, 48), Range = Color.FromArgb(90, 90, 90), Muted = Color.FromArgb(160, 160, 160);
    enum Part { None, Start, End, Playhead }
    TimeSpan duration = TimeSpan.FromSeconds(1), start, end = TimeSpan.FromSeconds(1), playhead; Part dragging;
    // Raised while a handle is dragged (true: start handle) and when the playhead is moved by the user.
    public event Action<bool>? RangeChanged;
    public event Action<TimeSpan>? Seek;
    public TrimBar()
    {
        TabStop = true; AccessibleRole = AccessibleRole.Slider; AccessibleName = "Clip range";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public TimeSpan Duration { get => duration; set { duration = value > TimeSpan.Zero ? value : TimeSpan.FromSeconds(1); start = TimeSpan.Zero; end = duration; playhead = TimeSpan.Zero; Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public TimeSpan Start { get => start; set { start = Clamp(value, TimeSpan.Zero, end - MinLength); Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public TimeSpan End { get => end; set { end = Clamp(value, start + MinLength, duration); Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public TimeSpan Playhead { get => playhead; set { playhead = Clamp(value, TimeSpan.Zero, duration); Invalidate(); } }
    // Short clips can be trimmed to a second, but never to less than the clip itself.
    TimeSpan MinLength => duration < ClipEdit.MinLength * 2 ? duration / 2 : ClipEdit.MinLength;
    static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) => value < min ? min : value > max ? max : value;

    int Pad => LogicalToDeviceUnits(8);
    int HandleWidth => LogicalToDeviceUnits(8);
    float X(TimeSpan t) => Pad + (float)(t / duration) * (Width - 2 * Pad);
    TimeSpan At(int x) => Clamp(duration * ((x - Pad) / (double)Math.Max(1, Width - 2 * Pad)), TimeSpan.Zero, duration);
    protected override Size DefaultSize => new(400, 44);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(BackColor); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        int top = LogicalToDeviceUnits(6), height = Height - 2 * top; float left = X(start), right = X(end);
        using (var track = new SolidBrush(Track)) g.FillRectangle(track, Pad, top, Width - 2 * Pad, height);
        using (var range = new SolidBrush(Range)) g.FillRectangle(range, left, top, right - left, height);
        using (var edge = new Pen(ForeColor, LogicalToDeviceUnits(2))) { g.DrawLine(edge, left, top, right, top); g.DrawLine(edge, left, top + height - 1, right, top + height - 1); }
        using (var handle = new SolidBrush(ForeColor))
        {
            g.FillRectangle(handle, left - HandleWidth, 0, HandleWidth, Height); g.FillRectangle(handle, right, 0, HandleWidth, Height);
        }
        using (var grip = new Pen(BackColor, 1)) foreach (var x in new[] { left - HandleWidth / 2f, right + HandleWidth / 2f }) g.DrawLine(grip, x, Height / 2f - LogicalToDeviceUnits(6), x, Height / 2f + LogicalToDeviceUnits(6));
        float p = X(playhead); using (var line = new Pen(Muted, LogicalToDeviceUnits(2))) g.DrawLine(line, p, 0, p, Height);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, ClientRectangle, ForeColor, BackColor);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Focus(); if (e.Button != MouseButtons.Left) return;
        // Handles sit just outside the range, so a short range stays grabbable at both ends.
        float left = X(start), right = X(end); int grab = LogicalToDeviceUnits(6);
        dragging = e.X >= left - HandleWidth - grab && e.X <= left + grab / 2 ? Part.Start : e.X >= right - grab / 2 && e.X <= right + HandleWidth + grab ? Part.End : Part.Playhead;
        Drag(e.X); Capture = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging != Part.None) Drag(e.X);
        else { float left = X(start), right = X(end); int grab = LogicalToDeviceUnits(6); Cursor = e.X >= left - HandleWidth - grab && e.X <= left + grab / 2 || e.X >= right - grab / 2 && e.X <= right + HandleWidth + grab ? Cursors.SizeWE : Cursors.Hand; }
    }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = Part.None; Capture = false; }
    void Drag(int x)
    {
        var t = At(x);
        switch (dragging)
        {
            case Part.Start: Start = t; RangeChanged?.Invoke(true); break;
            case Part.End: End = t; RangeChanged?.Invoke(false); break;
            case Part.Playhead: Playhead = t; Seek?.Invoke(playhead); break;
        }
    }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = TimeSpan.FromSeconds(e.Shift ? 1 : 0.1);
        TimeSpan? to = e.KeyCode switch { Keys.Left => playhead - step, Keys.Right => playhead + step, Keys.Home => start, Keys.End => end, _ => null };
        if (to is { } t) { Playhead = t; Seek?.Invoke(playhead); e.Handled = true; }
    }
}
