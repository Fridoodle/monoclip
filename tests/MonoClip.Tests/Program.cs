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


Test("1080p60 uses automatic 12 Mbps budget", () => Equal(12000, CapturePolicy.BitrateKbps(1920,1080,60)));
Test("automatic bitrate scales pixels and frame rate", () =>
{
    Equal(6000, CapturePolicy.BitrateKbps(1920,1080,30));
    Equal(24000, CapturePolicy.BitrateKbps(1920,1080,120));
});
Test("automatic bitrate rounds to nearest thousand", () =>
{
    Equal(5000, CapturePolicy.BitrateKbps(1280,720,60));
    Equal(21000, CapturePolicy.BitrateKbps(2560,1440,60));
});
Test("automatic bitrate clamps to safe encoder limits", () =>
{
    Equal(4000, CapturePolicy.BitrateKbps(1280,720,30));
    Equal(80000, CapturePolicy.BitrateKbps(3840,2160,120));
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
        ["StartWithWindows"] = false, ["StartMinimized"] = true, ["StartBufferOnLaunch"] = false,
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
