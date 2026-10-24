using MonoClip.Core;
using static MonoClip.Windows.Capture.Obs;
namespace MonoClip.Windows.Capture;

// What the buffer records: one libobs scene with the monitor, the hooked game and two window captures, of which only
// the one the user is in is shown. ClipEngine lets it follow the user every 250 ms.
internal sealed class CaptureScene : IDisposable
{
    // The capture on screen: what a clip saved now is about, and how the settings window names it.
    public sealed record Shown(ClipContext Context, string Target);
    static readonly string OwnExecutable = Path.GetFileName(Environment.ProcessPath ?? "MonoClip.exe");
    readonly int width, height; readonly HashSet<string> wgcMonitors;
    IntPtr scene, desktop, game, desktopItem, gameItem;
    // Two window sources alternate: the next window warms up hidden while the current one stays visible.
    readonly IntPtr[] windows = new IntPtr[2], windowItems = new IntPtr[2], windowHandles = new IntPtr[2]; int warming = -1; DateTime warmingSince; bool keepShowing;
    IntPtr shownItem, candidateHandle; CaptureKind candidate = CaptureKind.Desktop; WindowInfo? candidateInfo; int candidateTicks;
    string monitorId = "", monitorName = ""; bool usingWgc; DateTime monitorChanged;
    IntPtr[] Items => [desktopItem, gameItem, windowItems[0], windowItems[1]];
    public IntPtr Source => obs_scene_get_source(scene);
    // Raised when a monitor gives DXGI duplication no frames and its capture switches to WGC.
    public event Action? MonitorFallback;
    // Fill the frame while the user's monitor runs a stretched resolution (see CapturePolicy.IsStretchedMode).
    public bool StretchToFill { get; set; } = true;
    bool stretched;

    // wgcMonitors: the monitors that needed the WGC fallback, kept by the engine so a restarted buffer starts with it.
    public CaptureScene(int width, int height, HashSet<string> wgcMonitors)
    {
        this.width = width; this.height = height; this.wgcMonitors = wgcMonitors;
        try
        {
            scene = Require(obs_scene_create("MonoClip capture"), "Scene");
            using (var data = MonitorSettings("DUMMY", wgc: false)) desktop = Require(obs_source_create("monitor_capture", "Desktop", data.Handle, IntPtr.Zero), "Desktop capture");
            desktopItem = Add(desktop, visible: true); SelectMonitor(true);
            for (int i = 0; i < windows.Length; i++)
            {
                // WGC window capture, matched by executable first (titles change while apps run).
                using (var data = new ObsData().Set("window", "").Set("method", 2L).Set("priority", 2L).Set("cursor", true).Set("client_area", true).Set("force_sdr", true).Set("capture_audio", false)) windows[i] = Require(obs_source_create("window_capture", "Active window " + (i + 1), data.Handle, IntPtr.Zero), "Window capture");
                windowItems[i] = Add(windows[i], visible: false);
            }
            using (var data = new ObsData().Set("capture_mode", "any_fullscreen").Set("capture_cursor", true).Set("limit_framerate", true).Set("anti_cheat_hook", true).Set("capture_overlays", false).Set("capture_audio", false)) game = Require(obs_source_create("game_capture", "Automatic game", data.Handle, IntPtr.Zero), "Game capture");
            gameItem = Add(game, visible: false); shownItem = desktopItem;
            // Hidden scene items stop ticking. Keep the game hook and window capture alive
            // so switching between them is instant instead of re-hooking each time.
            obs_source_inc_showing(game); foreach (var w in windows) obs_source_inc_showing(w); keepShowing = true;
        }
        catch { Dispose(); throw; }
    }
    IntPtr Add(IntPtr source, bool visible)
    {
        var item = obs_scene_add(scene, source);
        obs_sceneitem_set_bounds_type(item, 2); obs_sceneitem_set_bounds_alignment(item, 0); var bounds = new Vec2(width, height); obs_sceneitem_set_bounds(item, ref bounds);
        if (!visible) obs_sceneitem_set_visible(item, false);
        return item;
    }
    static ObsData MonitorSettings(string id, bool wgc) => new ObsData().Set("monitor_id", id).Set("method", wgc ? 2L : 1L).Set("capture_cursor", true).Set("force_sdr", true);
    // The desktop as shown now (the foreground title is what the clip remembers).
    public Shown DesktopTarget => new(new("Desktop", "", ForegroundTracker.Title(), usingWgc ? "desktop-wgc" : "desktop-dxgi"), "Desktop · " + monitorName);

    void SelectMonitor(bool force = false)
    {
        var selected = ForegroundTracker.Monitor(); if (!force && selected.Id == monitorId) return;
        string id = selected.Id;
        if (string.IsNullOrWhiteSpace(id))
        {
            var props = obs_source_properties(desktop); try { var prop = obs_properties_get(props, "monitor_id"); for (nuint i = 0; i < obs_property_list_item_count(prop); i++) { var value = Str(obs_property_list_item_string(prop, i)); if (value != "DUMMY") { id = value; break; } } } finally { obs_properties_destroy(props); }
        }
        if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("No display found for GPU capture.");
        usingWgc = wgcMonitors.Contains(id); monitorChanged = DateTime.UtcNow;
        using var data = MonitorSettings(id, usingWgc); obs_source_update(desktop, data.Handle); monitorId = id;
        monitorName = selected.Name;
    }
    // Capture the window the user is in (focus, not the mouse, so controller play works): the hooked game, any
    // other window, or the monitor for the desktop. No active window for a moment keeps the current capture.
    // Returns what is shown now, or null when the picture stays as it was.
    public Shown? Follow()
    {
        ApplyStretch(StretchToFill && DisplayMode.Stretched(ForegroundTracker.Monitor().Name));
        var (hooked, gameExe, gameTitle) = HookedGame();
        var (handle, info) = ForegroundTracker.ActiveWindow();
        var kind = handle == IntPtr.Zero ? CaptureKind.Keep : CaptureTargetPolicy.Decide(info, hooked ? Path.GetFileName(gameExe) : "", OwnExecutable);
        if (kind != CaptureKind.Keep && (kind != candidate || (kind == CaptureKind.Window && handle != candidateHandle))) { candidate = kind; candidateHandle = handle; candidateInfo = info; candidateTicks = 0; }
        else candidateTicks++;
        // Confirm for one more tick so quickly tabbing through windows does not thrash capture.
        if (candidateTicks < 1) return shownItem == desktopItem ? ShowDesktop() : null;
        switch (candidate)
        {
            case CaptureKind.Game:
                StopWarming(); Show(gameItem);
                var gameName = Path.GetFileNameWithoutExtension(gameExe); if (string.IsNullOrWhiteSpace(gameName)) gameName = "Game";
                return new(new(gameName, gameExe, gameTitle, "game"), "Game · " + gameName);
            case CaptureKind.Window when candidateInfo != null:
                var ready = FollowWindow(candidateHandle, candidateInfo);
                // Not a game hook: window clips stay in the Desktop folder.
                if (ready == true) return new(new("Desktop", candidateInfo.Executable, candidateInfo.Title, "window"), "Window · " + Path.GetFileNameWithoutExtension(candidateInfo.Executable));
                return ready == false || shownItem == desktopItem ? ShowDesktop() : null;
            default:
                StopWarming(); return ShowDesktop();
        }
    }
    (bool Hooked, string Executable, string Title) HookedGame()
    {
        var cd = new CallData();
        try
        {
            proc_handler_call(obs_source_get_proc_handler(game), "get_hooked", ref cd); calldata_get_data(ref cd, "hooked", out var value, 1);
            if (value == 0) return (false, "", "");
            calldata_get_string(ref cd, "executable", out var exe); calldata_get_string(ref cd, "title", out var title);
            return (true, Str(exe), Str(title));
        }
        finally { bfree(cd.Stack); }
    }
    // OBS bounds types: 1 stretches a picture to the frame, 2 fits it inside keeping its shape (black bars).
    internal void ApplyStretch(bool stretch)
    {
        if (stretch == stretched) return;
        stretched = stretch;
        foreach (var item in Items) if (item != IntPtr.Zero) obs_sceneitem_set_bounds_type(item, stretch ? 1 : 2);
    }
    // true: shown. null: still warming up, keep the current picture. false: no frames yet.
    bool? FollowWindow(IntPtr handle, WindowInfo info)
    {
        int active = Array.IndexOf(windowItems, shownItem);
        if (active >= 0 && windowHandles[active] == handle) return true;
        if (warming < 0 || windowHandles[warming] != handle)
        {
            if (warming >= 0) SetWindow(warming, IntPtr.Zero, null);
            warming = active == 0 ? 1 : 0; SetWindow(warming, handle, info); warmingSince = DateTime.UtcNow;
            return null; // libobs applies source updates on its next video tick.
        }
        if (obs_source_get_width(windows[warming]) > 0) { var item = windowItems[warming]; warming = -1; Show(item); return true; }
        return DateTime.UtcNow - warmingSince > TimeSpan.FromSeconds(2) ? false : null;
    }
    void SetWindow(int slot, IntPtr handle, WindowInfo? info)
    {
        if (windowHandles[slot] == handle) return;
        windowHandles[slot] = handle;
        using var data = new ObsData().Set("window", info == null ? "" : CaptureTargetPolicy.EncodeWindow(info)); obs_source_update(windows[slot], data.Handle);
    }
    void StopWarming() { if (warming < 0) return; SetWindow(warming, IntPtr.Zero, null); warming = -1; }
    void Show(IntPtr item)
    {
        if (shownItem == item) return;
        foreach (var x in Items) obs_sceneitem_set_visible(x, x == item);
        shownItem = item; if (item == desktopItem) monitorChanged = DateTime.UtcNow;
        // Window captures that are neither visible nor warming up cost GPU time; stop them.
        for (int i = 0; i < windows.Length; i++) if (windowItems[i] != item && i != warming) SetWindow(i, IntPtr.Zero, null);
    }
    Shown ShowDesktop()
    {
        SelectMonitor(); Show(desktopItem);
        if (!usingWgc && obs_source_get_width(desktop) == 0 && DateTime.UtcNow - monitorChanged > TimeSpan.FromSeconds(2))
        {
            using var fallback = MonitorSettings(monitorId, wgc: true); obs_source_update(desktop, fallback.Handle); usingWgc = true; wgcMonitors.Add(monitorId); MonitorFallback?.Invoke();
        }
        return DesktopTarget;
    }
    // Remove the items first: a released scene lets go of its sources only when libobs gets round to destroying it,
    // which may be after shutdown ("sources remaining", then "Double destroy").
    public void Dispose()
    {
        if (scene != IntPtr.Zero) { foreach (var item in Items) if (item != IntPtr.Zero) obs_sceneitem_remove(item); obs_scene_release(scene); scene = IntPtr.Zero; }
        desktopItem = gameItem = windowItems[0] = windowItems[1] = IntPtr.Zero;
        if (keepShowing) { obs_source_dec_showing(game); foreach (var w in windows) obs_source_dec_showing(w); keepShowing = false; }
        if (game != IntPtr.Zero) { obs_source_release(game); game = IntPtr.Zero; }
        for (int i = 0; i < windows.Length; i++) { if (windows[i] != IntPtr.Zero) obs_source_release(windows[i]); windows[i] = IntPtr.Zero; }
        if (desktop != IntPtr.Zero) { obs_source_release(desktop); desktop = IntPtr.Zero; }
    }
}
