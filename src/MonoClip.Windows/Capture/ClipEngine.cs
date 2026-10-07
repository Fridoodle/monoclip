using MonoClip.Core;
using System.Diagnostics;
namespace MonoClip.Windows.Capture;

public sealed class ClipEngine : IClipEngine
{
    readonly Control dispatcher = new(); readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    Obs.Signal? savedCallback; ObsHost? host;
    readonly List<Task> exports = []; string? saveError;
    readonly object saveGate = new(); long outputGeneration;
    IntPtr scene, desktop, game, desktopItem, gameItem, output, videoEncoder; readonly List<IntPtr> audioEncoders = []; readonly List<IntPtr> audioSources = [];
    AppSettings settings = new(); string monitorId = "", monitorName = ""; bool hooked; bool disposed; bool usingWgc; DateTime monitorChanged; readonly HashSet<string> wgcMonitors = [];
    record Pending(long Generation, string Request, ClipContext Context, AppSettings Settings, string Encoder, DateTime At);
    Pending? pending; ClipContext context = new("Desktop", "", "", "desktop"); DateTime started;
    public bool IsRunning => output != IntPtr.Zero && Obs.obs_output_active(output);
    public string Status { get; private set; } = "Puffer gestoppt";
    public string EncoderName { get; private set; } = "Hardware-H.264";
    public string CaptureTarget { get; private set; } = "Automatisch: Spiel oder aktiver Bildschirm";
    public int TotalFrames => output == IntPtr.Zero ? 0 : Obs.obs_output_get_total_frames(output);
    public int DroppedFrames => output == IntPtr.Zero ? 0 : Obs.obs_output_get_frames_dropped(output);
    public event EventHandler? StatusChanged; public event EventHandler<ClipSavedEventArgs>? ClipSaved;
    public ClipEngine() { _ = dispatcher.Handle; timer.Tick += (_, _) => Tick(); }
    void SetStatus(string text) { Status = text; StatusChanged?.Invoke(this, EventArgs.Empty); }
    static IntPtr Require(IntPtr ptr, string name) => ptr == IntPtr.Zero ? throw new InvalidOperationException(name + " konnte nicht erstellt werden.") : ptr;
    public IReadOnlyList<AudioDevice> GetAudioDevices(bool input) => [new("default", input ? "Windows-Standardmikrofon" : "Windows-Standardausgabe")];
    public void Start(AppSettings s)
    {
        ObjectDisposedException.ThrowIf(disposed, this); if (IsRunning) return; s.Validate(); Release(); settings = s with { };
        try
        {
            Directory.CreateDirectory(settings.ClipsDirectory);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(settings.ClipsDirectory))!);
            if (drive.IsReady && drive.AvailableFreeSpace < 512L * 1024 * 1024) throw new IOException("Weniger als 512 MB Speicherplatz frei. Puffer wurde nicht gestartet.");
            host = new(); host.Initialize(s.Width, s.Height, s.Fps);
            scene = Require(Obs.obs_scene_create("MonoClip capture"), "Szene");
            using (var data = new ObsData().Set("monitor_id", "DUMMY").Set("method", 1L).Set("capture_cursor", true).Set("force_sdr", true)) desktop = Require(Obs.obs_source_create("monitor_capture", "Desktop", data.Handle, IntPtr.Zero), "Desktopaufnahme");
            desktopItem = Obs.obs_scene_add(scene, desktop); Fit(desktopItem); SelectMonitor(true);
            using (var data = new ObsData().Set("capture_mode", "any_fullscreen").Set("capture_cursor", true).Set("limit_framerate", true).Set("anti_cheat_hook", true).Set("capture_overlays", false).Set("capture_audio", false)) game = Require(Obs.obs_source_create("game_capture", "Automatic game", data.Handle, IntPtr.Zero), "Spielaufnahme");
            gameItem = Obs.obs_scene_add(scene, game); Fit(gameItem);
            Obs.obs_set_output_source(0, Obs.obs_scene_get_source(scene));
            // Track 1 is a playback mix; track 2 desktop-only; track 3 microphone-only.
            if (s.DesktopAudio) AddAudio("wasapi_output_capture", "Desktop audio", s.DesktopDevice, 1u | 2u, 1);
            if (s.Microphone) AddAudio("wasapi_input_capture", "Microphone", s.MicrophoneDevice, 1u | 4u, 2);
            using (var data = new ObsData().Set("rate_control", "CBR").Set("bitrate", CapturePolicy.BitrateKbps(s.Width, s.Height, s.Fps)).Set("keyint_sec", 1L).Set("preset", "speed").Set("profile", "high").Set("bf", 0L))
            {
                string? id = host.Encoders.Contains("h264_texture_amf") ? "h264_texture_amf" : host.Encoders.FirstOrDefault(x => x == "obs_nvenc_h264_tex");
                if (id == null) throw new NotSupportedException("Kein unterstützter GPU-H.264-Encoder. Kein CPU-Fallback, um Spielleistung zu schützen.");
                EncoderName = id == "h264_texture_amf" ? "AMD AMF · H.264 GPU" : "NVIDIA NVENC · H.264 GPU";
                videoEncoder = Require(Obs.obs_video_encoder_create(id, "MonoClip GPU video", data.Handle, IntPtr.Zero), "GPU-Encoder"); Obs.obs_encoder_set_video(videoEncoder, Obs.obs_get_video());
            }
            var staging = Path.Combine(s.ClipsDirectory, ".pending"); Directory.CreateDirectory(staging);
            using (var data = new ObsData().Set("directory", staging.Replace('\\', '/')).Set("format", "Replay_%CCYY-%MM-%DD_%hh-%mm-%ss").Set("extension", "mkv").Set("allow_spaces", false).Set("max_time_sec", s.ClipSeconds + 1).Set("max_size_mb", CapturePolicy.BufferMegabytes(s))) output = Require(Obs.obs_output_create("replay_buffer", "MonoClip replay", data.Handle, IntPtr.Zero), "Replay-Puffer");
            Obs.obs_output_set_video_encoder(output, videoEncoder);
            AddAudioEncoder(0, "Playback mix"); if (s.DesktopAudio) AddAudioEncoder(1, "Desktop"); if (s.Microphone) AddAudioEncoder(2, "Microphone");
            var captureOutput = output; var generation = ++outputGeneration;
            savedCallback = (parameter, data) => OnSaved(captureOutput, generation);

            Obs.signal_handler_connect(Obs.obs_output_get_signal_handler(output), "saved", savedCallback, IntPtr.Zero);

            if (!Obs.obs_output_start(output)) throw new InvalidOperationException("GPU-Puffer konnte nicht starten. " + Obs.Str(Obs.obs_output_get_last_error(output)));
            started = DateTime.UtcNow; timer.Start(); UpdateCaptureTarget(new("Desktop", "", ForegroundTracker.Title(), usingWgc ? "desktop-wgc" : "desktop-dxgi"), "Desktop · " + monitorName); SetStatus("Puffer aktiv · Aufnahme läuft");
        }
        catch (Exception e) { Release(); SetStatus("Fehler: " + e.Message); throw; }
    }
    void Fit(IntPtr item) { Obs.obs_sceneitem_set_bounds_type(item, 2); Obs.obs_sceneitem_set_bounds_alignment(item, 0); var bounds = new Obs.Vec2(settings.Width, settings.Height); Obs.obs_sceneitem_set_bounds(item, ref bounds); }
    void AddAudio(string id, string name, string device, uint mixers, uint channel)
    {
        using var data = new ObsData().Set("device_id", device).Set("use_device_timing", false);
        var source = Require(Obs.obs_source_create(id, name, data.Handle, IntPtr.Zero), name); audioSources.Add(source); Obs.obs_source_set_audio_mixers(source, mixers); Obs.obs_set_output_source(channel, source);
    }
    void AddAudioEncoder(nuint mixer, string name)
    {
        using var data = new ObsData().Set("bitrate", 160L); var enc = Require(Obs.obs_audio_encoder_create("ffmpeg_aac", name, data.Handle, mixer, IntPtr.Zero), "AAC-Encoder"); audioEncoders.Add(enc); Obs.obs_encoder_set_audio(enc, Obs.obs_get_audio()); Obs.obs_output_set_audio_encoder(output, enc, (nuint)(audioEncoders.Count - 1));
    }
    void SelectMonitor(bool force = false)
    {
        var selected = ForegroundTracker.Monitor(); if (!force && selected.Id == monitorId) return;
        string id = selected.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            var props = Obs.obs_source_properties(desktop); try { var prop = Obs.obs_properties_get(props, "monitor_id"); for (nuint i = 0; i < Obs.obs_property_list_item_count(prop); i++) { var value = Obs.Str(Obs.obs_property_list_item_string(prop, i)); if (value != "DUMMY") { id = value; break; } } } finally { Obs.obs_properties_destroy(props); }
        }
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Kein Bildschirm für GPU-Aufnahme gefunden.");
        usingWgc = wgcMonitors.Contains(id); monitorChanged = DateTime.UtcNow;
        using var data = new ObsData().Set("monitor_id", id).Set("method", usingWgc ? 2L : 1L).Set("capture_cursor", true).Set("force_sdr", true); Obs.obs_source_update(desktop, data.Handle); monitorId = id;
        monitorName = selected.Name;
    }
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
            if (!IsRunning) { timer.Stop(); SetStatus("Aufnahme wurde unerwartet beendet. " + (output == IntPtr.Zero ? "" : Obs.Str(Obs.obs_output_get_last_error(output)))); return; }
            var cd = new Obs.CallData(); bool nowHooked = false; string exe = "", title = "";
            try { Obs.proc_handler_call(Obs.obs_source_get_proc_handler(game), "get_hooked", ref cd); Obs.calldata_get_data(ref cd, "hooked", out var value, 1); nowHooked = value != 0; if (nowHooked) { Obs.calldata_get_string(ref cd, "executable", out var p); exe = Obs.Str(p); Obs.calldata_get_string(ref cd, "title", out p); title = Obs.Str(p); } } finally { Obs.bfree(cd.Stack); }
            if (hooked != nowHooked) { hooked = nowHooked; Obs.obs_sceneitem_set_visible(desktopItem, !hooked); }
            if (hooked) { var gameName = Path.GetFileNameWithoutExtension(exe); if (string.IsNullOrWhiteSpace(gameName)) gameName = "Spiel"; UpdateCaptureTarget(new(gameName, exe, title, "game"), "Spiel · " + gameName); }
            else
            {
                SelectMonitor();
                if (!usingWgc && Obs.obs_source_get_width(desktop) == 0 && DateTime.UtcNow - monitorChanged > TimeSpan.FromSeconds(2))
                {
                    using var fallback = new ObsData().Set("monitor_id", monitorId).Set("method", 2L).Set("capture_cursor", true).Set("force_sdr", true); Obs.obs_source_update(desktop, fallback.Handle); usingWgc = true; wgcMonitors.Add(monitorId); SetStatus("Puffer aktiv · Windows-GPU-Aufnahme als Fallback");
                }
                UpdateCaptureTarget(new("Desktop", "", ForegroundTracker.Title(), usingWgc ? "desktop-wgc" : "desktop-dxgi"), "Desktop · " + monitorName);
            }
            bool slowSave;
            lock (saveGate) slowSave = pending is not null && DateTime.UtcNow - pending.At > TimeSpan.FromSeconds(20);
            if (slowSave && !Status.StartsWith("Clip-Speicherung dauert")) SetStatus("Clip-Speicherung dauert zu lange. Details in capture.log.");
        }
        catch (Exception e) { SetStatus("Fehler: " + e.Message); }
    }
    public void SaveClip()
    {
        if (!IsRunning) throw new InvalidOperationException("Puffer ist gestoppt. Erst die Aufnahme starten.");
        if (DateTime.UtcNow - started < TimeSpan.FromSeconds(2)) throw new InvalidOperationException("Puffer läuft gerade an. Bitte kurz warten.");
        Tick();
        lock (saveGate)
        {
            if (pending != null) throw new InvalidOperationException("Ein Clip wird bereits gespeichert.");
            var save = new Pending(outputGeneration, Guid.NewGuid().ToString("N"), context, settings with { }, EncoderName, DateTime.UtcNow);
            // Bind the native result path to this immutable request. libobs' saved
            // signal has no ID; timestamps alone can reuse a moved staging filename.
            using (var data = new ObsData().Set("format", "Replay_%CCYY-%MM-%DD_%hh-%mm-%ss_" + save.Request)) Obs.obs_output_update(output, data.Handle);
            pending = save; saveError = null;
            var cd = new Obs.CallData();
            try { if (!Obs.proc_handler_call(Obs.obs_output_get_proc_handler(output), "save", ref cd)) throw new InvalidOperationException("Replay-Speicherung nicht verfügbar."); }
            catch { if (ReferenceEquals(pending, save)) pending = null; throw; }
            finally { Obs.bfree(cd.Stack); }
        }
        SetStatus("Clip wird gespeichert …");
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
                        Post(() => { if (generation == outputGeneration) SetStatus(IsRunning ? "Puffer aktiv · Clip gespeichert" : "Puffer gestoppt · Clip gespeichert"); ClipSaved?.Invoke(this, new(record)); });
                    }
                    catch (Exception e)
                    {
                        var error = "Clip bleibt erhalten: " + path + " · " + e.Message;
                        lock (saveGate) saveError = error;
                        Post(() => { if (generation == outputGeneration) SetStatus(error); });
                    }
                }));
                pending = null;
            }
        }
        catch (Exception e) { Post(() => SetStatus("Clip-Fehler: " + e.Message)); }
    }
    void Post(Action action) { try { if (!dispatcher.IsDisposed && dispatcher.IsHandleCreated) dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { } }
    public void Apply(AppSettings s) { s.Validate(); if (settings.Width == s.Width && settings.Height == s.Height && settings.Fps == s.Fps && settings.ClipSeconds == s.ClipSeconds && settings.ClipsDirectory == s.ClipsDirectory && settings.Microphone == s.Microphone && settings.DesktopAudio == s.DesktopAudio && settings.MicrophoneDevice == s.MicrophoneDevice && settings.DesktopDevice == s.DesktopDevice) { settings = s with { }; return; } var old = settings; bool running = IsRunning; if (!running) { settings = s with { }; return; } Stop(); try { Start(s); } catch { try { Start(old); } catch { } throw; } }
    public void Stop() { Release(); SetStatus(saveError ?? "Puffer gestoppt"); }
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
                if (clock.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Native Clip-Speicherung ist noch nicht abgeschlossen. Puffer und Anfrage bleiben erhalten.");
                Thread.Sleep(10);
            }
            if (Obs.obs_output_active(output)) Obs.obs_output_force_stop(output);
            // libobs waits for capture shutdown and joins its mux thread on release.
            // Keep saved connected and the output handle valid until that join finishes;
            // neither native callbacks nor exports wait on the WinForms dispatcher.
            Obs.obs_output_release(output);
            lock (saveGate)
            {
                if (pending is { } save) saveError = "Clip-Speicherung nicht abgeschlossen. Vorhandene Dateien bleiben in " + Path.Combine(save.Settings.ClipsDirectory, ".pending");
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
        if (scene != IntPtr.Zero) { Obs.obs_scene_release(scene); scene = IntPtr.Zero; }
        if (game != IntPtr.Zero) { Obs.obs_source_release(game); game = IntPtr.Zero; }
        if (desktop != IntPtr.Zero) { Obs.obs_source_release(desktop); desktop = IntPtr.Zero; }
        foreach (var a in audioSources) Obs.obs_source_release(a); audioSources.Clear(); host?.Dispose(); host = null; hooked = false; monitorId = ""; monitorName = "";
    }
    public void Dispose() { if (disposed) return; Release(); disposed = true; timer.Dispose(); dispatcher.Dispose(); }
}
