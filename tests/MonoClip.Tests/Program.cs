using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
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
    // One source: its own track would copy the mix, so only the mix is encoded; the same when separate tracks are off.
    Equal(1, CapturePolicy.AudioTracks(s with { Microphone = false })); Equal(1, CapturePolicy.AudioTracks(s with { DesktopAudio = false })); Equal(1, CapturePolicy.AudioTracks(s with { SeparateAudioTracks = false }));
    True(CapturePolicy.SeparateTracks(s) && !CapturePolicy.SeparateTracks(s with { Microphone = false }), "separate tracks only with both sources");
    // Tracks carry the names ClipAudio finds them by; each source feeds the mix (mixer 1) and its own track.
    Equal("Playback mix,Desktop,Microphone", string.Join(",", CapturePolicy.AudioTrackNames(s))); Equal("Playback mix", string.Join(",", CapturePolicy.AudioTrackNames(s with { SeparateAudioTracks = false })));
    Equal(3u, CapturePolicy.AudioMixers(s, ClipAudio.Desktop)); Equal(5u, CapturePolicy.AudioMixers(s, ClipAudio.Microphone)); Equal(1u, CapturePolicy.AudioMixers(s with { Microphone = false }, ClipAudio.Desktop));
    var expected = (15000 + 3 * 160) * 1000d * 30.5 / 8 / (1024 * 1024) * 1.02;
    True(Math.Abs(CapturePolicy.EstimatedClipMegabytes(s) - expected) < 0.001, "unexpected 1080p60 30 s estimate " + CapturePolicy.EstimatedClipMegabytes(s));
    True(CapturePolicy.EstimatedClipMegabytes(s with { ClipSeconds = 60 }) > 1.9 * CapturePolicy.EstimatedClipMegabytes(s), "length not reflected");
    True(CapturePolicy.EstimatedClipMegabytes(s with { Quality = QualityPreset.Performance }) < CapturePolicy.EstimatedClipMegabytes(s), "quality not reflected");
    True(CapturePolicy.EstimatedClipMegabytes(s with { Microphone = false }) < CapturePolicy.EstimatedClipMegabytes(s), "audio tracks not reflected");
    True(CapturePolicy.BufferMegabytes(s) > CapturePolicy.EstimatedClipMegabytes(s), "RAM buffer smaller than one clip");
    // Only what the buffer is built from restarts it.
    foreach (var changed in new[] { s with { Width = 1280, Height = 720 }, s with { Fps = 30 }, s with { ClipSeconds = 60 }, s with { ClipsDirectory = @"D:\Clips" }, s with { Microphone = false }, s with { DesktopAudio = false }, s with { SeparateAudioTracks = false }, s with { MicrophoneDevice = "mic" }, s with { DesktopDevice = "out" } })
        True(CapturePolicy.NeedsRestart(s, changed), "no restart for " + changed);
    foreach (var same in new[] { s, s with { Hotkey = "Ctrl+F1" }, s with { ShareMinutes = 30 }, s with { SortByApp = false }, s with { ClipBeep = false }, s with { StartWithWindows = true } })
        True(!CapturePolicy.NeedsRestart(s, same), "needless restart for " + same);
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
        ["Quality"] = QualityPreset.Balanced, ["AdvancedMode"] = false, ["ShareMinutes"] = 15, ["AutoShare"] = false, ["CopyShareLink"] = true, ["SortByApp"] = true, ["DirectLink"] = false, ["SeparateAudioTracks"] = true, ["ExportDirectory"] = "",
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
    foreach (var seconds in new[] { -1, 0, 4, 1001, int.MaxValue })
        Throws<ArgumentException>(() => new AppSettings { ClipSeconds = seconds }.Validate());
    foreach (var seconds in new[] { 5, 30, 300, 1000 }) new AppSettings { ClipSeconds = seconds }.Validate();
    Equal(30, new AppSettings().ClipSeconds);
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
    Equal("Desktop",ClipLibrary.SafeName("..."));Equal("Café Ä",ClipLibrary.SafeName("Café Ä"));
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
    // MonoClip's own settings or editor getting focus keeps capturing the game or window from before.
    Equal(CaptureKind.Keep,CaptureTargetPolicy.Decide(W("WindowsForms10.Window.8.app","MonoClip.exe"),"game.exe","MonoClip.exe"));
    Equal(CaptureKind.Window,CaptureTargetPolicy.Decide(W("WindowsForms10.Window.8.app","MonoClip.exe"),"game.exe"));
    True(CaptureTargetPolicy.IsDesktopBackground("WorkerW")&&CaptureTargetPolicy.IsDesktopBackground("Progman")&&!CaptureTargetPolicy.IsDesktopBackground("Shell_SecondaryTrayWnd"),"desktop background classes");
});
Test("window selector escapes separators like libobs",()=>{
    Equal("Server#3A #22general | Discord:Chrome_WidgetWin_1:Discord.exe",CaptureTargetPolicy.EncodeWindow(new("Chrome_WidgetWin_1","Server: #general | Discord","Discord.exe",false,800,600)));
});
Test("share range parsing follows single-range HTTP rules",()=>{
    (long,long)? R(string? h,long len,out bool bad)=>ClipShareServer.ParseRange(h,len,out bad);bool u;
    Equal<(long,long)?>(null,R(null,100,out u));True(!u,"no header is satisfiable");
    Equal<(long,long)?>((0,99),R("bytes=0-",100,out u));Equal<(long,long)?>((10,19),R("bytes=10-19",100,out u));Equal<(long,long)?>((90,99),R("bytes=90-500",100,out u));
    Equal<(long,long)?>((95,99),R("bytes=-5",100,out u));Equal<(long,long)?>((0,99),R("bytes=-500",100,out u));
    Equal<(long,long)?>(null,R("bytes=100-",100,out u));True(u,"start past end must be 416");
    Equal<(long,long)?>(null,R("bytes=-0",100,out u));True(u,"empty suffix must be 416");
    foreach(var ignored in new[]{"items=0-1","bytes=0-1,5-6","bytes=x-1","bytes=5-2","bytes=","bytes=+1-2"}){Equal<(long,long)?>(null,R(ignored,100,out u));True(!u,"ignored range reported as unsatisfiable: "+ignored);}
});
Test("share policy finds tunnel host and builds unguessable clip path",()=>{
    Equal<string?>("https://brave-cat-tree.trycloudflare.com",SharePolicy.FindQuickTunnelUrl("2026-10-10T08:00:00Z INF |  https://Brave-Cat-Tree.trycloudflare.com                    |"));
    Equal<string?>(null,SharePolicy.FindQuickTunnelUrl("ERR failed to request quick Tunnel: Post \"https://api.trycloudflare.com/tunnel\""));
    Equal<string?>(null,SharePolicy.FindQuickTunnelUrl("INF Requesting new quick Tunnel on trycloudflare.com..."));
    var a=SharePolicy.NewToken();var b=SharePolicy.NewToken();Equal(32,a.Length);True(a!=b&&a.All(Uri.IsHexDigit),"token not random hex");
    Equal("/"+a+"/2026-10-10_10-17-49_4655f68c.mp4",SharePolicy.RequestPath(a,@"C:\Clips\Game\2026-10-10_10-17-49_4655f68c.mkv"));
    Equal("/"+a+"/My%20Clip.mp4",SharePolicy.RequestPath(a,"My Clip.mkv"));Equal(15,SharePolicy.DefaultMinutes);
});
Test("fast start moves the index before media data and shifts 32/64-bit chunk offsets",()=>{
    foreach(var wide in new[]{false,true})
    {
        var dir=Path.Combine(Path.GetTempPath(),"MonoClip-faststart-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        try{var payload=Enumerable.Range(0,100).Select(i=>(byte)(i*3+1)).ToArray();var input=Path.Combine(dir,"in.mp4");var output=Path.Combine(dir,"out.mp4");
            File.WriteAllBytes(input,Mp4Fixture.File(payload,wide,moovFirst:false));
            True(Mp4FastStart.Apply(input,output),"moov-at-end file reported as already fast start");
            var bytes=File.ReadAllBytes(output);Equal(new FileInfo(input).Length,bytes.LongLength);
            True(Mp4Fixture.IndexOf(bytes,"moov")<Mp4Fixture.IndexOf(bytes,"mdat"),"moov still after mdat");
            var offsets=Mp4Fixture.ChunkOffsets(bytes,wide);Equal(2,offsets.Length);
            True(bytes.AsSpan((int)offsets[0],10).SequenceEqual(payload.AsSpan(0,10)),"first chunk offset not shifted");
            True(bytes.AsSpan((int)offsets[1],10).SequenceEqual(payload.AsSpan(50,10)),"second chunk offset not shifted");
            File.WriteAllBytes(input,Mp4Fixture.File(payload,wide,moovFirst:true));True(!Mp4FastStart.Apply(input,output),"fast-start file rewritten");True(File.ReadAllBytes(output).SequenceEqual(File.ReadAllBytes(input)),"fast-start file changed");
            File.WriteAllBytes(input,new byte[]{0,0,0,9,(byte)'f',(byte)'r',(byte)'e',(byte)'e'});Throws<InvalidDataException>(()=>Mp4FastStart.Apply(input,output));
        }finally{Directory.Delete(dir,true);}
    }
});
Test("share server serves exactly the clip with ranges, HEAD and correct errors",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-share-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var data=Enumerable.Range(0,300000).Select(i=>(byte)(i*7)).ToArray();var file=Path.Combine(dir,"clip.mp4");File.WriteAllBytes(file,data);
        using var server=new ClipShareServer(file,"/tok/clip.mp4");server.Start();using var http=new HttpClient{BaseAddress=new($"http://127.0.0.1:{server.Port}")};
        HttpResponseMessage Get(string path,string? range=null,HttpMethod? method=null){var r=new HttpRequestMessage(method??HttpMethod.Get,path);if(range!=null)r.Headers.TryAddWithoutValidation("Range",range);return http.Send(r);}
        var full=Get("/tok/clip.mp4");Equal(200,(int)full.StatusCode);Equal("video/mp4",full.Content.Headers.ContentType!.MediaType);True(full.Content.ReadAsByteArrayAsync().Result.SequenceEqual(data),"full body differs");Equal("bytes",full.Headers.AcceptRanges.Single());
        var part=Get("/tok/clip.mp4","bytes=100-199");Equal(206,(int)part.StatusCode);Equal("bytes 100-199/300000",part.Content.Headers.ContentRange!.ToString());True(part.Content.ReadAsByteArrayAsync().Result.SequenceEqual(data[100..200]),"range body differs");
        True(Get("/tok/clip.mp4","bytes=-10").Content.ReadAsByteArrayAsync().Result.SequenceEqual(data[^10..]),"suffix range differs");
        var head=Get("/tok/clip.mp4",method:HttpMethod.Head);Equal(200,(int)head.StatusCode);Equal(300000L,head.Content.Headers.ContentLength);Equal(0,head.Content.ReadAsByteArrayAsync().Result.Length);
        Equal(200,(int)Get("/tok/clip.mp4?utm=discord").StatusCode);
        foreach(var bad in new[]{"/","/tok/","/TOK/clip.mp4","/tok/other.mp4","/tok//clip.mp4","/tok/clip.mp4%2F..%2Fx","/settings.json"})Equal(404,(int)Get(bad).StatusCode);
        Equal(416,(int)Get("/tok/clip.mp4","bytes=300000-").StatusCode);
        Equal(405,(int)Get("/tok/clip.mp4",method:HttpMethod.Post).StatusCode);
    }finally{Directory.Delete(dir,true);}
});
Test("share server keeps connections alive, caps parallel viewers and stops completely",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-share-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var file=Path.Combine(dir,"clip.mp4");File.WriteAllBytes(file,Enumerable.Range(0,1000).Select(i=>(byte)i).ToArray());
        var server=new ClipShareServer(file,"/t/c.mp4",maxConnections:2);server.Start();int port=server.Port;
        using(var raw=new TcpClient("127.0.0.1",port)){var s=raw.GetStream();s.ReadTimeout=3000;var req=Encoding.ASCII.GetBytes("GET /t/c.mp4 HTTP/1.1\r\nHost: x\r\nRange: bytes=0-9\r\n\r\n");s.Write(req);s.Write(req);
            var text=new StringBuilder();var buf=new byte[4096];while(System.Text.RegularExpressions.Regex.Matches(text.ToString(),"HTTP/1.1 206").Count<2||!text.ToString().EndsWith("\x09")){int n=s.Read(buf);if(n==0)break;text.Append(Encoding.Latin1.GetString(buf,0,n));}
            Equal(2,System.Text.RegularExpressions.Regex.Matches(text.ToString(),"HTTP/1.1 206").Count);True(text.ToString().Contains("Connection: keep-alive"),"keep-alive not announced");}
        var holders=new[]{new TcpClient("127.0.0.1",port),new TcpClient("127.0.0.1",port)};Thread.Sleep(300);
        using(var http=new HttpClient{BaseAddress=new($"http://127.0.0.1:{port}")}){Equal(503,(int)http.GetAsync("/t/c.mp4").Result.StatusCode);holders[0].Dispose();Thread.Sleep(300);Equal(200,(int)http.GetAsync("/t/c.mp4").Result.StatusCode);}
        holders[1].Dispose();server.Dispose();
        bool refused=false;try{using var late=new TcpClient("127.0.0.1",port);}catch(SocketException){refused=true;}True(refused,"stopped server still accepts connections");
        File.Delete(file);True(!File.Exists(file),"shared copy could not be removed");
    }finally{Directory.Delete(dir,true);}
});
Test("share duration is limited to 5 to 30 minutes and survives legacy settings",()=>{
    foreach(var m in new[]{-1,0,4,121,600})Throws<ArgumentException>(()=>new AppSettings{ShareMinutes=m}.Validate());
    foreach(var m in new[]{5,15,30,120})new AppSettings{ShareMinutes=m}.Validate();Equal(15,new AppSettings().ShareMinutes);
    foreach(var m in new[]{5,15,30})new AppSettings{ShareMinutes=m}.Validate();
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-sharemin-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var file=Path.Combine(dir,"settings.json");File.WriteAllText(file,"{}");Equal(15,SettingsStore.Load(file).ShareMinutes);SettingsStore.Save(file,new AppSettings{ShareMinutes=25});Equal(25,SettingsStore.Load(file).ShareMinutes);}finally{Directory.Delete(dir,true);}
});
Test("share server announces cache lifetime matching the share duration",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-cache-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var file=Path.Combine(dir,"c.mp4");File.WriteAllBytes(file,new byte[10]);using var server=new ClipShareServer(file,"/t/c.mp4",cacheSeconds:300);server.Start();using var http=new HttpClient();
        Equal("public, max-age=300",http.GetAsync($"http://127.0.0.1:{server.Port}/t/c.mp4").Result.Headers.CacheControl!.ToString());}finally{Directory.Delete(dir,true);}
});
Test("starter passes every argument through Windows command-line parsing unchanged",()=>{
    string[] args=["--settings","--minimized","","with space","quote\"inside","trailing\\","C:\\Path with spaces\\","a\\\\\"b","tab\there","naïve Ü ß"];
    var line=CommandLine.Build(@"C:\Mono Clip\app\MonoClip.exe",args);
    var parsed=WindowsArgv.Parse(line);
    Equal(@"C:\Mono Clip\app\MonoClip.exe",parsed[0]);Equal(args.Length+1,parsed.Length);
    for(int i=0;i<args.Length;i++)Equal(args[i],parsed[i+1]);
    Equal("plain",CommandLine.Quote("plain"));Equal("\"\"",CommandLine.Quote(""));
});
Test("only real MKV/MP4 videos up to the size limit can be shared manually",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-shareable-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{string F(string name,byte[] data){var p=Path.Combine(dir,name);File.WriteAllBytes(p,data);return p;}
        byte[] mkv=[0x1A,0x45,0xDF,0xA3,0x9F,0x42,0x86,0x81,1,0,0,0];byte[] mp4=[0,0,0,24,(byte)'f',(byte)'t',(byte)'y',(byte)'p',(byte)'i',(byte)'s',(byte)'o',(byte)'m'];
        SharePolicy.CheckShareable(F("clip.mkv",mkv));SharePolicy.CheckShareable(F("clip.MP4",mp4));
        Throws<ArgumentException>(()=>SharePolicy.CheckShareable(Path.Combine(dir,"missing.mkv")));
        Throws<ArgumentException>(()=>SharePolicy.CheckShareable(F("tool.exe",mkv)));
        Throws<ArgumentException>(()=>SharePolicy.CheckShareable(F("renamed.mp4",Encoding.ASCII.GetBytes("MZ this is a program, not a video"))));
        Throws<ArgumentException>(()=>SharePolicy.CheckShareable(F("empty.mkv",[])));
        Throws<ArgumentException>(()=>SharePolicy.CheckShareable(F("tiny.mkv",[0x1A,0x45])));
        var big=Path.Combine(dir,"big.mkv");using(var s=File.Create(big)){s.Write(mkv);s.SetLength((SharePolicy.MaxShareMegabytes+1)*1024L*1024);}
        try{SharePolicy.CheckShareable(big);throw new Exception("oversized file accepted");}catch(ArgumentException e){True(e.Message.Contains("max "+SharePolicy.MaxShareMegabytes+" MB"),"size reason missing: "+e.Message);}
        Equal(1024,SharePolicy.MaxShareMegabytes);
    }finally{Directory.Delete(dir,true);}
});
Test("share server can start before the clip is ready and answers 503 until then",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-ready-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var file=Path.Combine(dir,"later.mp4");using var server=new ClipShareServer(file,"/t/later.mp4"){Ready=false};server.Start();using var http=new HttpClient{BaseAddress=new($"http://127.0.0.1:{server.Port}")};
        var early=http.GetAsync("/t/later.mp4").Result;Equal(503,(int)early.StatusCode);Equal("1",early.Headers.RetryAfter!.ToString());
        File.WriteAllBytes(file,new byte[]{1,2,3});Equal(503,(int)http.GetAsync("/t/later.mp4").Result.StatusCode);
        server.Ready=true;var ok=http.GetAsync("/t/later.mp4").Result;Equal(200,(int)ok.StatusCode);Equal(3L,ok.Content.Headers.ContentLength);
    }finally{Directory.Delete(dir,true);}
});
Test("one share server serves clip after clip and drops the previous path",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-swap-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var a=Path.Combine(dir,"a.mp4");var b=Path.Combine(dir,"b.mp4");File.WriteAllBytes(a,new byte[]{1});File.WriteAllBytes(b,new byte[]{2,2});
        using var server=new ClipShareServer();server.Start();using var http=new HttpClient{BaseAddress=new($"http://127.0.0.1:{server.Port}")};
        Equal(404,(int)http.GetAsync("/x/a.mp4").Result.StatusCode);
        server.Publish(a,"/x/a.mp4",60);Equal(1L,http.GetAsync("/x/a.mp4").Result.Content.Headers.ContentLength);
        server.Publish(b,"/y/b.mp4",120);Equal(404,(int)http.GetAsync("/x/a.mp4").Result.StatusCode);var second=http.GetAsync("/y/b.mp4").Result;Equal(2L,second.Content.Headers.ContentLength);Equal("public, max-age=120",second.Headers.CacheControl!.ToString());
        server.Unpublish();Equal(404,(int)http.GetAsync("/y/b.mp4").Result.StatusCode);Equal("",server.RequestPath);
    }finally{Directory.Delete(dir,true);}
});
Test("unsorted clips go straight into the clip folder",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-flat-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var root=Path.Combine(dir,"clips");var a=Path.Combine(dir,"a.mkv");File.WriteAllBytes(a,[1]);var b=Path.Combine(dir,"b.mkv");File.WriteAllBytes(b,[2]);
        var flat=ClipLibrary.Store(a,root,new("Some Game","game.exe","","game"),new AppSettings{SortByApp=false},"AMD");Equal(Path.GetFullPath(root),Path.GetDirectoryName(flat.Path));
        var sorted=ClipLibrary.Store(b,root,new("Some Game","game.exe","","game"),new AppSettings(),"AMD");Equal(Path.Combine(Path.GetFullPath(root),"Some Game"),Path.GetDirectoryName(sorted.Path));
        Equal(1,ClipLibrary.GetGames(root).Count);True(ClipLibrary.LatestClip(root)!=null,"latest clip not found");
    }finally{Directory.Delete(dir,true);}
});
Test("mp4 info reads video size and duration, skipping audio tracks",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-mp4info-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var file=Path.Combine(dir,"c.mp4");File.WriteAllBytes(file,Mp4Fixture.WithTracks(timescale:1000,length:34500,(0,0),(1920,1080)));
        var info=Mp4Info.Read(file)!;Equal(1920,info.Width);Equal(1080,info.Height);Equal(34.5,info.Duration.TotalSeconds);
        Equal("0:34 · 1920×1080 · shared with MonoClip",SharePage.Describe(info));
        File.WriteAllBytes(file,[0,0,0,8,(byte)'f',(byte)'r',(byte)'e',(byte)'e']);Equal<Mp4Info?>(null,Mp4Info.Read(file));
    }finally{Directory.Delete(dir,true);}
});
Test("share page carries video preview tags and escapes its text",()=>{
    var html=SharePage.Build(new("<script>alert(1)</script> clip","0:30 · 1920×1080","https://a.trycloudflare.com/tok","https://a.trycloudflare.com/tok/c.mp4","https://a.trycloudflare.com/tok/preview.jpg",1920,1080,1280,720));
    foreach(var tag in new[]{"property=\"og:site_name\" content=\"Clipped with MonoClip\"","property=\"og:type\" content=\"video.other\"","property=\"og:video\" content=\"https://a.trycloudflare.com/tok/c.mp4\"","property=\"og:video:type\" content=\"video/mp4\"","property=\"og:video:width\" content=\"1920\"","property=\"og:video:height\" content=\"1080\"","property=\"og:image\" content=\"https://a.trycloudflare.com/tok/preview.jpg\"","property=\"og:image:width\" content=\"1280\"","name=\"twitter:card\" content=\"player\"","<video src=\"https://a.trycloudflare.com/tok/c.mp4\""})
        True(html.Contains(tag),"missing "+tag);
    True(!html.Contains("<script>"),"title not escaped");True(html.Contains("&lt;script&gt;"),"escaped title missing");
});
Test("share server serves the player page and preview image next to the clip",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-page-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var file=Path.Combine(dir,"c.mp4");File.WriteAllBytes(file,new byte[100]);using var server=new ClipShareServer(file,"/tok/c.mp4");server.Start();using var http=new HttpClient{BaseAddress=new($"http://127.0.0.1:{server.Port}")};
        Equal(404,(int)http.GetAsync("/tok").Result.StatusCode);
        server.AttachPage(new("/tok",Encoding.UTF8.GetBytes("<html>page</html>"),"/tok/preview.jpg",[0xFF,0xD8,0xFF]));
        foreach(var path in new[]{"/tok","/tok/","/tok?x=1"}){var page=http.GetAsync(path).Result;Equal(200,(int)page.StatusCode);Equal("text/html",page.Content.Headers.ContentType!.MediaType);Equal("<html>page</html>",page.Content.ReadAsStringAsync().Result);}
        var image=http.GetAsync("/tok/preview.jpg").Result;Equal("image/jpeg",image.Content.Headers.ContentType!.MediaType);Equal(3,image.Content.ReadAsByteArrayAsync().Result.Length);
        var head=http.Send(new HttpRequestMessage(HttpMethod.Head,"/tok"));Equal(17L,head.Content.Headers.ContentLength);Equal(0,head.Content.ReadAsByteArrayAsync().Result.Length);
        Equal(100,http.GetAsync("/tok/c.mp4").Result.Content.ReadAsByteArrayAsync().Result.Length);
        foreach(var bad in new[]{"/tok/other.jpg","/TOK","/tok//"})Equal(404,(int)http.GetAsync(bad).Result.StatusCode);
        server.Publish(file,"/new/c.mp4",60);Equal(404,(int)http.GetAsync("/tok").Result.StatusCode);
    }finally{Directory.Delete(dir,true);}
});
Test("large clips get an embed copy sized for Discord",()=>{
    Equal(SharePolicy.DefaultEmbedBytes,SharePolicy.EmbedBudget(0));Equal(20L*1024*1024,SharePolicy.EmbedBudget(5*1024*1024));
    Equal(SharePolicy.MaxEmbedBytes,SharePolicy.EmbedBudget(100e6));Equal(SharePolicy.MinEmbedBytes,SharePolicy.EmbedBudget(100_000));
    True(!SharePolicy.NeedsEmbedCopy(9_500_000,SharePolicy.DefaultEmbedBytes),"small clip copied");True(SharePolicy.NeedsEmbedCopy(66_000_000,SharePolicy.DefaultEmbedBytes),"large clip not copied");
    True(!SharePolicy.NeedsEmbedCopy(20_000_000,SharePolicy.EmbedBudget(5*1024*1024)),"clip within a fast upload's budget copied");
    Equal(2_596_202,SharePolicy.EmbedVideoBitrate(TimeSpan.FromSeconds(30),SharePolicy.DefaultEmbedBytes));Equal(12_000_000,SharePolicy.EmbedVideoBitrate(TimeSpan.FromSeconds(5),SharePolicy.MaxEmbedBytes));Equal(500_000,SharePolicy.EmbedVideoBitrate(TimeSpan.FromMinutes(5),SharePolicy.MinEmbedBytes));
    Equal(720,SharePolicy.EmbedHeight(2_596_202));Equal(1080,SharePolicy.EmbedHeight(8_000_000));Equal(540,SharePolicy.EmbedHeight(1_000_000));
    Equal(0.0,SharePolicy.UpdateUploadEstimate(0,1_000_000,TimeSpan.FromSeconds(1)));Equal(5e6,SharePolicy.UpdateUploadEstimate(0,10_000_000,TimeSpan.FromSeconds(2)));Equal(4e6,SharePolicy.UpdateUploadEstimate(5e6,6_000_000,TimeSpan.FromSeconds(2)));
    Equal("/tok/embed.mp4",SharePolicy.EmbedPath("tok"));
});
Test("share page gives chat apps the embed copy and plays the original itself",()=>{
    var html=SharePage.Build(new("Clip","0:30","https://a.trycloudflare.com/tok","https://a.trycloudflare.com/tok/c.mp4","https://a.trycloudflare.com/tok/preview.jpg",1920,1080,1280,720,"https://a.trycloudflare.com/tok/embed.mp4",1280,720));
    foreach(var tag in new[]{"property=\"og:video\" content=\"https://a.trycloudflare.com/tok/embed.mp4\"","property=\"og:video:width\" content=\"1280\"","property=\"og:video:height\" content=\"720\"","<video src=\"https://a.trycloudflare.com/tok/c.mp4\""})
        True(html.Contains(tag),"missing "+tag);
});
Test("share server serves the embed copy with ranges, only while attached",()=>{
    var dir=Path.Combine(Path.GetTempPath(),"MonoClip-embed-"+Guid.NewGuid());Directory.CreateDirectory(dir);
    try{var file=Path.Combine(dir,"c.mp4");var small=Path.Combine(dir,"c.embed.mp4");File.WriteAllBytes(file,new byte[100]);File.WriteAllBytes(small,Enumerable.Range(0,40).Select(i=>(byte)i).ToArray());
        using var server=new ClipShareServer(file,"/tok/c.mp4");server.Start();using var http=new HttpClient{BaseAddress=new($"http://127.0.0.1:{server.Port}")};
        Equal(404,(int)http.GetAsync("/tok/embed.mp4").Result.StatusCode);
        server.AttachPage(new("/tok",[1],"/tok/preview.jpg",[2],"/tok/embed.mp4",small));
        var sent=new List<long>();server.Sent=(bytes,_)=>{lock(sent)sent.Add(bytes);};
        var whole=http.GetAsync("/tok/embed.mp4").Result;Equal("video/mp4",whole.Content.Headers.ContentType!.MediaType);Equal(40,whole.Content.ReadAsByteArrayAsync().Result.Length);
        var part=new HttpRequestMessage(HttpMethod.Get,"/tok/embed.mp4");part.Headers.Range=new(10,19);var r=http.Send(part);Equal(206,(int)r.StatusCode);Equal(10,(int)r.Content.ReadAsByteArrayAsync().Result[0]);
        Equal(100,http.GetAsync("/tok/c.mp4").Result.Content.ReadAsByteArrayAsync().Result.Length);
        for(int i=0;i<100;i++){lock(sent)if(sent.Count>=3)break;Thread.Sleep(10);}lock(sent)Equal("40,10,100",string.Join(",",sent));
        server.AttachPage(new("/tok",[1],"/tok/preview.jpg",[2]));Equal(404,(int)http.GetAsync("/tok/embed.mp4").Result.StatusCode);
    }finally{Directory.Delete(dir,true);}
});
Test("GPU encoder choice prefers AMD, then NVIDIA, then Intel, never the CPU",()=>{
    Equal("h264_texture_amf",CapturePolicy.PickEncoder(["obs_qsv11_v2","obs_nvenc_h264_tex","h264_texture_amf"])!.Value.Id);
    Equal("obs_nvenc_h264_tex",CapturePolicy.PickEncoder(["obs_qsv11_v2","obs_nvenc_h264_tex"])!.Value.Id);
    Equal("Intel Quick Sync · H.264 GPU",CapturePolicy.PickEncoder(["ffmpeg_aac","obs_qsv11_soft_v2","obs_qsv11_v2"])!.Value.Name);
    Equal<(string,string)?>(null,CapturePolicy.PickEncoder(["ffmpeg_aac","obs_x264","ffmpeg_svt_av1"]));
});
Test("audio for a shared clip is chosen by track name, in any order",()=>{
    string?[] clip=["Playback mix","Desktop","Microphone"],mp4=["Microphone","Desktop","Playback mix"];
    True(ClipAudio.CanSplit(clip),"split");Equal<int?>(0,ClipAudio.Choose(clip,true,true));Equal<int?>(1,ClipAudio.Choose(clip,true,false));Equal<int?>(2,ClipAudio.Choose(clip,false,true));Equal<int?>(null,ClipAudio.Choose(clip,false,false));
    Equal<int?>(2,ClipAudio.Choose(mp4,true,true));Equal<int?>(1,ClipAudio.Choose(mp4,true,false));Equal<int?>(0,ClipAudio.Choose(mp4,false,true));Equal<int?>(2,ClipAudio.DefaultTrack(mp4));
    string?[] desktopOnly=["Desktop","Playback mix"],unnamed=[null,null];
    True(!ClipAudio.CanSplit(desktopOnly),"no split without a microphone track");Equal("Desktop audio",ClipAudio.SingleLabel(desktopOnly));Equal<int?>(1,ClipAudio.Choose(desktopOnly,true,true));Equal<int?>(null,ClipAudio.Choose(desktopOnly,false,false));
    Equal("Microphone",ClipAudio.SingleLabel(["Playback mix","Microphone"]));Equal("Audio",ClipAudio.SingleLabel(unnamed));Equal<int?>(0,ClipAudio.Choose(unnamed,true,true));Equal<int?>(null,ClipAudio.Choose([],true,true));
});
Test("clip edits know when they change the clip and estimate the shared size",()=>{
    var length=TimeSpan.FromSeconds(90);
    True(!new ClipEdit(TimeSpan.Zero,length,0).ChangesClip(length,0),"whole clip with the mix changes nothing");
    True(new ClipEdit(TimeSpan.FromSeconds(5),length,0).ChangesClip(length,0),"trimmed start");True(new ClipEdit(TimeSpan.Zero,TimeSpan.FromSeconds(60),0).ChangesClip(length,0),"trimmed end");
    True(new ClipEdit(TimeSpan.Zero,length,2).ChangesClip(length,0),"other track");True(new ClipEdit(TimeSpan.Zero,length,null).ChangesClip(length,0),"muted");
    Equal(30_000_000L,new ClipEdit(TimeSpan.FromSeconds(30),TimeSpan.FromSeconds(60)).EstimateBytes(90_000_000,length));
});
// NEXT_TEST
Console.WriteLine($"RESULT {count - failures}/{count} passed");
return failures == 0 && count > 0 ? 0 : 1;

static class WindowsArgv
{
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern IntPtr CommandLineToArgvW(string line, out int count);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    public static string[] Parse(string line)
    {
        var argv = CommandLineToArgvW(line, out int count);
        try { return Enumerable.Range(0, count).Select(i => System.Runtime.InteropServices.Marshal.PtrToStringUni(System.Runtime.InteropServices.Marshal.ReadIntPtr(argv, i * IntPtr.Size))!).ToArray(); }
        finally { LocalFree(argv); }
    }
}
static class Mp4Fixture
{
    static byte[] Box(string type,params byte[][] parts){var body=parts.SelectMany(p=>p).ToArray();var b=new byte[8+body.Length];BinaryPrimitives.WriteUInt32BigEndian(b,(uint)b.Length);Encoding.ASCII.GetBytes(type).CopyTo(b,4);body.CopyTo(b,8);return b;}
    static byte[] Offsets(bool wide,params long[] values){var b=new byte[8+values.Length*(wide?8:4)];BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(4),(uint)values.Length);for(int i=0;i<values.Length;i++)if(wide)BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(8+i*8),(ulong)values[i]);else BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(8+i*4),(uint)values[i]);return b;}
    // ftyp + mdat(payload) + moov{trak{mdia{minf{stbl{stco|co64}}}}}; chunks at payload[0] and payload[50].
    public static byte[] File(byte[] payload,bool wide,bool moovFirst)
    {
        var ftyp=Box("ftyp",Encoding.ASCII.GetBytes("isom"),new byte[4]);byte[] Moov(long mdatAt)=>Box("moov",Box("trak",Box("mdia",Box("minf",Box("stbl",Box(wide?"co64":"stco",Offsets(wide,mdatAt+8,mdatAt+58)))))));
        if(!moovFirst)return[..ftyp,..Box("mdat",payload),..Moov(ftyp.Length)];
        var moovSize=Moov(0).Length;return[..ftyp,..Moov(ftyp.Length+moovSize),..Box("mdat",payload)];
    }
    // moov{mvhd, trak{tkhd(w,h)}...}+mdat, for Mp4Info.
    public static byte[] WithTracks(uint timescale,uint length,params (int W,int H)[] tracks)
    {
        var mvhd=new byte[100];BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(12),timescale);BinaryPrimitives.WriteUInt32BigEndian(mvhd.AsSpan(16),length);
        var traks=tracks.Select(t=>{var tkhd=new byte[84];BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(76),(uint)t.W<<16);BinaryPrimitives.WriteUInt32BigEndian(tkhd.AsSpan(80),(uint)t.H<<16);return Box("trak",Box("tkhd",tkhd));}).ToArray();
        return[..Box("ftyp",Encoding.ASCII.GetBytes("isom"),new byte[4]),..Box("moov",[Box("mvhd",mvhd),..traks]),..Box("mdat",new byte[16])];
    }
    public static int IndexOf(byte[] data,string type)=>data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(type));
    public static long[] ChunkOffsets(byte[] data,bool wide){int at=IndexOf(data,wide?"co64":"stco")+4;int count=(int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at+4));return Enumerable.Range(0,count).Select(i=>wide?(long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(at+8+i*8)):BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at+8+i*4))).ToArray();}
}
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
