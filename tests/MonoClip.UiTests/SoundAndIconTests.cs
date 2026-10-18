using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using MonoClip.Windows.UI;
using static UiTest;

// The generated sound cues and tray symbols, checked sample by sample and pixel by pixel.
internal static class SoundAndIconTests
{
    public static void Run()
    {
        Test("Four distinct, short, clean sound cues", DistinctShortCleanCues);
        Test("Tray symbol is a black tile with a filled or hollow white dot", TileWithDot);
        Test("Tray symbol shows creating, online and error states", BusyOnlineAndErrorStates);
    }

    static void DistinctShortCleanCues()
    {
        var waves = new Dictionary<Sound, short[]>();
        foreach (var sound in Enum.GetValues<Sound>())
        {
            var bytes = SoundEffects.CreateWave(sound);
            using var reader = new BinaryReader(new MemoryStream(bytes));
            // RIFF/WAVE header: 16-bit PCM.
            Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
            Equal(bytes.Length - 8, reader.ReadInt32());
            Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
            reader.BaseStream.Position = 22;
            Equal((short)1, reader.ReadInt16());
            var rate = reader.ReadInt32();
            reader.BaseStream.Position = 34;
            Equal((short)16, reader.ReadInt16());
            reader.BaseStream.Position = 40;
            var size = reader.ReadInt32();

            double ms = size * 1000d / (rate * 2);
            if (ms is < 60 or > 600) throw new Exception($"{sound} is {ms:0} ms long");
            var samples = new short[size / 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = reader.ReadInt16();
            int peak = samples.Max(x => Math.Abs((int)x));
            if (peak < 5000) throw new Exception(sound + " is nearly silent");
            if (peak > 16000) throw new Exception(sound + " is too loud");
            if (samples.Take(3).Concat(samples.TakeLast(3)).Any(x => Math.Abs((int)x) > 200)) throw new Exception(sound + " edges click");
            waves[sound] = samples;
        }
        var distinct = waves.Values.Select(w => Convert.ToBase64String(SHA256.HashData(MemoryMarshal.AsBytes(w.AsSpan())))).Distinct().Count();
        if (distinct != waves.Count) throw new Exception("two cues are identical");
        if (waves.Count != 9) throw new Exception("expected 9 cues, found " + waves.Count);
        if (waves[Sound.Link].Length <= waves[Sound.Clip].Length) throw new Exception("link cue should ring out longer than the clip cue");
    }

    static void TileWithDot()
    {
        foreach (var size in new[] { 16, 20, 24, 32 })
        {
            using var on = TrayAppContext.MakeIcon(TrayIconKind.Recording, size);
            using var off = TrayAppContext.MakeIcon(TrayIconKind.Stopped, size);
            using var a = on.ToBitmap();
            using var b = off.ToBitmap();
            Color P(Bitmap bmp, int x, int y) => bmp.GetPixel(x, y);
            // Tile: opaque and dark near the edges (corners stay rounded), white dot in the middle.
            foreach (var (x, y) in new[] { (size / 2, 1), (1, size / 2), (size - 2, size / 2), (size / 2, size - 2) })
                foreach (var bmp in new[] { a, b })
                    if (P(bmp, x, y).A < 200 || P(bmp, x, y).R > 60) throw new Exception($"no black tile at {x},{y} size {size}");
            if (P(a, 0, 0).A > 200) throw new Exception("tile corners not rounded");
            var center = P(a, size / 2, size / 2);
            if (center.A < 200 || center.R < 200) throw new Exception("recording dot not filled white");
            if (P(b, size / 2, size / 2).R > 60) throw new Exception("stopped symbol is not a ring");
            bool ring = false;
            for (int x = 0; x < size / 2; x++) if (P(b, x, size / 2).R > 150) ring = true;
            if (!ring) throw new Exception("ring outline missing");
        }
    }

    static void BusyOnlineAndErrorStates()
    {
        foreach (var size in new[] { 16, 24, 32 })
        {
            Bitmap B(TrayIconKind kind, bool online = false, int frame = 0)
            {
                using var icon = TrayAppContext.MakeIcon(kind, size, online, frame);
                return icon.ToBitmap();
            }
            // Pixels whose red channel differs clearly between the two symbols.
            int Diff(Bitmap a, Bitmap b)
            {
                int n = 0;
                for (int x = 0; x < size; x++)
                    for (int y = 0; y < size; y++)
                        if (Math.Abs(a.GetPixel(x, y).R - b.GetPixel(x, y).R) > 60) n++;
                return n;
            }
            using var rec = B(TrayIconKind.Recording);
            using var online = B(TrayIconKind.Recording, true);
            using var f0 = B(TrayIconKind.Busy);
            using var f3 = B(TrayIconKind.Busy, frame: 3);
            using var error = B(TrayIconKind.Error);
            if (Diff(rec, online) < 3) throw new Exception("online badge not visible at " + size);
            bool badgeTopRight = false;
            for (int x = size * 2 / 3; x < size; x++)
                for (int y = 0; y < size / 3; y++)
                    if (online.GetPixel(x, y).R > 200) badgeTopRight = true;
            if (!badgeTopRight) throw new Exception("badge not in the top-right corner");
            if (Diff(f0, f3) < 3) throw new Exception("spinner frames do not move");
            if (error.GetPixel(size / 2, size * 2 / 5).R < 200 || error.GetPixel(size / 2, size / 2).R < 120) throw new Exception("error mark missing");
            if (Diff(error, rec) < size) throw new Exception("error symbol looks like the recording dot");
        }
    }
}
