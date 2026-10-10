using System.Text.Json;
using MonoClip.Core;
using static Checks;

var failures = 0;
var count = 0;
void Test(string name, Action test)
{
    if (args.Length > 0 && !name.Contains(args[0], StringComparison.OrdinalIgnoreCase)) return;
    count++;
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.GetType().Name + ": " + e.Message); }
}


Test("balanced 1080p60 uses 15 Mbps reference budget", () => Equal(15000, CapturePolicy.BitrateKbps(1920,1080,60)));
Test("automatic bitrate scales pixels and frame rate sublinearly", () =>
{
    Equal(9000, CapturePolicy.BitrateKbps(1920,1080,30));
    Equal(25000, CapturePolicy.BitrateKbps(1920,1080,120));
    Equal(7500, CapturePolicy.BitrateKbps(1280,720,60));
    Equal(24500, CapturePolicy.BitrateKbps(2560,1440,60));
    Equal(48500, CapturePolicy.BitrateKbps(3840,2160,60));
    True(CapturePolicy.BitrateKbps(1920,1080,120) < 2 * CapturePolicy.BitrateKbps(1920,1080,60), "doubling frames must cost less than double");
    True(CapturePolicy.BitrateKbps(3840,2160,60) < 4 * CapturePolicy.BitrateKbps(1920,1080,60), "quadrupling pixels must cost less than quadruple");
});
Test("quality presets order bitrate performance < balanced < quality", () =>
{
    Equal(9000, CapturePolicy.BitrateKbps(1920,1080,60,QualityPreset.Performance));
    Equal(24000, CapturePolicy.BitrateKbps(1920,1080,60,QualityPreset.Quality));
    foreach (var (w, h) in new[] { (1280,720), (1920,1080), (2560,1440), (3840,2160) })
        foreach (var fps in new[] { 30, 60, 120 })
        {
            int p = CapturePolicy.BitrateKbps(w,h,fps,QualityPreset.Performance), b = CapturePolicy.BitrateKbps(w,h,fps), q = CapturePolicy.BitrateKbps(w,h,fps,QualityPreset.Quality);
            True(p < b && (b < q || q == 100000), $"preset order broken at {w}x{h}@{fps}: {p}/{b}/{q}");
            True(p % 500 == 0 && b % 500 == 0 && q % 500 == 0, "bitrate not rounded to 500 kbps");
        }
    Equal(15000, CapturePolicy.BitrateKbps(new AppSettings()));
    Equal(24000, CapturePolicy.BitrateKbps(new AppSettings { Quality = QualityPreset.Quality }));
});
Test("automatic bitrate clamps to safe encoder limits", () =>
{
    Equal(2500, CapturePolicy.BitrateKbps(1280,720,30,QualityPreset.Performance));
    Equal(100000, CapturePolicy.BitrateKbps(3840,2160,120,QualityPreset.Quality));
});
Test("estimated clip size covers video, every audio track and keyframe slack", () =>
{
    var s = new AppSettings();
    Equal(3, CapturePolicy.AudioTracks(s)); Equal(1, CapturePolicy.AudioTracks(s with { DesktopAudio = false, Microphone = false }));
    var expected = (15000 + 3 * 160) * 1000d * 30.5 / 8 / (1024 * 1024) * 1.02;
    True(Math.Abs(CapturePolicy.EstimatedClipMegabytes(s) - expected) < 0.001, "unexpected 1080p60 30 s estimate " + CapturePolicy.EstimatedClipMegabytes(s));
    True(CapturePolicy.EstimatedClipMegabytes(s with { ClipSeconds = 60 }) > 1.9 * CapturePolicy.EstimatedClipMegabytes(s), "length not reflected");
    True(CapturePolicy.EstimatedClipMegabytes(s with { Quality = QualityPreset.Performance }) < CapturePolicy.EstimatedClipMegabytes(s), "quality not reflected");
    True(CapturePolicy.EstimatedClipMegabytes(s with { Microphone = false }) < CapturePolicy.EstimatedClipMegabytes(s), "audio tracks not reflected");
    True(CapturePolicy.BufferMegabytes(s) > CapturePolicy.EstimatedClipMegabytes(s), "RAM buffer smaller than one clip");
});
Test("settings expose required safe defaults", () =>
{
    var type = typeof(CapturePolicy).Assembly.GetType("MonoClip.Core.AppSettings");
    True(type is not null, "AppSettings is missing");
    var value = Activator.CreateInstance(type!);
    var expected = new Dictionary<string, object>
    {
        ["Width"] = 1920, ["Height"] = 1080, ["Fps"] = 60, ["ClipSeconds"] = 30,
        ["Hotkey"] = "Ctrl+Shift+F9", ["Microphone"] = true, ["DesktopAudio"] = true,
        ["StartWithWindows"] = false, ["StartMinimized"] = true, ["StartBufferOnLaunch"] = true,
        ["Quality"] = QualityPreset.Balanced, ["AdvancedMode"] = false,
        ["MicrophoneDevice"] = "default", ["DesktopDevice"] = "default",
        ["ClipsDirectory"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "MonoClip")
    };
    foreach (var item in expected) Equal(item.Value, type!.GetProperty(item.Key)!.GetValue(value));
});
Test("settings require supported paired resolutions", () =>
{
    Throws<ArgumentException>(() => new AppSettings { Width = 1920, Height = 720 }.Validate());
    Throws<ArgumentException>(() => new AppSettings { Width = 0 }.Validate());
    foreach (var pair in new[] { (1280,720), (1920,1080), (2560,1440), (3840,2160) })
        new AppSettings { Width = pair.Item1, Height = pair.Item2 }.Validate();
});
Test("settings require supported frame rates", () =>
{
    foreach (var fps in new[] { 0, 29, 59, 121, int.MaxValue })
        Throws<ArgumentException>(() => new AppSettings { Fps = fps }.Validate());
    foreach (var fps in new[] { 30, 60, 120 }) new AppSettings { Fps = fps }.Validate();
});
Test("settings require clip duration within inclusive bounds", () =>
{
    foreach (var seconds in new[] { -1, 0, 4, 301, int.MaxValue })
        Throws<ArgumentException>(() => new AppSettings { ClipSeconds = seconds }.Validate());
    foreach (var seconds in new[] { 5, 30, 300 }) new AppSettings { ClipSeconds = seconds }.Validate();
});
Test("buffer memory budget grows with duration and bitrate", () => {
    var method=typeof(CapturePolicy).GetMethod("BufferMegabytes"); True(method is not null,"BufferMegabytes missing");
    int Budget(AppSettings s)=>(int)method!.Invoke(null,new object[]{s})!;
    True(Budget(new())>=60,"insufficient 30 second budget");
    True(Budget(new(){ClipSeconds=60})>Budget(new()),"duration not reflected");
});
Test("settings persist atomically and malformed data is rejected", () => {
    var type=typeof(CapturePolicy).Assembly.GetType("MonoClip.Core.SettingsStore"); True(type is not null,"SettingsStore missing");
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-test-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try {
        var file=Path.Combine(dir,"settings.json");var settings=new AppSettings{ClipSeconds=120,Microphone=false};
        type!.GetMethod("Save")!.Invoke(null,new object[]{file,settings});
        Equal(settings,(AppSettings)type.GetMethod("Load")!.Invoke(null,new object[]{file})!);
        True(!Directory.EnumerateFiles(dir).Any(p=>p.EndsWith(".tmp")),"temporary file left behind");
        File.WriteAllText(file,"not json");
        try {type.GetMethod("Load")!.Invoke(null,new object[]{file});throw new Exception("corrupt settings accepted");}
        catch(System.Reflection.TargetInvocationException e) when(e.InnerException is JsonException){}
    } finally{Directory.Delete(dir,true);}
});
Test("clips are indexed by safe game folder without sidecar metadata", () => {
    var lib=typeof(CapturePolicy).Assembly.GetType("MonoClip.Core.ClipLibrary");True(lib is not null,"ClipLibrary missing");
    var contextType=typeof(CapturePolicy).Assembly.GetType("MonoClip.Core.ClipContext")!;
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-clips-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try {
        var source=Path.Combine(dir,"replay.mkv");File.WriteAllText(source,"fixture, not a real recording");
        var context=Activator.CreateInstance(contextType,new object[]{"../CON: Game","game.exe","A title","game"})!;
        var record=lib!.GetMethod("Store")!.Invoke(null,new object[]{source,Path.Combine(dir,"clips"),context,new AppSettings(),"AMD AMF"})!;
        var path=(string)record.GetType().GetProperty("Path")!.GetValue(record)!;
        True(File.Exists(path),"clip lost");True(!File.Exists(source),"source not moved");
        True(path.StartsWith(Path.Combine(dir,"clips")+Path.DirectorySeparatorChar),"path escaped root");
        True(!File.Exists(path+".json"),"sidecar metadata must not exist");
        Equal("AMD AMF",(string)record.GetType().GetProperty("Encoder")!.GetValue(record)!);
        var games=(IReadOnlyList<string>)lib.GetMethod("GetGames")!.Invoke(null,new object[]{Path.Combine(dir,"clips")})!;Equal(1,games.Count);
    } finally{Directory.Delete(dir,true);}
});
Test("safe game folder rejects Windows reserved names and traversal", () => {
    Equal("_CON",ClipLibrary.SafeName("CON"));Equal("_LPT1.txt",ClipLibrary.SafeName("LPT1.txt"));
    True(!ClipLibrary.SafeName("../../game").Contains('/'),"traversal preserved");
    Equal("Desktop",ClipLibrary.SafeName("..."));Equal("Spiel Ä",ClipLibrary.SafeName("Spiel Ä"));
});
Test("settings reject empty hotkey and nonabsolute clip destination",()=>{
    Throws<ArgumentException>(()=>new AppSettings{Hotkey=""}.Validate());
    Throws<ArgumentException>(()=>new AppSettings{ClipsDirectory="../clips"}.Validate());
    Throws<ArgumentException>(()=>new AppSettings{MicrophoneDevice=""}.Validate());
});
Test("failed clip export preserves original staging video",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-export-error-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var source=Path.Combine(dir,"replay.mkv");var bytes=new byte[]{1,2,3,4};File.WriteAllBytes(source,bytes);var target=Path.Combine(dir,"clips");Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"Test"),"blocking directory fixture");
        Throws<IOException>(()=>ClipLibrary.Store(source,target,new("Test","","","desktop"),new(),"AMD"));True(File.ReadAllBytes(source).SequenceEqual(bytes),"failed export destroyed staging data");True(!Directory.EnumerateFiles(target,"*.json",SearchOption.AllDirectories).Any(),"failed export wrote metadata");
    }finally{Directory.Delete(dir,true);}
});
Test("hidden staging folder is not indexed as a game",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-hidden-"+Guid.NewGuid());Directory.CreateDirectory(Path.Combine(dir,".pending"));
    try{File.WriteAllText(Path.Combine(dir,".pending","raw.mkv"),"fixture");Equal(0,ClipLibrary.GetGames(dir).Count);}finally{Directory.Delete(dir,true);}
});
Test("clip beep default and persisted toggle survive legacy settings",()=>{
    var property=typeof(AppSettings).GetProperty("ClipBeep");True(property is not null,"ClipBeep setting missing");
    Equal(true,(bool)property!.GetValue(new AppSettings())!);
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-beep-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var file=Path.Combine(dir,"settings.json");File.WriteAllText(file,"{}");Equal(true,(bool)property.GetValue(SettingsStore.Load(file))!);var settings=new AppSettings();property.SetValue(settings,false);SettingsStore.Save(file,settings);Equal(false,(bool)property.GetValue(SettingsStore.Load(file))!);}finally{Directory.Delete(dir,true);}
});
Test("saved game folders contain only video files and no metadata sidecars",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-clean-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var source=Path.Combine(dir,"replay.mkv");File.WriteAllBytes(source,new byte[]{1,2,3,4});var clip=ClipLibrary.Store(source,Path.Combine(dir,"clips"),new("Test Game","game.exe","private title","game"),new(),"AMD");
        True(File.Exists(clip.Path),"clip missing");True(!Directory.EnumerateFiles(Path.Combine(dir,"clips"),"*",SearchOption.AllDirectories).Any(p=>!p.EndsWith(".mkv")),"metadata sidecar pollutes clip folder");Equal(1,ClipLibrary.GetGames(Path.Combine(dir,"clips")).Count);
    }finally{Directory.Delete(dir,true);}
});
Test("quality preset persists as readable text and legacy settings default to balanced",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-quality-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var file=Path.Combine(dir,"settings.json");File.WriteAllText(file,"{\"StartBufferOnLaunch\":false}");var legacy=SettingsStore.Load(file);Equal(QualityPreset.Balanced,legacy.Quality);Equal(false,legacy.StartBufferOnLaunch);Equal(false,legacy.AdvancedMode);
        SettingsStore.Save(file,new AppSettings{Quality=QualityPreset.Performance,AdvancedMode=true});True(File.ReadAllText(file).Contains("\"Performance\""),"quality not stored as text");var back=SettingsStore.Load(file);Equal(QualityPreset.Performance,back.Quality);Equal(true,back.AdvancedMode);
        Throws<ArgumentException>(()=>new AppSettings{Quality=(QualityPreset)7}.Validate());
    }finally{Directory.Delete(dir,true);}
});
Test("latest clip is the newest video across game folders, never staging",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-latest-"+Guid.NewGuid());
    try{Equal<string?>(null,ClipLibrary.LatestClip(dir));Directory.CreateDirectory(dir);Equal<string?>(null,ClipLibrary.LatestClip(dir));
        string Make(string folder,string name,int minutesAgo){var d=Path.Combine(dir,folder);Directory.CreateDirectory(d);var f=Path.Combine(d,name);File.WriteAllText(f,"fixture");File.SetLastWriteTimeUtc(f,DateTime.UtcNow.AddMinutes(-minutesAgo));return f;}
        Make("Desktop","old.mkv",30);var newest=Make("Game","new.mkv",1);Make("Game","older.mkv",10);Make(".pending","raw.mkv",0);Make("Desktop","notes.txt",0);
        Equal<string?>(newest,ClipLibrary.LatestClip(dir));
    }finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
});
Test("cursor target: hooked game, other window, desktop or keep",()=>{
    WindowInfo W(string cls,string exe,bool tool=false,int w=1280,int h=720)=>new(cls,"Title",exe,tool,w,h);
    Equal(CaptureKind.Game,CaptureTargetPolicy.Decide(W("UnrealWindow","VALORANT-Win64-Shipping.exe"),"valorant-win64-shipping.exe"));
    Equal(CaptureKind.Window,CaptureTargetPolicy.Decide(W("Chrome_WidgetWin_1","Discord.exe"),"VALORANT-Win64-Shipping.exe"));
    Equal(CaptureKind.Window,CaptureTargetPolicy.Decide(W("UnrealWindow","VALORANT-Win64-Shipping.exe"),""));
    Equal(CaptureKind.Desktop,CaptureTargetPolicy.Decide(null,"game.exe"));
    Equal(CaptureKind.Desktop,CaptureTargetPolicy.Decide(W("Progman","explorer.exe"),""));
    Equal(CaptureKind.Desktop,CaptureTargetPolicy.Decide(W("Shell_TrayWnd","explorer.exe"),""));
    Equal(CaptureKind.Keep,CaptureTargetPolicy.Decide(W("#32768","Discord.exe"),""));
    Equal(CaptureKind.Keep,CaptureTargetPolicy.Decide(W("tooltips_class32","Discord.exe"),""));
    Equal(CaptureKind.Keep,CaptureTargetPolicy.Decide(W("WindowsForms10.Window.808","MonoClip.exe",tool:true),""));
    Equal(CaptureKind.Keep,CaptureTargetPolicy.Decide(W("Popup","app.exe",w:40,h:20),""));
    Equal(CaptureKind.Keep,CaptureTargetPolicy.Decide(W("Protected","",w:800,h:600),""));
});
Test("window selector escapes separators like libobs",()=>{
    Equal("Server#3A #22general | Discord:Chrome_WidgetWin_1:Discord.exe",CaptureTargetPolicy.EncodeWindow(new("Chrome_WidgetWin_1","Server: #general | Discord","Discord.exe",false,800,600)));
});
// NEXT_TEST
Console.WriteLine($"RESULT {count - failures}/{count} passed");
return failures == 0 && count > 0 ? 0 : 1;

static class Checks
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
    public static void True(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}");
    }
}
