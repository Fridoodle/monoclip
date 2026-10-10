using System.Runtime.InteropServices;
namespace MonoClip.Windows.UI;

// Scrolling the settings page must not change a value the cursor happens to pass over.
// The wheel is handed to the scrollable page instead; an open drop-down list still scrolls itself.
static class WheelToPage
{
    internal const int WmMouseWheel = 0x020A;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    internal static bool Forward(Control control, ref Message m)
    {
        if (m.Msg != WmMouseWheel) return false;
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
            if (parent is ScrollableControl { AutoScroll: true } page && page.IsHandleCreated) { SendMessage(page.Handle, WmMouseWheel, m.WParam, m.LParam); break; }
        m.Result = IntPtr.Zero; return true;
    }
}
internal sealed class MonoComboBox : ComboBox
{
    // Check the message first: DroppedDown itself sends a window message.
    protected override void WndProc(ref Message m) { if (m.Msg == WheelToPage.WmMouseWheel && !DroppedDown && WheelToPage.Forward(this, ref m)) return; base.WndProc(ref m); }
}
// The wheel also reaches this control from its inner text box, which passes unhandled wheel messages up.
internal sealed class MonoNumeric : NumericUpDown
{
    protected override void WndProc(ref Message m) { if (WheelToPage.Forward(this, ref m)) return; base.WndProc(ref m); }
}
