using MonoClip.Core;
using MonoClip.Windows.Capture;
namespace MonoClip.Windows.Share;

internal static class ClipRemux
{
    // Browsers and Discord do not play MKV. Lossless remux with the bundled libobs, then fast-start.
    public static void ToFastStartMp4(string clip, string target)
    {
        var raw = target + ".remux.mp4";
        try
        {
            if (!Obs.media_remux_job_create(out var job, clip, raw)) throw new InvalidOperationException("Could not remux the clip to MP4.");
            try { if (!Obs.media_remux_job_process(job, IntPtr.Zero, IntPtr.Zero)) throw new InvalidOperationException("MP4 remux failed."); }
            finally { Obs.media_remux_job_destroy(job); }
            try { Mp4FastStart.Apply(raw, target); }
            catch (NotSupportedException) { File.Move(raw, target, true); } // Still playable, just starts a little later.
        }
        finally { if (File.Exists(raw)) File.Delete(raw); }
    }
}
