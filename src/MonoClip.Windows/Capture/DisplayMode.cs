using System.Runtime.InteropServices;
using MonoClip.Core;
namespace MonoClip.Windows.Capture;

// A fullscreen game in a stretched resolution (e.g. 1280 × 960 on a 1920 × 1080 desktop) switches its monitor to that
// mode while it runs; the desktop resolution stays what Windows' display settings say.
internal static class DisplayMode
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra; public int Fields;
        public int PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels; public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
    const int CurrentSettings = -1, RegistrySettings = -2;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DevMode result);

    // The mode the monitor runs right now, and the one set in Windows' display settings (null when unknown).
    public static Size? Current(string device) => Read(device, CurrentSettings);
    public static Size? Desktop(string device) => Read(device, RegistrySettings);
    public static bool Stretched(string device) =>
        Current(device) is { } now && Desktop(device) is { } desktop && CapturePolicy.IsStretchedMode(now.Width, now.Height, desktop.Width, desktop.Height);

    static Size? Read(string device, int mode)
    {
        var result = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
        return EnumDisplaySettings(device, mode, ref result) && result.PelsWidth > 0 && result.PelsHeight > 0 ? new Size(result.PelsWidth, result.PelsHeight) : null;
    }
}
