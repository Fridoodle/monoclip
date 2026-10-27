using MonoClip.Core;
using System.Diagnostics;
namespace MonoClip.Windows.Capture;

// The replay buffer: libobs records the CaptureScene with GPU H.264 and AAC into RAM, and a save writes the last
// ClipSeconds to the clip folder (see ClipLibrary).
public sealed class ClipEngine : IClipEngine
{
    const string SlowSave = "Saving the clip is taking too long. See capture.log.";
    readonly Control dispatcher = new(); readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    Obs.Signal? savedCallback; ObsHost? host; CaptureScene? capture;
    readonly List<Task> exports = []; string? saveError;
    readonly object saveGate = new(); long outputGeneration;
    IntPtr output, videoEncoder; readonly List<IntPtr> audioEncoders = []; readonly List<IntPtr> audioSources = []; IntPtr desktopAudio, microphone;
    AppSettings settings = new(); bool disposed;
    // Monitors whose DXGI capture gave no frames: they start with WGC from then on, also after a restart.
    readonly HashSet<string> wgcMonitors = [];
    record Pending(long Generation, string Request, ClipContext Context, AppSettings Settings, string Encoder, DateTime At);
    Pending? pending; ClipContext context = new("Desktop", "", "", "desktop"); DateTime started;
    public bool IsRunning => output != IntPtr.Zero && Obs.obs_output_active(output);
    public string Status { get; private set; } = "Buffer stopped";
    public string EncoderName { get; private set; } = "Hardware-H.264";
    public string CaptureTarget { get; private set; } = "Automatic: active window";
    public int TotalFrames => output == IntPtr.Zero ? 0 : Obs.obs_output_get_total_frames(output);
    public int DroppedFrames => output == IntPtr.Zero ? 0 : Obs.obs_output_get_frames_dropped(output);
    public event EventHandler? StatusChanged; public event EventHandler<string>? Failed; public event EventHandler<ClipSavedEventArgs>? ClipSaved;
    public ClipEngine() { _ = dispatcher.Handle; timer.Tick += (_, _) => Tick(); }
    void SetStatus(string text) { Status = text; StatusChanged?.Invoke(this, EventArgs.Empty); }
    void SetFailure(string text) { if (text == Status) return; SetStatus(text); Failed?.Invoke(this, text); }
    public void Start(AppSettings s)
    {
        ObjectDisposedException.ThrowIf(disposed, this); if (IsRunning) return; s.Validate(); Release(); settings = s with { };
        try
        {
            Directory.CreateDirectory(settings.ClipsDirectory);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(settings.ClipsDirectory))!);
            if (drive.IsReady && drive.AvailableFreeSpace < 512L * 1024 * 1024) throw new IOException("Less than 512 MB of free disk space. Buffer not started.");
            host = new(); host.Initialize(s.Width, s.Height, s.Fps);
            capture = new(s.Width, s.Height, wgcMonitors) { StretchToFill = s.StretchToFill }; capture.MonitorFallback += () => SetStatus("Buffer active · WGC fallback");
            Obs.obs_set_output_source(0, capture.Source);
            // Audio tracks as CapturePolicy lays them out: each source feeds the mix and, when recorded, its own track.
            if (s.DesktopAudio) desktopAudio = AddAudio("wasapi_output_capture", "Desktop audio", s.DesktopDevice, CapturePolicy.AudioMixers(s, ClipAudio.Desktop), 1);
            if (s.Microphone) microphone = AddAudio("wasapi_input_capture", "Microphone", s.MicrophoneDevice, CapturePolicy.AudioMixers(s, ClipAudio.Microphone), 2);
            SetVolumes(s);
            using (var data = new ObsData().Set("rate_control", "CBR").Set("bitrate", CapturePolicy.BitrateKbps(s)).Set("keyint_sec", 1L).Set("preset", "speed").Set("profile", "high").Set("bf", 0L).Set("bframes", 0L).Set("target_usage", "TU5"))
            {
                // Intel (QSV) reads "bframes" and "target_usage" (TU5: fast), AMD and NVIDIA "bf" and "preset".
                var (id, name) = CapturePolicy.PickEncoder(host.Encoders) ?? throw new NotSupportedException("No supported GPU H.264 encoder found (AMD, NVIDIA or Intel; no CPU fallback).");
                EncoderName = name;
                videoEncoder = Obs.Require(Obs.obs_video_encoder_create(id, "MonoClip GPU video", data.Handle, IntPtr.Zero), "GPU encoder"); Obs.obs_encoder_set_video(videoEncoder, Obs.obs_get_video());
            }
            var staging = Path.Combine(s.ClipsDirectory, ".pending"); Directory.CreateDirectory(staging);
            using (var data = new ObsData().Set("directory", staging.Replace('\\', '/')).Set("format", "Replay_%CCYY-%MM-%DD_%hh-%mm-%ss").Set("extension", "mkv").Set("allow_spaces", false).Set("max_time_sec", s.ClipSeconds + 1).Set("max_size_mb", CapturePolicy.BufferMegabytes(s))) output = Obs.Require(Obs.obs_output_create("replay_buffer", "MonoClip replay", data.Handle, IntPtr.Zero), "Replay buffer");
            Obs.obs_output_set_video_encoder(output, videoEncoder);
            var tracks = CapturePolicy.AudioTrackNames(s); for (int track = 0; track < tracks.Count; track++) AddAudioEncoder((nuint)track, tracks[track]);
            var captureOutput = output; var generation = ++outputGeneration;
            savedCallback = (parameter, data) => OnSaved(captureOutput, generation);
            Obs.signal_handler_connect(Obs.obs_output_get_signal_handler(output), "saved", savedCallback, IntPtr.Zero);
            if (!Obs.obs_output_start(output)) throw new InvalidOperationException("GPU buffer failed to start. " + Obs.Str(Obs.obs_output_get_last_error(output)));
            started = DateTime.UtcNow; timer.Start(); var desktop = capture.DesktopTarget; UpdateCaptureTarget(desktop.Context, desktop.Target); SetStatus("Buffer active");
        }
        catch (Exception e) { Release(); SetFailure("Error: " + e.Message); throw; }
    }
    IntPtr AddAudio(string id, string name, string device, uint mixers, uint channel)
    {
        using var data = new ObsData().Set("device_id", device).Set("use_device_timing", false);
        var source = Obs.Require(Obs.obs_source_create(id, name, data.Handle, IntPtr.Zero), name); audioSources.Add(source); Obs.obs_source_set_audio_mixers(source, mixers); Obs.obs_set_output_source(channel, source);
        return source;
    }
    void SetVolumes(AppSettings s)
    {
        if (desktopAudio != IntPtr.Zero) Obs.obs_source_set_volume(desktopAudio, s.DesktopVolume / 100f);
        if (microphone != IntPtr.Zero) Obs.obs_source_set_volume(microphone, s.MicrophoneVolume / 100f);
    }
    void AddAudioEncoder(nuint mixer, string name)
    {
        using var data = new ObsData().Set("bitrate", (long)CapturePolicy.AudioKbpsPerTrack); var enc = Obs.Require(Obs.obs_audio_encoder_create("ffmpeg_aac", name, data.Handle, mixer, IntPtr.Zero), "AAC encoder"); audioEncoders.Add(enc); Obs.obs_encoder_set_audio(enc, Obs.obs_get_audio()); Obs.obs_output_set_audio_encoder(output, enc, (nuint)(audioEncoders.Count - 1));
    }
    // What the next clip is about; observers hear about a new target once.
    void UpdateCaptureTarget(ClipContext nextContext, string target)
    {
        bool changed = CaptureTarget != target;
        context = nextContext; CaptureTarget = target;
        if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
    }
    void Tick()
    {
        try
        {
            if (!IsRunning) { timer.Stop(); SetFailure("Capture stopped unexpectedly. " + (output == IntPtr.Zero ? "" : Obs.Str(Obs.obs_output_get_last_error(output)))); return; }
            if (capture!.Follow() is { } shown) UpdateCaptureTarget(shown.Context, shown.Target);
            bool slowSave;
            lock (saveGate) slowSave = pending is not null && DateTime.UtcNow - pending.At > TimeSpan.FromSeconds(20);
            if (slowSave) SetFailure(SlowSave);
        }
        catch (Exception e) { SetFailure("Error: " + e.Message); }
    }
    public void SaveClip()
    {
        if (!IsRunning) throw new InvalidOperationException("Buffer is stopped.");
        if (DateTime.UtcNow - started < TimeSpan.FromSeconds(2)) throw new InvalidOperationException("Buffer is still starting.");
        Tick();
        lock (saveGate)
        {
            if (pending != null) throw new InvalidOperationException("A clip is already being saved.");
            var save = new Pending(outputGeneration, Guid.NewGuid().ToString("N"), context, settings with { }, EncoderName, DateTime.UtcNow);
            // Bind the native result path to this immutable request. libobs' saved
            // signal has no ID; timestamps alone can reuse a moved staging filename.
            using (var data = new ObsData().Set("format", "Replay_%CCYY-%MM-%DD_%hh-%mm-%ss_" + save.Request)) Obs.obs_output_update(output, data.Handle);
            pending = save; saveError = null;
            var cd = new Obs.CallData();
            try { if (!Obs.proc_handler_call(Obs.obs_output_get_proc_handler(output), "save", ref cd)) throw new InvalidOperationException("Replay saving is not available."); }
            catch { if (ReferenceEquals(pending, save)) pending = null; throw; }
            finally { Obs.bfree(cd.Stack); }
        }
        SetStatus("Saving clip…");
    }
    static bool MatchesRequest(string path, Pending save) => !string.IsNullOrWhiteSpace(path) && Path.GetFileNameWithoutExtension(path).EndsWith("_" + save.Request, StringComparison.Ordinal);
    static string LastReplay(IntPtr captureOutput)
    {
        var cd = new Obs.CallData();
        try { Obs.proc_handler_call(Obs.obs_output_get_proc_handler(captureOutput), "get_last_replay", ref cd); Obs.calldata_get_string(ref cd, "path", out var p); return Obs.Str(p); }
        finally { Obs.bfree(cd.Stack); }
    }
    void OnSaved(IntPtr captureOutput, long generation)
    {
        try
        {
            lock (saveGate)
            {
                // A saved signal has no request ID. Keep exactly one native save in flight,
                // and reject retired output callbacks before touching their native handle.
                if (generation != outputGeneration || captureOutput != output || pending is not { } save || save.Generation != generation) return;
                var path = LastReplay(captureOutput);
                if (!MatchesRequest(path, save)) return;
                // Register the export before releasing the in-flight request. Completion
                // includes disk/index work, not execution of the posted UI notification.
                exports.RemoveAll(t => t.IsCompleted);
                exports.Add(Task.Run(() =>
                {
                    try
                    {
                        var record = ClipLibrary.Store(path, save.Settings.ClipsDirectory, save.Context, save.Settings, save.Encoder);
                        Post(() => { if (generation == outputGeneration) SetStatus(IsRunning ? "Buffer active · clip saved" : "Buffer stopped · clip saved"); ClipSaved?.Invoke(this, new(record)); });
                    }
                    catch (Exception e)
                    {
                        var error = "Clip kept at " + path + " · " + e.Message;
                        lock (saveGate) saveError = error;
                        Post(() => { if (generation == outputGeneration) SetFailure(error); });
                    }
                }));
                pending = null;
            }
        }
        catch (Exception e) { Post(() => SetFailure("Clip error: " + e.Message)); }
    }
    void Post(Action action) { try { if (!dispatcher.IsDisposed && dispatcher.IsHandleCreated) dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { } }
    // Settings the buffer is built from restart it while it runs; the rest apply to the next clip as they are.
    public void Apply(AppSettings s)
    {
        s.Validate();
        if (!IsRunning || !CapturePolicy.NeedsRestart(settings, s)) { settings = s with { }; if (capture != null) capture.StretchToFill = s.StretchToFill; SetVolumes(s); return; }
        var old = settings; Stop();
        try { Start(s); } catch { try { Start(old); } catch { } throw; }
    }
    // A clip that could not be stored stays the status (and a failure) until the next save.
    public void Stop() { Release(); if (saveError != null) SetFailure(saveError); else SetStatus("Buffer stopped"); }
    void Release()
    {
        timer.Stop();
        if (output != IntPtr.Zero)
        {
            // save() only arms the next encoded packet. Wait until this request has
            // finished muxing (get_last_replay returns its unique path), or capture failed.
            // Mux failures emit no saved signal, but still return the completed path.
            var clock = Stopwatch.StartNew();
            while (Obs.obs_output_active(output))
            {
                lock (saveGate) { if (pending == null || MatchesRequest(LastReplay(output), pending)) break; }
                if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Native clip save has not finished. Buffer and request are kept.");
                Thread.Sleep(10);
            }
            if (Obs.obs_output_active(output)) Obs.obs_output_force_stop(output);
            // libobs waits for capture shutdown and joins its mux thread on release.
            // Keep saved connected and the output handle valid until that join finishes;
            // neither native callbacks nor exports wait on the WinForms dispatcher.
            Obs.obs_output_release(output);
            lock (saveGate)
            {
                if (pending is { } save) saveError = "Clip save did not finish. Existing files remain in " + Path.Combine(save.Settings.ClipsDirectory, ".pending");
                ++outputGeneration; pending = null; output = IntPtr.Zero;
            }
            savedCallback = null;
        }
        Task[] work;
        lock (saveGate) work = exports.ToArray();
        Task.WaitAll(work);
        lock (saveGate) exports.Clear();
        foreach (var e in audioEncoders) Obs.obs_encoder_release(e); audioEncoders.Clear(); if (videoEncoder != IntPtr.Zero) { Obs.obs_encoder_release(videoEncoder); videoEncoder = IntPtr.Zero; }
        if (host != null) { for (uint i = 0; i < 3; i++) Obs.obs_set_output_source(i, IntPtr.Zero); }
        capture?.Dispose(); capture = null;
        foreach (var a in audioSources) Obs.obs_source_release(a); audioSources.Clear(); desktopAudio = microphone = IntPtr.Zero;
        host?.Dispose(); host = null;
    }
    public void Dispose() { if (disposed) return; Release(); disposed = true; timer.Dispose(); dispatcher.Dispose(); }
}
