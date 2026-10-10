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
            while(clock.Elapsed.TotalSeconds<20&&clip==null){Application.DoEvents();Thread.Sleep(25);if(clock.Elapsed.TotalSeconds>10&&!saved){if(!engine.CaptureTarget.StartsWith("Spiel"))throw new Exception("Game not auto-detected: "+engine.CaptureTarget+" "+engine.Status);engine.SaveClip();saved=true;}}
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
    int logStart=File.Exists(ObsHost.LogPath)?File.ReadAllText(ObsHost.LogPath).Length:0;
    using var host = new ObsHost();
    host.Initialize(1920, 1080, 60);
    Application.DoEvents();Thread.Sleep(300);if(File.ReadAllText(ObsHost.LogPath)[logStart..].Contains("source graphics-hook"))throw new Exception("game hook runtime asset resolution failed");
    if (!host.Encoders.Contains("h264_texture_amf")) throw new Exception("AMD texture encoder not available");
    if (!host.Sources.Contains("monitor_capture")) throw new Exception("GPU monitor capture not available");
    if (!host.Sources.Contains("game_capture")) throw new Exception("Game capture not available");
    if (!host.Sources.Contains("wasapi_input_capture")) throw new Exception("Mic capture not available");
    Console.WriteLine("PASS native bootstrap AMD texture encoder + game/desktop/mic capture");
    Console.WriteLine(string.Join(", ", host.Encoders));
    return 0;
} catch(Exception e) { Console.Error.WriteLine("FAIL " + e); return 1; }
}}
