using System.Media;
using System.Text;
namespace MonoClip.Windows.UI;

public enum Sound { Clip, Upload, Link, Error }

// Short synthesized cues instead of pop-up notifications. Generated once in memory, no files.
// Windows plays one sound per process at a time, so cues are queued instead of cutting each other off.
public sealed class SoundEffects : IDisposable
{
    const int Rate = 44100;
    readonly Dictionary<Sound, (SoundPlayer Player, MemoryStream Data, TimeSpan Length)> loaded = [];
    readonly Queue<Sound> queue = new(); readonly System.Windows.Forms.Timer next = new(); DateTime busyUntil; bool disposed;
    public SoundEffects() { next.Tick += (_, _) => { next.Stop(); Pump(); }; }

    // A note: start (s), frequency (Hz), optional glide target, length (s), decay time constant (s), level.
    record Note(double Start, double Frequency, double Length, double Decay, double Level, double GlideTo = 0);
    static Note[] Score(Sound sound) => sound switch
    {
        // Crisp "capture" pop: a quick upward flick that settles.
        Sound.Clip => [new(0, 1250, 0.11, 0.035, 1.0, GlideTo: 1760)],
        // Two rising soft notes: something is on its way.
        Sound.Upload => [new(0, 784, 0.12, 0.05, 0.8), new(0.075, 1175, 0.14, 0.06, 0.8)],
        // Bright major arpeggio with a longer tail: done, link ready.
        Sound.Link => [new(0, 1047, 0.32, 0.11, 0.75), new(0.07, 1319, 0.32, 0.11, 0.7), new(0.14, 1568, 0.42, 0.16, 0.75)],
        // Two descending, rounder low notes: something went wrong.
        _ => [new(0, 523, 0.16, 0.07, 0.9), new(0.12, 392, 0.22, 0.09, 0.9)],
    };

    public static byte[] CreateWave(Sound sound)
    {
        var notes = Score(sound); double total = notes.Max(n => n.Start + n.Length) + 0.01;
        var mix = new double[(int)(total * Rate)];
        foreach (var n in notes)
        {
            int start = (int)(n.Start * Rate), length = (int)(n.Length * Rate); double phase = 0;
            for (int i = 0; i < length && start + i < mix.Length; i++)
            {
                double t = (double)i / Rate, glide = n.GlideTo > 0 ? n.Frequency + (n.GlideTo - n.Frequency) * Math.Min(1, t / 0.02) : n.Frequency;
                phase += 2 * Math.PI * glide / Rate;
                // 3 ms attack, exponential decay, short release so every note ends in silence.
                double envelope = Math.Min(1, t / 0.003) * Math.Exp(-t / n.Decay) * Math.Min(1, (n.Length - t) / 0.01);
                // Soft bell timbre: fundamental plus a little second and third harmonic.
                mix[start + i] += n.Level * envelope * (Math.Sin(phase) + 0.22 * Math.Sin(2 * phase) + 0.06 * Math.Sin(3 * phase));
            }
        }
        double peak = mix.Max(Math.Abs), gain = peak > 0 ? 0.32 / peak : 0; // about -10 dBFS: present, never harsh
        using var data = new MemoryStream(); using var w = new BinaryWriter(data, Encoding.ASCII, true);
        w.Write("RIFF"u8); w.Write(36 + mix.Length * 2); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(mix.Length * 2);
        foreach (var v in mix) w.Write((short)Math.Round(v * gain * short.MaxValue));
        w.Flush(); return data.ToArray();
    }

    public void Play(Sound sound)
    {
        if (disposed || queue.Count >= 3) return; // never build up a backlog of stale cues
        queue.Enqueue(sound); Pump();
    }
    void Pump()
    {
        if (disposed || queue.Count == 0) return;
        var wait = busyUntil - DateTime.UtcNow;
        if (wait > TimeSpan.Zero) { next.Interval = Math.Max(1, (int)wait.TotalMilliseconds); next.Start(); return; }
        var sound = queue.Dequeue();
        try
        {
            if (!loaded.TryGetValue(sound, out var cue))
            {
                var bytes = CreateWave(sound); var stream = new MemoryStream(bytes, false);
                loaded[sound] = cue = (new SoundPlayer(stream), stream, TimeSpan.FromSeconds((bytes.Length - 44) / 2.0 / Rate));
            }
            cue.Player.Play(); busyUntil = DateTime.UtcNow + cue.Length + TimeSpan.FromMilliseconds(40);
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or TimeoutException or System.ComponentModel.Win32Exception) { /* No audio device: silence, never an error. */ }
        if (queue.Count > 0) Pump();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; next.Dispose(); queue.Clear();
        foreach (var (player, data, _) in loaded.Values) { player.Stop(); player.Dispose(); data.Dispose(); }
    }
}
