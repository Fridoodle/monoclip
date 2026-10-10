using MonoClip.Windows.Capture;
using System.Runtime.InteropServices;
class Program {
[STAThread] static int Main(string[] args) {
Console.OutputEncoding = System.Text.Encoding.UTF8; Console.InputEncoding = System.Text.Encoding.UTF8;
try {
    if(args.Contains("--monitor-alias")) {
        var method=typeof(ForegroundTracker).GetMethod("StableMonitorId",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)??throw new Exception("stable monitor fallback missing");
        var name=Screen.PrimaryScreen!.DeviceName;if((string)method.Invoke(null,new object[]{"",name})! != name)throw new Exception("display alias lost on missing device GUID");
        if((string)method.Invoke(null,new object[]{"a-device-guid",name})! != "a-device-guid")throw new Exception("valid device id was replaced");Console.WriteLine("PASS stable Windows display alias for missing monitor GUID");return 0;
    }
    if(args.Contains("--exports")) {
        var handle=NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,"obs.dll"));
        try {var missing=new List<string>();int count=0;foreach(var m in typeof(Obs).GetMethods(System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)){var attr=m.GetCustomAttributes(typeof(DllImportAttribute),false).Cast<DllImportAttribute>().FirstOrDefault();if(attr?.Value!="obs.dll")continue;count++;var symbol=attr.EntryPoint??m.Name;if(!NativeLibrary.TryGetExport(handle,symbol,out _))missing.Add(symbol);}if(missing.Count>0)throw new Exception("Missing native exports: "+string.Join(",",missing));Console.WriteLine("PASS all "+count+" libobs imports resolve in shipped DLL");return 0;}finally{NativeLibrary.Free(handle);}
    }
    if(args.Contains("--properties")) {
        using var native=new ObsHost();native.Initialize(1280,720,30);
        using var data=new ObsData().Set("monitor_id","DUMMY");var source=Obs.obs_source_create("monitor_capture","Properties fallback regression",data.Handle,IntPtr.Zero);
        try {var props=Obs.obs_source_properties(source);if(props==IntPtr.Zero)throw new Exception("monitor properties are missing");try{var list=Obs.obs_properties_get(props,"monitor_id");if(list==IntPtr.Zero||Obs.obs_property_list_item_count(list)<1)throw new Exception("monitor picker is empty");Console.WriteLine("PASS real monitor fallback properties API");}finally{Obs.obs_properties_destroy(props);}}finally{Obs.obs_source_release(source);}return 0;
    }
    if(args.Contains("--share-remux")||args.Contains("--share-live")) {
        // Synthetic game-fixture clip only: nothing from the real desktop leaves the PC.
        var fixtureClips=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MonoClip","GameVerificationClips");
        var source=Directory.Exists(fixtureClips)?MonoClip.Core.ClipLibrary.LatestClip(Path.Combine(fixtureClips,"MonoClip.GameFixture")):null;
        if(source==null)throw new Exception("run --game first to create a synthetic fixture clip");
        if(args.Contains("--share-remux")) {
            var target=Path.Combine(Path.GetTempPath(),"MonoClip-remux-"+Guid.NewGuid()+".mp4");
            try {
                MonoClip.Windows.Share.ClipRemux.ToFastStartMp4(source,target);
                var bytes=File.ReadAllBytes(target);int moov=bytes.AsSpan().IndexOf("moov"u8),mdat=bytes.AsSpan().IndexOf("mdat"u8);
                if(bytes.Length<10000||moov<0||mdat<0||moov>mdat)throw new Exception($"not a fast-start MP4: size={bytes.Length} moov={moov} mdat={mdat}");
                if(File.Exists(target+".remux.mp4"))throw new Exception("intermediate remux file left behind");
                var probe=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffprobe",$"-v error -show_entries stream=codec_name -of csv=p=0 \"{target}\""){RedirectStandardOutput=true,UseShellExecute=false})!;var codecs=probe.StandardOutput.ReadToEnd().Split((char)10,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);probe.WaitForExit();
                if(codecs.FirstOrDefault()!="h264"||!codecs.Skip(1).All(c=>c=="aac")||codecs.Length<2)throw new Exception("unexpected streams: "+string.Join(",",codecs));
                Console.WriteLine("PASS real libobs remux to fast-start MP4 "+new FileInfo(target).Length+" bytes streams="+string.Join(",",codecs));return 0;
            } finally { if(File.Exists(target))File.Delete(target); }
        }
        using var share=new MonoClip.Windows.Share.ClipShare();var steps=new List<string>();
        var task=share.ShareAsync(source,TimeSpan.FromMinutes(5),new Progress<string>(steps.Add));var clock=System.Diagnostics.Stopwatch.StartNew();
        while(!task.IsCompleted&&clock.Elapsed.TotalSeconds<180){Application.DoEvents();Thread.Sleep(20);}
        var info=task.GetAwaiter().GetResult();Console.WriteLine($"TIME link ready after {clock.Elapsed.TotalSeconds:0.0} s");Console.WriteLine("INFO "+string.Join(" | ",steps)+" | "+info.Url);
        if(!info.Url.StartsWith("https://")||!info.Url.Contains(".trycloudflare.com/")||(info.ExpiresAt-DateTimeOffset.Now).TotalMinutes is <4.9 or >5.01)throw new Exception("unexpected share "+info);
        var local=Directory.EnumerateFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MonoClip","share"),"*.mp4").Single();var expected=File.ReadAllBytes(local);
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(60)};
        var got=http.GetAsync(info.Url).GetAwaiter().GetResult();var body=got.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        if((int)got.StatusCode!=200||got.Content.Headers.ContentType?.MediaType!="video/mp4"||!body.SequenceEqual(expected)){var tun=(MonoClip.Windows.Share.CloudflaredTunnel?)typeof(MonoClip.Windows.Share.ClipShare).GetField("tunnel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(share);throw new Exception($"public link did not return the clip: {(int)got.StatusCode} {body.Length}/{expected.Length} {System.Text.Encoding.ASCII.GetString(body.Take(40).ToArray())} | "+string.Join(" | ",tun?.RecentLog.TakeLast(8)??["no tunnel"]));}
        var range=new HttpRequestMessage(HttpMethod.Get,info.Url);range.Headers.Range=new(0,1023);var part=http.Send(range);if((int)part.StatusCode!=206)throw new Exception("range through tunnel: "+(int)part.StatusCode);
        var guess=http.GetAsync(info.Url.Replace(info.Url.Split('/')[3],SharePolicyToken())).GetAwaiter().GetResult();if((int)guess.StatusCode!=404)throw new Exception("wrong token served: "+(int)guess.StatusCode);
        share.Stop();Application.DoEvents();
        if(share.Current!=null||File.Exists(local))throw new Exception("stop left the share or its copy behind");
        Thread.Sleep(3000);int after;try{after=(int)http.GetAsync(info.Url).GetAwaiter().GetResult().StatusCode;}catch(HttpRequestException){after=-1;}
        if(after==200)throw new Exception("link still serves the clip after stop");
        Console.WriteLine($"PASS live Cloudflare quick tunnel served {body.Length} bytes publicly, range 206, wrong token 404, offline after stop (status {after})");return 0;
        static string SharePolicyToken()=>MonoClip.Core.SharePolicy.NewToken();
    }
    if(args.Any(a => a is "--restart" or "--timeout" or "--stale" or "--stop-save" or "--dispose-save" or "--export-drain" or "--target" or "--rapid-save" or "--export-error")) return RegressionTests.Run(args);
    if(args.Contains("--game")) {
        var fixture=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../tests/MonoClip.GameFixture/bin/Release/net10.0-windows/MonoClip.GameFixture.exe"));
        if(!File.Exists(fixture))throw new Exception("Direct3D game fixture missing");
        using var game=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fixture,args.Contains("--exclusive")?"--exclusive":""){UseShellExecute=false})!;
        try {
            // Capture follows the mouse: put the cursor over the fixture like a player would.
            for(int i=0;i<100&&game.MainWindowHandle==IntPtr.Zero;i++){Thread.Sleep(50);game.Refresh();}
            var bounds=Screen.FromHandle(game.MainWindowHandle).Bounds;Cursor.Position=new System.Drawing.Point(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
            using var engine=new ClipEngine();var s=new MonoClip.Core.AppSettings{ClipSeconds=5,DesktopAudio=false,Microphone=false,ClipsDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MonoClip","GameVerificationClips")};
            MonoClip.Core.ClipRecord? clip=null;engine.ClipSaved+=(_,e)=>clip=e.Clip;engine.Start(s);var clock=System.Diagnostics.Stopwatch.StartNew();bool saved=false;
            while(clock.Elapsed.TotalSeconds<20&&clip==null){Application.DoEvents();Thread.Sleep(25);if(clock.Elapsed.TotalSeconds>10&&!saved){if(!engine.CaptureTarget.StartsWith("Game"))throw new Exception("Game not auto-detected: "+engine.CaptureTarget+" "+engine.Status);engine.SaveClip();saved=true;}}
            if(clip==null||clip.Context.CaptureMode!="game")throw new Exception("game clip was not indexed as game");
            Console.WriteLine("PASS automatic "+(args.Contains("--exclusive")?"exclusive fullscreen":"borderless")+" game clip "+clip.Path+" frames="+engine.TotalFrames+" drops="+engine.DroppedFrames);engine.Stop();return 0;
        }finally{if(!game.HasExited)game.Kill();}
    }
    if(args.Contains("--failure")) {
        var path=Path.Combine(Path.GetTempPath(),"MonoClip-failure-"+Guid.NewGuid());Directory.CreateDirectory(path);File.WriteAllText(Path.Combine(path,".pending"),"blocking fixture");
        try {using var engine=new ClipEngine();try{engine.Start(new MonoClip.Core.AppSettings{ClipsDirectory=path});throw new Exception("blocked export path accepted");}catch(IOException){if(engine.IsRunning)throw new Exception("failed start leaked active capture");Console.WriteLine("PASS failed startup releases GPU sources and encoders");return 0;}}finally{Directory.Delete(path,true);}
    }
    if(args.Contains("--capture")||args.Contains("--benchmark")) {
        using var engine=new MonoClip.Windows.Capture.ClipEngine();
        bool benchmark=args.Contains("--benchmark"),fast=args.Contains("--fps120"),hd=args.Contains("--hd30")||fast;var settings=new MonoClip.Core.AppSettings {ClipSeconds=benchmark?30:5,Width=hd?1280:1920,Height=hd?720:1080,Fps=fast?120:hd?30:60,ClipsDirectory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MonoClip","VerificationClips")};
        MonoClip.Core.ClipRecord? result=null;engine.ClipSaved+=(_,e)=>result=e.Clip;
        engine.Start(settings);if(!engine.IsRunning)throw new Exception("buffer did not start");
        var clock=System.Diagnostics.Stopwatch.StartNew();bool saved=false;
        while(clock.Elapsed.TotalSeconds<(benchmark?50:22)&&result==null) {Application.DoEvents();Thread.Sleep(30);if(clock.Elapsed.TotalSeconds>(benchmark?38:8)&&!saved){engine.SaveClip();saved=true;}}
        if(result==null||!File.Exists(result.Path))throw new Exception("replay save failed: "+engine.Status);
        if(engine.TotalFrames<settings.Fps*5)throw new Exception("insufficient frames: "+engine.TotalFrames);
        Console.WriteLine("PASS real replay "+result.Path+" target="+engine.CaptureTarget+" mode="+result.Context.CaptureMode+" encoder="+engine.EncoderName+" frames="+engine.TotalFrames+" dropped="+engine.DroppedFrames);
        int before=engine.TotalFrames;engine.Apply(settings with{Hotkey="Ctrl+Shift+F10"});if(engine.TotalFrames<before)throw new Exception("non-capture settings discarded replay buffer");
        engine.Stop();if(engine.IsRunning)throw new Exception("stop did not stop");
        return 0;
    }
    static string ReadLog(){using var s=new FileStream(ObsHost.LogPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return new StreamReader(s).ReadToEnd();}
    int logStart=File.Exists(ObsHost.LogPath)?ReadLog().Length:0;
    using var host = new ObsHost();
    host.Initialize(1920, 1080, 60);
    Application.DoEvents();Thread.Sleep(300);if(ReadLog()[logStart..].Contains("source graphics-hook"))throw new Exception("game hook runtime asset resolution failed");
    if (!host.Encoders.Contains("h264_texture_amf")) throw new Exception("AMD texture encoder not available");
    if (!host.Sources.Contains("monitor_capture")) throw new Exception("GPU monitor capture not available");
    if (!host.Sources.Contains("game_capture")) throw new Exception("Game capture not available");
    if (!host.Sources.Contains("wasapi_input_capture")) throw new Exception("Mic capture not available");
    Console.WriteLine("PASS native bootstrap AMD texture encoder + game/desktop/mic capture");
    Console.WriteLine(string.Join(", ", host.Encoders));
    return 0;
} catch(Exception e) { Console.Error.WriteLine("FAIL " + e); return 1; }
}}
