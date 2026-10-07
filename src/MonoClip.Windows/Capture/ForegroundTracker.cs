using System.Runtime.InteropServices;
using System.Text;
namespace MonoClip.Windows.Capture;

internal static class ForegroundTracker
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct DisplayDevice { public int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; public int Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key; }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice result, uint flags);
    public static (string Id, string Name) Monitor()
    {
        var h = GetForegroundWindow(); var screen = h == IntPtr.Zero ? Screen.PrimaryScreen! : Screen.FromHandle(h);
        var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        var found=EnumDisplayDevices(screen.DeviceName,0,ref device,1);
        return (StableMonitorId(found?device.Id:"",screen.DeviceName),screen.DeviceName);
    }
    internal static string StableMonitorId(string? deviceId,string displayName)=>string.IsNullOrWhiteSpace(deviceId)?displayName:deviceId;
    public static string Title() { var b = new StringBuilder(512); GetWindowText(GetForegroundWindow(), b, b.Capacity); return b.ToString(); }
}
