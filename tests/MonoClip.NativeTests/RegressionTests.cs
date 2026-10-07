using System.Diagnostics;
using System.Reflection;
using MonoClip.Core;
using MonoClip.Windows.Capture;

internal static class RegressionTests
{
    static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Field<T>(ClipEngine engine, string name) => (T)typeof(ClipEngine).GetField(name, Fields)!.GetValue(engine)!;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(20); }
    }
    internal static int Run(string[] args)
    {
        var root = Path.Combine(Path.GetTempPath(), "MonoClip-regression-" + Guid.NewGuid());
        var settings = new AppSettings { Width = 1280, Height = 720, Fps = 30, ClipSeconds = 5, DesktopAudio = false, Microphone = false, ClipsDirectory = root };
        using var engine = new ClipEngine();
        if (args.Contains("--restart"))
        {
            engine.Start(settings);
            Pump(2500);
            var oldHost = Field<ObsHost>(engine, "host");
            Obs.obs_output_force_stop(Field<IntPtr>(engine, "output"));
            var clock = Stopwatch.StartNew();
            while (engine.IsRunning && clock.ElapsedMilliseconds < 10000) Thread.Sleep(10);
            Check(!engine.IsRunning, "forced stop did not finish");
            Console.WriteLine("REPRO forced native output inactive; restarting with previous host still owned");
            using var watchdog = new System.Threading.Timer(_ => { Console.Error.WriteLine("FAIL restart hangs with previous OBS host/sources still owned"); Environment.Exit(1); }, null, 20000, Timeout.Infinite);
            engine.Start(settings);
            Check(engine.IsRunning, "restart did not start real replay output");
            Check(!(bool)typeof(ObsHost).GetField("initialized", Fields)!.GetValue(oldHost)!, "previous OBS host was not shut down before restart");
            Pump(2500);
            Check(engine.TotalFrames > 30, "restarted output did not capture frames");
            engine.Stop();
            Console.WriteLine("PASS unexpected native output stop releases old host and restarts actual capture");
            return 0;
        }
        if (args.Contains("--timeout") || args.Contains("--stale"))
        {
            ClipRecord? record = null;
            engine.ClipSaved += (_, e) => record = e.Clip;
            engine.Start(settings);
            Pump(3000);
            var callback = Field<Obs.Signal>(engine, "savedCallback");
            Obs.signal_handler_disconnect(Obs.obs_output_get_signal_handler(Field<IntPtr>(engine, "output")), "saved", callback, IntPtr.Zero);
            engine.SaveClip();
            Pump(1000);
            var original = Field<object>(engine, "pending");
            Check(original != null, "test requires a native save with delayed notification");
            Check(Directory.EnumerateFiles(Path.Combine(root, ".pending"), "*.mkv").Any(), "native replay was not muxed");
            if (args.Contains("--timeout"))
            {
                original!.GetType().GetProperty("At")!.SetValue(original, DateTime.UtcNow.AddSeconds(-21));
                typeof(ClipEngine).GetMethod("Tick", Fields)!.Invoke(engine, null);
                Check(ReferenceEquals(original, Field<object>(engine, "pending")), "timeout discarded native request before its saved signal");
                bool rejected = false;
                try { engine.SaveClip(); } catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "timeout allowed overlapping native saves");
                callback(IntPtr.Zero, IntPtr.Zero);
                Pump(1000);
                Check(record != null && File.Exists(record.Path), "late saved signal did not export original clip");
                Console.WriteLine("PASS timeout retains request and late native saved notification exports original clip " + record!.Path);
            }
            else
            {
                callback(IntPtr.Zero, IntPtr.Zero);
                Pump(1000);
                Check(record != null, "first request was not exported");
                engine.Stop();
                engine.Start(settings);
                Pump(3000);
                var currentCallback = Field<Obs.Signal>(engine, "savedCallback");
                Obs.signal_handler_disconnect(Obs.obs_output_get_signal_handler(Field<IntPtr>(engine, "output")), "saved", currentCallback, IntPtr.Zero);
                record = null;
                engine.SaveClip();
                Pump(1000);
                var next = Field<object>(engine, "pending");
                callback(IntPtr.Zero, IntPtr.Zero);
                Check(ReferenceEquals(next, Field<object>(engine, "pending")), "old output saved signal consumed restarted output's request");
                currentCallback(IntPtr.Zero, IntPtr.Zero);
                Pump(1000);
                Check(record != null && File.Exists(record.Path), "current output request was not exported");
                Console.WriteLine("PASS old output saved notification cannot consume restarted output's request " + record!.Path);
            }
            engine.Stop();
            return 0;
        }
        if (args.Contains("--stop-save") || args.Contains("--dispose-save") || args.Contains("--export-drain"))
        {
            engine.Start(settings);
            Pump(6500);
            using var watchdog = new System.Threading.Timer(_ => { Console.Error.WriteLine("FAIL shutdown waited on dispatcher or stalled native request"); Environment.Exit(1); }, null, 30000, Timeout.Infinite);
            if (args.Contains("--export-drain"))
            {
                var callback = Field<Obs.Signal>(engine, "savedCallback");
                Obs.signal_handler_disconnect(Obs.obs_output_get_signal_handler(Field<IntPtr>(engine, "output")), "saved", callback, IntPtr.Zero);
                engine.SaveClip();
                Pump(1000);
                ThreadPool.GetMaxThreads(out var workers, out var io);
                ThreadPool.GetMinThreads(out var minWorkers, out var minIo);
                using var occupied = new ManualResetEventSlim();
                using var unblock = new ManualResetEventSlim();
                ThreadPool.SetMinThreads(1, minIo);
                Check(ThreadPool.SetMaxThreads(1, io), "could not isolate export worker");
                ThreadPool.QueueUserWorkItem(_ => { occupied.Set(); unblock.Wait(); });
                Check(occupied.Wait(5000), "export blocker did not start");
                try
                {
                    callback(IntPtr.Zero, IntPtr.Zero);
                    var releaser = new Thread(() => { Thread.Sleep(750); unblock.Set(); });
                    releaser.Start();
                    var clock = Stopwatch.StartNew();
                    engine.Stop();
                    Check(clock.ElapsedMilliseconds >= 500, "Stop returned while queued export was still blocked");
                    releaser.Join();
                }
                finally { unblock.Set(); ThreadPool.SetMaxThreads(workers, io); ThreadPool.SetMinThreads(minWorkers, minIo); }
            }
            else
            {
                engine.SaveClip();
                // Intentionally do not pump WinForms messages after SaveClip.
                if (args.Contains("--dispose-save")) engine.Dispose(); else engine.Stop();
            }
            var clips=Directory.EnumerateFiles(Path.Combine(root,"Desktop"),"*.mkv").ToArray();
            Check(clips.Length==1,"shutdown lost pending video export");
            Check(!Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories).Any(),"export created forbidden metadata sidecar");
            var video=clips[0];
            Check(File.Exists(video) && new FileInfo(video).Length > 1000, "shutdown clip data missing");
            using var stream = File.OpenRead(video);
            var magic = new byte[4]; stream.ReadExactly(magic);
            Check(magic.SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }), "shutdown output is not a Matroska clip");
            Console.WriteLine("PASS " + args.Single(a => a.StartsWith("--")) + " persists native video without JSON or pumping dispatcher " + video);
            return 0;
        }
        if (args.Contains("--target"))
        {
            var update = typeof(ClipEngine).GetMethod("UpdateCaptureTarget", Fields);
            Check(update != null, "capture target notification seam missing");
            var seen = new List<(string Target, ClipContext Context)>();
            engine.StatusChanged += (_, _) => seen.Add((engine.CaptureTarget, Field<ClipContext>(engine, "context")));
            var first = new ClipContext("First", "first.exe", "First window", "game");
            update!.Invoke(engine, new object[] { first, "Spiel · First" });
            Check(seen.Count == 1 && seen[0] == ("Spiel · First", first), "target notification missing or emitted before target/context update");
            var second = new ClipContext("Second", "second.exe", "Second window", "game");
            update.Invoke(engine, new object[] { second, "Spiel · Second" });
            Check(seen.Count == 2 && seen[1] == ("Spiel · Second", second), "game change without hook transition did not notify current target");
            var desktop = new ClipContext("Desktop", "", "desktop window", "desktop-dxgi");
            update.Invoke(engine, new object[] { desktop, "Desktop · Monitor 1" });
            update.Invoke(engine, new object[] { desktop, "Desktop · Monitor 2" });
            Check(seen.Count == 4 && seen[3] == ("Desktop · Monitor 2", desktop), "monitor change did not notify current target");
            update.Invoke(engine, new object[] { desktop with { WindowTitle = "new foreground title" }, "Desktop · Monitor 2" });
            Check(seen.Count == 4, "unchanged target emitted duplicate notification");
            Check(Field<ClipContext>(engine, "context").WindowTitle == "new foreground title", "unchanged target did not update context");
            Console.WriteLine("PASS capture target observers see updated game/monitor/context exactly once per target change without a game fixture");
            return 0;
        }
        if (args.Contains("--rapid-save"))
        {
            int saved = 0;
            engine.ClipSaved += (_, _) => saved++;
            engine.Start(settings); Pump(6500);
            for (int i = 0; i < 4; i++)
            {
                engine.SaveClip();
                var clock = Stopwatch.StartNew();
                while (saved <= i && clock.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(5); }
                Console.WriteLine("REPRO rapid request=" + (i + 1) + " exported=" + saved + " status=" + engine.Status + " pending=" + (Field<object>(engine, "pending") != null) + " root=" + root);
                Check(saved == i + 1, "sequential native save reused staging path and was mistaken for an old signal");
            }
            engine.Stop();
            Check(Directory.EnumerateFiles(Path.Combine(root,"Desktop"),"*.mkv").Count()==4,"rapid sequential saves were lost");
            Check(!Directory.EnumerateFiles(root,"*.json",SearchOption.AllDirectories).Any(),"rapid saves produced JSON sidecars");
            Console.WriteLine("PASS rapid sequential native saves preserve four distinct clips " + root);
            return 0;
        }
        if (args.Contains("--export-error"))
        {
            engine.Start(settings); Pump(6500);
            File.WriteAllText(Path.Combine(root, "Desktop"), "blocking export folder fixture");
            engine.SaveClip(); engine.Stop();
            var staged = Directory.EnumerateFiles(Path.Combine(root, ".pending"), "*.mkv").Single();
            Check(new FileInfo(staged).Length > 1000, "failed export discarded real native recording");
            Console.WriteLine("REPRO export failure status=" + engine.Status + " actual staged=" + staged);
            Check(engine.Status.Replace('/', Path.DirectorySeparatorChar).Contains(staged), "export failure did not report actual preserved recording path");
            Check(!Directory.EnumerateFiles(root, "*.mkv.json", SearchOption.AllDirectories).Any(), "failed export reported committed metadata");
            Console.WriteLine("PASS immediate shutdown preserves real recording after export failure " + staged);
            return 0;
        }
        throw new ArgumentException("Unknown regression mode");
    }
}
