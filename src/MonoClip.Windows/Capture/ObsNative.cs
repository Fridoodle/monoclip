using System.Runtime.InteropServices;
namespace MonoClip.Windows.Capture;

internal static class Obs
{
    const string D = "obs.dll";
    [StructLayout(LayoutKind.Sequential)] internal struct VideoInfo { public IntPtr GraphicsModule; public uint FpsNum, FpsDen, BaseWidth, BaseHeight, OutputWidth, OutputHeight; public int Format; public uint Adapter; [MarshalAs(UnmanagedType.I1)] public bool GpuConversion; public int Colorspace, Range, ScaleType; }
    [StructLayout(LayoutKind.Sequential)] internal struct AudioInfo { public uint Rate; public int Speakers; }
    [StructLayout(LayoutKind.Sequential)] internal struct CallData { public IntPtr Stack; public nuint Size, Capacity; [MarshalAs(UnmanagedType.I1)] public bool Fixed; }
    [StructLayout(LayoutKind.Sequential)] internal struct Vec2 { public float X, Y; public Vec2(float x, float y) { X = x; Y = y; } }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Signal(IntPtr param, IntPtr data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Log(int level, IntPtr format, IntPtr args, IntPtr param);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_startup([MarshalAs(UnmanagedType.LPUTF8Str)] string locale, [MarshalAs(UnmanagedType.LPUTF8Str)] string config, IntPtr profiler);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_shutdown();
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_add_data_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern int obs_reset_video(ref VideoInfo info);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_reset_audio(ref AudioInfo info);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern int obs_open_module(out IntPtr module, [MarshalAs(UnmanagedType.LPUTF8Str)] string file, [MarshalAs(UnmanagedType.LPUTF8Str)] string data);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_init_module(IntPtr module);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_post_load_modules();
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_enum_encoder_types(nuint idx, out IntPtr id);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_enum_input_types(nuint idx, out IntPtr id);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void base_set_log_handler(Log callback, IntPtr param);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_get_video();
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_get_audio();
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_data_create();
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_data_release(IntPtr data);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_data_set_string(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_data_set_int(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, long value);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_data_set_bool(IntPtr data, [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.I1)] bool value);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_source_create([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr settings, IntPtr hotkeys);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_source_update(IntPtr source, IntPtr settings);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_source_release(IntPtr source);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern uint obs_source_get_width(IntPtr source);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern uint obs_source_get_height(IntPtr source);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_source_properties(IntPtr source);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_properties_get(IntPtr props, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern nuint obs_property_list_item_count(IntPtr prop);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_property_list_item_name(IntPtr prop, nuint index);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_property_list_item_string(IntPtr prop, nuint index);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_properties_destroy(IntPtr props);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_source_set_audio_mixers(IntPtr source, uint mixers);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_source_get_proc_handler(IntPtr source);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_set_output_source(uint channel, IntPtr source);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_scene_create([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_scene_get_source(IntPtr scene);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_scene_add(IntPtr scene, IntPtr source);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_scene_release(IntPtr scene);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_bounds_type(IntPtr item, int type);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_bounds_alignment(IntPtr item, uint align);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_bounds(IntPtr item, ref Vec2 bounds);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_sceneitem_set_visible(IntPtr item, [MarshalAs(UnmanagedType.I1)] bool visible);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_video_encoder_create([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr data, IntPtr hotkeys);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_audio_encoder_create([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr data, nuint mixer, IntPtr hotkeys);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_encoder_set_video(IntPtr encoder, IntPtr video);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_encoder_set_audio(IntPtr encoder, IntPtr audio);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_encoder_release(IntPtr encoder);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_output_create([MarshalAs(UnmanagedType.LPUTF8Str)] string id, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr data, IntPtr hotkeys);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_output_update(IntPtr output, IntPtr settings);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_output_set_video_encoder(IntPtr output, IntPtr encoder);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_output_set_audio_encoder(IntPtr output, IntPtr encoder, nuint index);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_output_start(IntPtr output);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool obs_output_active(IntPtr output);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_output_force_stop(IntPtr output);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void obs_output_release(IntPtr output);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_output_get_last_error(IntPtr output);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_output_get_proc_handler(IntPtr output);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern IntPtr obs_output_get_signal_handler(IntPtr output);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern int obs_output_get_total_frames(IntPtr output);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern int obs_output_get_frames_dropped(IntPtr output);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void signal_handler_connect(IntPtr handler, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, Signal callback, IntPtr param);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void signal_handler_disconnect(IntPtr handler, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, Signal callback, IntPtr param);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool proc_handler_call(IntPtr handler, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref CallData data);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool calldata_get_string(ref CallData data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out IntPtr str);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)][return: MarshalAs(UnmanagedType.I1)] internal static extern bool calldata_get_data(ref CallData data, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out byte value, nuint size);
    [DllImport(D, CallingConvention = CallingConvention.Cdecl)] internal static extern void bfree(IntPtr ptr);
    internal static string Str(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";
}
internal sealed class ObsData : IDisposable
{
    public IntPtr Handle { get; } = Obs.obs_data_create();
    public ObsData Set(string key, string value) { Obs.obs_data_set_string(Handle, key, value); return this; }
    public ObsData Set(string key, long value) { Obs.obs_data_set_int(Handle, key, value); return this; }
    public ObsData Set(string key, bool value) { Obs.obs_data_set_bool(Handle, key, value); return this; }
    public void Dispose() => Obs.obs_data_release(Handle);
}
