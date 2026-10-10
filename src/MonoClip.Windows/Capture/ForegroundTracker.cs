using MonoClip.Core;
using System.Runtime.InteropServices;
using System.Text;
namespace MonoClip.Windows.Capture;

internal static class ForegroundTracker
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct DisplayDevice { public int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; public int Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key; }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    const uint GaRoot = 2, ProcessQueryLimitedInformation = 0x1000, DwmwaCloaked = 14; const int GwlExStyle = -20; const long WsExToolWindow = 0x80;
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice result, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, uint attribute, out int value, int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    // Polled every 250 ms: cache what never changes for a monitor or window.
    static readonly Dictionary<string, string> monitorIds = [];
    static IntPtr cachedWindow; static uint cachedProcess; static WindowInfo? cachedInfo;
    // Monitor under the mouse cursor (the cursor marks what the user is looking at).
    public static (string Id, string Name) Monitor()
    {
        var screen = GetCursorPos(out var p) ? Screen.FromPoint(new System.Drawing.Point(p.X, p.Y)) : Screen.PrimaryScreen!;
        if (!monitorIds.TryGetValue(screen.DeviceName, out var id))
        {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            var found = EnumDisplayDevices(screen.DeviceName, 0, ref device, 1);
            monitorIds[screen.DeviceName] = id = StableMonitorId(found ? device.Id : "", screen.DeviceName);
        }
        return (id, screen.DeviceName);
    }
    internal static string StableMonitorId(string? deviceId,string displayName)=>string.IsNullOrWhiteSpace(deviceId)?displayName:deviceId;
    // Top-level window under the mouse cursor, or null when there is none.
    public static (IntPtr Handle, WindowInfo? Info) CursorWindow()
    {
        if (!GetCursorPos(out var p)) return (IntPtr.Zero, null);
        var h = WindowFromPoint(p); if (h != IntPtr.Zero) h = GetAncestor(h, GaRoot);
        if (h == IntPtr.Zero || !IsWindowVisible(h) || Cloaked(h)) return (IntPtr.Zero, null);
        // Class, executable and style belong to the window; only re-read them for a new window
        // (or a reused handle in another process). The size is cheap and may change.
        GetWindowThreadProcessId(h, out var pid);
        if (h != cachedWindow || pid != cachedProcess || cachedInfo == null) { cachedInfo = Describe(h); cachedWindow = h; cachedProcess = pid; }
        else if (GetWindowRect(h, out var r) && (r.Right - r.Left != cachedInfo.Width || r.Bottom - r.Top != cachedInfo.Height)) cachedInfo = cachedInfo with { Width = r.Right - r.Left, Height = r.Bottom - r.Top };
        return (h, cachedInfo);
    }
    static bool Cloaked(IntPtr h) => DwmGetWindowAttribute(h, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
    static WindowInfo Describe(IntPtr h)
    {
        var cls = new StringBuilder(256); GetClassName(h, cls, cls.Capacity);
        GetWindowRect(h, out var r); bool tool = (GetWindowLongPtr(h, GwlExStyle).ToInt64() & WsExToolWindow) != 0;
        return new(cls.ToString(), Text(h), Executable(h), tool, r.Right - r.Left, r.Bottom - r.Top);
    }
    static string Executable(IntPtr h)
    {
        GetWindowThreadProcessId(h, out var pid); var process = OpenProcess(ProcessQueryLimitedInformation, false, pid); if (process == IntPtr.Zero) return "";
        try { var name = new StringBuilder(1024); int size = name.Capacity; return QueryFullProcessImageName(process, 0, name, ref size) ? Path.GetFileName(name.ToString(0, size)) : ""; }
        finally { CloseHandle(process); }
    }
    static string Text(IntPtr h) { var b = new StringBuilder(512); GetWindowText(h, b, b.Capacity); return b.ToString(); }
    public static string Title() => Text(GetForegroundWindow());
}
