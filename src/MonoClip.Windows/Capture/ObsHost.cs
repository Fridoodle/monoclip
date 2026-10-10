using System.Runtime.InteropServices;
using System.Text;
namespace MonoClip.Windows.Capture;

public sealed class ObsHost : IDisposable
{
    bool initialized; string? previousDirectory;
    public IReadOnlyList<string> Encoders { get; private set; } = [];
    public IReadOnlyList<string> Sources { get; private set; } = [];
    static readonly Obs.Log LogDelegate = WriteLog;
    static readonly object LogLock = new();
    // One open writer instead of opening and closing the file for every OBS log line.
    static StreamWriter? logWriter; [ThreadStatic] static byte[]? logBuffer;
    public static string LogPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MonoClip", "capture.log");
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetDllDirectory(string path);
    [DllImport("msvcrt.dll", EntryPoint = "_vsnprintf", CallingConvention = CallingConvention.Cdecl)] static extern int vsnprintf(byte[] buffer, nuint size, IntPtr format, IntPtr args);
    static void WriteLog(int level, IntPtr format, IntPtr args, IntPtr param)
    {
        if (level > 300) return;
        try { var bytes = logBuffer ??= new byte[8192]; var n = vsnprintf(bytes, (nuint)bytes.Length, format, args); if (n < 0) n = bytes.Length - 1; string text = Encoding.UTF8.GetString(bytes, 0, Math.Min(n, bytes.Length - 1)); lock (LogLock) { logWriter ??= new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false)) { AutoFlush = true }; logWriter.Write($"{DateTime.Now:HH:mm:ss} [{level}] {text}\n"); } } catch { /* Never cross a native callback boundary. */ }
    }
    public void Initialize(int width, int height, int fps)
    {
        if (initialized) throw new InvalidOperationException("OBS already initialized");
        var root = AppContext.BaseDirectory;
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        lock (LogLock) { logWriter?.Dispose(); logWriter = null; }
        if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 2_000_000) File.Move(LogPath, LogPath + ".old", true);
        foreach (var required in new[] { "obs.dll", "obs-ffmpeg-mux.exe", "libobs-d3d11.dll", "runtime/data/libobs/default.effect" })
            if (!File.Exists(Path.Combine(root, required))) throw new FileNotFoundException("Aufnahme-Runtime unvollständig (" + required + " fehlt). Das komplette MonoClip-ZIP erneut in einen neuen Ordner entpacken.", required);
        if (!SetDllDirectory(root)) throw new System.ComponentModel.Win32Exception();
        NativeLibrary.Load(Path.Combine(root, "obs.dll"));
        Obs.base_set_log_handler(LogDelegate, IntPtr.Zero);
        var config = Path.Combine(Path.GetDirectoryName(LogPath)!, "module-config"); Directory.CreateDirectory(config);
        if (!Obs.obs_startup("en-US", config, IntPtr.Zero)) throw new InvalidOperationException("libobs initialization failed");
        initialized = true;
        try
        {
            Obs.obs_add_data_path(Path.Combine(root, "runtime/data/libobs").Replace('\\', '/') + "/");
            var graphics = Marshal.StringToCoTaskMemUTF8(Path.Combine(root, "libobs-d3d11.dll"));
            try
            {
                var video = new Obs.VideoInfo { GraphicsModule = graphics, FpsNum = (uint)fps, FpsDen = 1, BaseWidth = (uint)width, BaseHeight = (uint)height, OutputWidth = (uint)width, OutputHeight = (uint)height, Format = 2, Adapter = 0, GpuConversion = true, Colorspace = 2, Range = 1, ScaleType = 3 };
                var result = Obs.obs_reset_video(ref video); if (result != 0) throw new InvalidOperationException($"D3D11 video init failed ({result})");
            }
            finally { Marshal.FreeCoTaskMem(graphics); }
            var audio = new Obs.AudioInfo { Rate = 48000, Speakers = 2 }; if (!Obs.obs_reset_audio(ref audio)) throw new InvalidOperationException("Audio init failed");
            // The stock win-capture Vulkan helper resolves ../../data relative to the working directory.
            previousDirectory = Environment.CurrentDirectory; Directory.SetCurrentDirectory(Path.Combine(root, "runtime/obs-plugins/64bit"));
            // obs-ffmpeg provides the replay buffer; the streaming outputs (obs-outputs) are not needed.
            foreach (var id in new[] { "win-capture", "win-wasapi", "obs-ffmpeg", "obs-nvenc" })
            {
                var dll = Path.Combine(root, "runtime/obs-plugins/64bit", id + ".dll");
                if (!File.Exists(dll)) continue;
                var result = Obs.obs_open_module(out var module, dll.Replace('\\', '/'), Path.Combine(root, "runtime/data/obs-plugins", id).Replace('\\', '/'));
                if (result != 0 || !Obs.obs_init_module(module)) { if (id == "obs-nvenc") continue; throw new InvalidOperationException($"OBS module {id} failed ({result})"); }
            }
            Obs.obs_post_load_modules();
            var encoders = new List<string>(); for (nuint i = 0; Obs.obs_enum_encoder_types(i, out var p); i++) encoders.Add(Obs.Str(p)); Encoders = encoders;
            var sources = new List<string>(); for (nuint i = 0; Obs.obs_enum_input_types(i, out var p); i++) sources.Add(Obs.Str(p)); Sources = sources;
        }
        catch { Dispose(); throw; }
    }
    public void Dispose() { if (initialized) { Obs.obs_shutdown(); initialized = false; if (previousDirectory != null && Directory.Exists(previousDirectory)) Directory.SetCurrentDirectory(previousDirectory); lock (LogLock) { logWriter?.Dispose(); logWriter = null; } } }
}
