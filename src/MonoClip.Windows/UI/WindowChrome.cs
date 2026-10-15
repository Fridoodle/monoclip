using System.Runtime.InteropServices;
namespace MonoClip.Windows.UI;

// Window chrome shared by MonoClip's windows: dark title bars and scroll bars, the popup's rounded corners and
// shadow, the app icon. Cosmetic only: Windows versions without an attribute just return an error, which is ignored.
internal static class WindowChrome
{
    // DWM attributes: 20 (19 before Windows 10 20H1) dark title bar; 33 corner preference (Windows 11, 2 = round);
    // 34/35/36 exact border, caption and text colours.
    const int DarkModeOld = 19, DarkMode = 20, CornerPreference = 33, BorderColor = 34, CaptionColor = 35, TextColor = 36, Round = 2;
    public const int DropShadow = 0x00020000; // class style CS_DROPSHADOW
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    static int ColorRef(Color c) => c.R | c.G << 8 | c.B << 16;
    static bool Set(IntPtr window, int attribute, int value)
    {
        try { return DwmSetWindowAttribute(window, attribute, ref value, sizeof(int)) == 0; }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }
    public static void DarkTitleBar(IntPtr window)
    {
        if (!Set(window, DarkMode, 1)) Set(window, DarkModeOld, 1);
        Set(window, CaptionColor, ColorRef(Color.FromArgb(12, 12, 12))); Set(window, TextColor, ColorRef(Look.Text)); Set(window, BorderColor, ColorRef(Look.Line));
    }
    // The borderless popup: rounded corners with a thin border.
    public static void RoundedFrame(IntPtr window) { Set(window, CornerPreference, Round); Set(window, BorderColor, ColorRef(Look.Line)); }
    public static void DarkScrollbars(IntPtr window)
    {
        try { SetWindowTheme(window, "DarkMode_Explorer", null); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }
    // A tray click allows taking the foreground; without it a popup would not get the focus.
    public static void TakeForeground(IntPtr window) => SetForegroundWindow(window);
    public static bool IsForeground(IntPtr window) => GetForegroundWindow() == window;
    // Embedded in the app; absent when the window is compiled into tests.
    public static Icon? AppIcon()
    {
        using var stream = typeof(WindowChrome).Assembly.GetManifestResourceStream("MonoClip.ico");
        return stream == null ? null : new Icon(stream);
    }
}
