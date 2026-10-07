namespace MonoClip.Windows.UI;

internal sealed class MonoButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, ClientRectangle);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, FlatAppearance.BorderColor, ButtonBorderStyle.Solid);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(140, 140, 140), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, BackColor);
    }
}
