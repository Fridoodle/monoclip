using System.Runtime.InteropServices;
using static MonoClip.Windows.Share.Mf;
namespace MonoClip.Windows.UI;

// Small video player for the share editor: Windows' MFPlay renders into a window with sound and plays only
// the chosen audio track. Plain vtable calls (no built-in COM in the trimmed app), no event callback: the
// editor polls the position. Slots count IUnknown's three methods.
internal sealed unsafe class ClipPlayer : IDisposable
{
    [DllImport("mfplay.dll", CharSet = CharSet.Unicode)] static extern int MFPCreateMediaPlayer(string? url, int startPlayback, uint options, IntPtr callback, IntPtr window, out IntPtr player);
    // IMFPMediaPlayer
    const int Play_ = 3, Pause_ = 4, SetPosition_ = 7, GetPosition_ = 8, GetState_ = 13, CreateItem = 14, SetItem = 16, SetMute_ = 24, UpdateVideo_ = 32, Shutdown_ = 38;
    // IMFPMediaItem
    const int SetStreamSelection_ = 16;
    const int Playing = 2;
    IntPtr player, item; readonly IReadOnlyList<uint> audioStreams;

    public ClipPlayer(string path, IntPtr window, IReadOnlyList<uint> audioStreams, uint? audio)
    {
        this.audioStreams = audioStreams;
        Check(MFPCreateMediaPlayer(null, 0, 0, 0, window, out player), "Video player");
        try
        {
            IntPtr created;
            fixed (char* url = path) Check(((delegate* unmanaged[Stdcall]<IntPtr, char*, int, nuint, IntPtr*, int>)Vtbl(player)[CreateItem])(player, url, 1, 0, &created), "Opening the clip");
            item = created;
            SelectAudio(audio);
        }
        catch { Dispose(); throw; }
    }
    // Plays only this audio stream (null: none). Takes effect by setting the item again; the position is kept.
    public void SelectAudio(uint? stream)
    {
        var position = Position;
        foreach (var s in audioStreams) Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, int, int>)Vtbl(item)[SetStreamSelection_])(item, s, s == stream ? 1 : 0), "audio selection");
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vtbl(player)[SetItem])(player, item), "Loading the clip");
        Position = position;
    }
    public void Play() => Call(Play_, "Play");
    // A freshly loaded clip shows nothing until it has played: the editor starts it muted and pauses at once.
    public bool Muted { set => ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Vtbl(player)[SetMute_])(player, value ? 1 : 0); }
    public void Pause() => Call(Pause_, "Pause");
    public bool IsPlaying { get { int state; return ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Vtbl(player)[GetState_])(player, &state) >= 0 && state == Playing; } }
    public TimeSpan Position
    {
        get { var key = Guid.Empty; PropVariant value; return ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, PropVariant*, int>)Vtbl(player)[GetPosition_])(player, &key, &value) >= 0 && value.Type == VtI8 ? TimeSpan.FromTicks(value.Long) : TimeSpan.Zero; }
        set { var key = Guid.Empty; var time = Time(value); ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, PropVariant*, int>)Vtbl(player)[SetPosition_])(player, &key, &time); }
    }
    // Repaints the current frame after the window was resized or uncovered.
    public void UpdateVideo() { if (player != 0) ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(player)[UpdateVideo_])(player); }
    void Call(int slot, string step) => Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(player)[slot])(player), step);
    public void Dispose()
    {
        if (player != 0) ((delegate* unmanaged[Stdcall]<IntPtr, int>)Vtbl(player)[Shutdown_])(player);
        Release(ref item); Release(ref player);
    }
}
