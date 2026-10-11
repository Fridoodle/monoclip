using MonoClip.Windows.Capture;
using System.Runtime.InteropServices;
class Program {
[STAThread] static int Main(string[] args) {
Console.OutputEncoding = System.Text.Encoding.UTF8; Console.InputEncoding = System.Text.Encoding.UTF8;
try {
    if(args.Contains("--trim-file")) {
        // Diagnostics: trim any file (start, end in seconds, audio track or -1) and describe the result.
        int at=Array.IndexOf(args,"--trim-file");var input=args[at+1];var output=Path.Combine(Path.GetTempPath(),"MonoClip-trim-file.mp4");int track=int.Parse(args[at+4]);
        var media=MonoClip.Windows.Share.ClipTrim.Probe(input);Console.WriteLine("INFO "+media.Duration+" tracks: "+string.Join(" | ",media.AudioTracks.Select(n=>n??"-")));
        MonoClip.Windows.Share.ClipTrim.Create(input,output,new(TimeSpan.FromSeconds(double.Parse(args[at+2],System.Globalization.CultureInfo.InvariantCulture)),TimeSpan.FromSeconds(double.Parse(args[at+3],System.Globalization.CultureInfo.InvariantCulture)),track<0?null:track));
        Console.WriteLine(Run("ffprobe",$"-v error -show_entries stream=codec_type,codec_name,start_time,duration -of compact \"{output}\""));return 0;
    }
    if(args.Contains("--player")) {
        // The share editor's player: real MFPlay video in a window, seeking while paused, playback, audio switching.
        var dir=Path.Combine(Path.GetTempPath(),"MonoClip-player-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        try {
            var mkv=Path.Combine(dir,"clip.mkv");TestClip(mkv);var media=MonoClip.Windows.Share.ClipTrim.Probe(mkv);
            using var form=new Form{Text="MonoClip player test",ClientSize=new Size(640,360),StartPosition=FormStartPosition.CenterScreen,TopMost=true};var view=new Panel{Dock=DockStyle.Fill,BackColor=Color.Black};form.Controls.Add(view);form.Show();
            void Pump(int ms){var w=System.Diagnostics.Stopwatch.StartNew();while(w.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(15);}}
            using var player=new MonoClip.Windows.UI.ClipPlayer(mkv,view.Handle,media.AudioStreams,media.AudioStreams[0]);
            player.Muted=true;player.Play();for(int i=0;i<40&&!player.IsPlaying;i++)Pump(50);player.Pause();player.Muted=false;player.Position=TimeSpan.FromSeconds(3);Pump(500);
            var paused=player.Position;if(Math.Abs(paused.TotalSeconds-3)>0.2||player.IsPlaying)throw new Exception($"seek while paused: {paused} playing={player.IsPlaying}");
            Bitmap Shot(){var b=new Bitmap(view.Width,view.Height);using(var g=Graphics.FromImage(b))g.CopyFromScreen(view.PointToScreen(Point.Empty),Point.Empty,view.Size);return b;}
            int Colored(Bitmap b){int n=0;for(int x=0;x<b.Width;x+=8)for(int y=0;y<b.Height;y+=8){var c=b.GetPixel(x,y);if(Math.Max(c.R,Math.Max(c.G,c.B))>60)n++;}return n;}
            int lit=0;for(int i=0;i<20&&lit<3000;i++){player.UpdateVideo();Pump(100);using var shot=Shot();lit=Colored(shot);}if(lit<3000)throw new Exception("no video frame shown while paused ("+lit+" of 3600 samples lit)");
            player.Play();Pump(1200);using(var shot=Shot())if(Colored(shot)<3000)throw new Exception("no video while playing");var playing=player.Position;if(!player.IsPlaying||playing.TotalSeconds<3.6)throw new Exception($"playback did not advance: {playing} playing={player.IsPlaying}");
            player.SelectAudio(media.AudioStreams[2]);Pump(500);player.SelectAudio(null);Pump(500);if(player.Position<playing)throw new Exception("switching audio lost the position: "+player.Position);
            player.Pause();Pump(300);if(player.IsPlaying)throw new Exception("pause failed");
            Console.WriteLine($"PASS clip player: frame shown at {paused.TotalSeconds:0.0} s while paused, played to {playing.TotalSeconds:0.0} s, audio switched to microphone and off at {player.Position.TotalSeconds:0.0} s");return 0;
        } finally { try{Directory.Delete(dir,true);}catch(IOException){} }
    }
    if(args.Contains("--trim")) {
        // Lossless trim of a synthetic MonoClip-like MKV: 720p30 with a keyframe per second and three named AAC
        // tracks at different pitches (mix 440 Hz, desktop 660 Hz, microphone 880 Hz) to tell them apart.
        var dir=Path.Combine(Path.GetTempPath(),"MonoClip-trim-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        try {
            var mkv=Path.Combine(dir,"clip.mkv");
            TestClip(mkv);
            var media=MonoClip.Windows.Share.ClipTrim.Probe(mkv);
            if(Math.Abs(media.Duration.TotalSeconds-6)>0.2)throw new Exception("probe duration "+media.Duration);
            Console.WriteLine("INFO probe "+media.Duration.TotalSeconds.ToString("0.00")+" s, tracks: "+string.Join(" | ",media.AudioTracks.Select(n=>n??"(no name)")));
            if(media.AudioTracks.Count!=3)throw new Exception("expected 3 audio tracks");
            var remuxed=Path.Combine(dir,"clip.mp4");MonoClip.Windows.Share.ClipRemux.ToFastStartMp4(mkv,remuxed);
            {var embed=Path.Combine(dir,"embed.mp4");MonoClip.Windows.Share.ClipEmbedCopy.Create(remuxed,embed,TimeSpan.FromSeconds(6),MonoClip.Core.SharePolicy.MinEmbedBytes);var tone=Path.Combine(dir,"e.raw");Run("ffmpeg",$"-v error -y -i \"{embed}\" -map 0:a:0 -ac 1 -ar 48000 -f s16le \"{tone}\"");var pcm=File.ReadAllBytes(tone);int c=0;for(int k=2;k+1<pcm.Length;k+=2)if(BitConverter.ToInt16(pcm,k-2)<0&&BitConverter.ToInt16(pcm,k)>=0)c++;double hz=c/(pcm.Length/2/48000.0);if(Math.Abs(hz-440)>40)throw new Exception($"embed copy plays {hz:0} Hz, not the playback mix");Console.WriteLine($"PASS embed copy keeps the playback mix ({hz:0} Hz)");}
            var mp4Tracks=MonoClip.Windows.Share.ClipTrim.Probe(remuxed).AudioTracks;Console.WriteLine("INFO mp4 tracks as Windows lists them: "+string.Join(" | ",mp4Tracks.Select(n=>n??"-")));
            // Tracks are picked by name (Windows lists MP4 audio tracks in another order than the file).
            foreach(var (input,label,track,tone,name) in new[]{(mkv,"mkv",MonoClip.Core.ClipAudio.Choose(media.AudioTracks,false,true),880,"microphone"),(remuxed,"remuxed mp4",MonoClip.Core.ClipAudio.Choose(mp4Tracks,true,false),660,"desktop")}){
                var output=Path.Combine(dir,"trim-"+label.Replace(' ','-')+".mp4");var timer=System.Diagnostics.Stopwatch.StartNew();
                MonoClip.Windows.Share.ClipTrim.Create(input,output,new(TimeSpan.FromSeconds(1.5),TimeSpan.FromSeconds(4.2),track));timer.Stop();
                var streams=Run("ffprobe",$"-v error -show_entries stream=codec_type,codec_name,start_time,duration -of csv=p=0 \"{output}\"").Trim().Replace("\r","").Split('\n');
                if(streams.Length!=2||!streams[0].StartsWith("h264,video")||!streams[1].StartsWith("aac,audio"))throw new Exception($"{label}: unexpected streams {string.Join(" ; ",streams)}");
                var video=streams[0].Split(',');double start=double.Parse(video[2],System.Globalization.CultureInfo.InvariantCulture),length=double.Parse(video[3],System.Globalization.CultureInfo.InvariantCulture);
                // Starts on the keyframe at 1.0 s, ends before 4.2 s: about 3.2 s.
                if(Math.Abs(start)>0.05||Math.Abs(length-3.2)>0.1)throw new Exception($"{label}: video start {start} length {length}");
                var first=Run("ffprobe",$"-v error -select_streams v -read_intervals %+#1 -show_entries frame=key_frame -of csv=p=0 \"{output}\"").Trim();if(!first.StartsWith("1"))throw new Exception(label+": first frame is not a keyframe");
                var pcm=Path.Combine(dir,"a.raw");Run("ffmpeg",$"-v error -y -i \"{output}\" -map 0:a -ac 1 -ar 48000 -f s16le \"{pcm}\"");
                var samples=File.ReadAllBytes(pcm);int crossings=0;for(int i=2;i+1<samples.Length;i+=2){short a=BitConverter.ToInt16(samples,i-2),b=BitConverter.ToInt16(samples,i);if(a<0&&b>=0)crossings++;}
                double pitch=crossings/(samples.Length/2/48000.0);if(Math.Abs(pitch-tone)>40)throw new Exception($"{label}: audio is {pitch:0} Hz, expected the {name} track ({tone} Hz)");
                var bytes=File.ReadAllBytes(output);int moov=bytes.AsSpan().IndexOf("moov"u8),mdat=bytes.AsSpan().IndexOf("mdat"u8);if(moov<0||moov>mdat)throw new Exception(label+": not fast-start");
                Console.WriteLine($"PASS lossless trim from {label}: 1.5-4.2 s -> {length:0.00} s from keyframe 1.0 s, {name} track ({pitch:0} Hz), {bytes.Length/1024} KB in {timer.ElapsedMilliseconds} ms");
            }
            var silent=Path.Combine(dir,"silent.mp4");MonoClip.Windows.Share.ClipTrim.Create(remuxed,silent,new(TimeSpan.Zero,TimeSpan.FromSeconds(6),null));
            var only=Run("ffprobe",$"-v error -show_entries stream=codec_type -of csv=p=0 \"{silent}\"").Trim();if(only!="video")throw new Exception("muted copy still has audio: "+only);
            Console.WriteLine("PASS muted copy has no audio track");
            // A real OBS recording: its MKV describes AAC differently (no sample size), which once dropped the audio.
            var fixtures=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MonoClip","GameVerificationClips","MonoClip.GameFixture");
            if(Directory.Exists(fixtures)&&MonoClip.Core.ClipLibrary.LatestClip(fixtures) is {} recorded){
                var cut=Path.Combine(dir,"recorded.mp4");MonoClip.Windows.Share.ClipTrim.Create(recorded,cut,new(TimeSpan.FromSeconds(1.5),TimeSpan.FromSeconds(4),0));
                var kinds=Run("ffprobe",$"-v error -show_entries stream=codec_type -of csv=p=0 \"{cut}\"").Trim().Replace("\r","").Replace("\n",",");if(kinds!="video,audio")throw new Exception("trimmed OBS recording has streams: "+kinds);
                Console.WriteLine("PASS lossless trim of a real OBS recording keeps its audio");
            } else Console.WriteLine("SKIP real OBS recording (run --game first)");
            return 0;
        } finally { Directory.Delete(dir,true); }
    }
    if(args.Contains("--share-throttled")) {
        // Diagnostics: is Discord's embed limit a size or a download time? Serves one MP4 with a player page
        // through a tunnel per given rate (KB/s); a loopback proxy slows each response down to that rate.
        int at=Array.IndexOf(args,"--share-throttled"),seconds=int.Parse(args[at+1]);var file=args[at+2];var rates=args.Skip(at+3).Select(int.Parse).ToArray();
        var cleanup=new List<IDisposable>();var name=Path.GetFileNameWithoutExtension(file);var facts=MonoClip.Core.Mp4Info.Read(file)!;
        var jpeg=MonoClip.Windows.Share.ClipThumbnail.CreateJpegAsync(file,1280,720,name).GetAwaiter().GetResult();
        try {
            foreach(var rate in rates){
                var token=MonoClip.Core.SharePolicy.NewToken();var path=MonoClip.Core.SharePolicy.RequestPath(token,file);
                var srv=new MonoClip.Core.ClipShareServer(file,path);srv.Start();cleanup.Add(srv);var label=$"{name}@{rate}KB/s";srv.Log=line=>Console.WriteLine($"{DateTime.Now:HH:mm:ss} [{label}] {line}");
                var proxy=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0);proxy.Start();int target=srv.Port;
                _=Task.Run(async()=>{while(true){var c=await proxy.AcceptTcpClientAsync();_=Task.Run(async()=>{using var client=c;using var upstream=new System.Net.Sockets.TcpClient();await upstream.ConnectAsync(System.Net.IPAddress.Loopback,target);
                    var a=client.GetStream();var b=upstream.GetStream();var up=a.CopyToAsync(b);
                    var down=Task.Run(async()=>{var buf=new byte[16384];long sent=0;var clock=System.Diagnostics.Stopwatch.StartNew();int n;
                        try{while((n=await b.ReadAsync(buf))>0){await a.WriteAsync(buf.AsMemory(0,n));sent+=n;var due=TimeSpan.FromSeconds(sent/1024.0/rate)-clock.Elapsed;if(due>TimeSpan.Zero)await Task.Delay(due);}}catch(IOException){}});
                    try{await Task.WhenAny(up,down);await down;}catch{}});}});
                var tunnel=MonoClip.Windows.Share.CloudflaredTunnel.StartAsync(((System.Net.IPEndPoint)proxy.LocalEndpoint).Port,null,CancellationToken.None).GetAwaiter().GetResult();cleanup.Add(tunnel);
                var origin=tunnel.PublicUrl;var page=new MonoClip.Core.SharePageInfo(name,MonoClip.Core.SharePage.Describe(facts),origin+"/"+token,origin+path,origin+MonoClip.Core.SharePolicy.ImagePath(token),facts.Width,facts.Height,1280,720);
                srv.AttachPage(new(MonoClip.Core.SharePolicy.PagePath(token),System.Text.Encoding.UTF8.GetBytes(MonoClip.Core.SharePage.Build(page)),MonoClip.Core.SharePolicy.ImagePath(token),jpeg));
                Console.WriteLine($"LINK {label} ~{new FileInfo(file).Length/1024/rate}s {page.PageUrl}");
            }
            var hold=System.Diagnostics.Stopwatch.StartNew();while(hold.Elapsed.TotalSeconds<seconds)Thread.Sleep(200);
            Console.WriteLine("DONE");return 0;
        } finally { foreach(var d in cleanup)d.Dispose(); }
    }
    if(args.Contains("--share-many")) {
        // Diagnostics for chat embed limits: share each given MP4 through its own tunnel with a player page whose
        // og:video is the file itself (no embed copy), print the links and log every request for N seconds.
        int at=Array.IndexOf(args,"--share-many"),seconds=int.Parse(args[at+1]);var files=args.Skip(at+2).ToArray();// All instances first: a new ClipShare clears copies left in the share folder.
        var shares=files.Select(_=>new MonoClip.Windows.Share.ClipShare()).ToList();
        try {
            for(int i=0;i<files.Length;i++){
                var file=files[i];var share=shares[i];var name=Path.GetFileNameWithoutExtension(file);
                var sharing=share.ShareAsync(file,TimeSpan.FromMinutes(Math.Max(5,(seconds+59)/60)),null,new(true,name));while(!sharing.IsCompleted){Application.DoEvents();Thread.Sleep(20);}
                var video=new Uri(sharing.GetAwaiter().GetResult().Url);var origin=video.GetLeftPart(UriPartial.Authority);var token=video.AbsolutePath.Split('/')[1];
                var srv=(MonoClip.Core.ClipShareServer)typeof(MonoClip.Windows.Share.ClipShare).GetField("server",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(share)!;
                var facts=MonoClip.Core.Mp4Info.Read(file)!;var jpeg=MonoClip.Windows.Share.ClipThumbnail.CreateJpegAsync(file,1280,720,name).GetAwaiter().GetResult();
                var page=new MonoClip.Core.SharePageInfo(name,MonoClip.Core.SharePage.Describe(facts),origin+"/"+token,video.ToString(),origin+MonoClip.Core.SharePolicy.ImagePath(token),facts.Width,facts.Height,1280,720);
                srv.AttachPage(new(MonoClip.Core.SharePolicy.PagePath(token),System.Text.Encoding.UTF8.GetBytes(MonoClip.Core.SharePage.Build(page)),MonoClip.Core.SharePolicy.ImagePath(token),jpeg));
                srv.Log=line=>Console.WriteLine($"{DateTime.Now:HH:mm:ss} [{name}] {line}");Console.WriteLine($"LINK {name} {new FileInfo(file).Length/1024/1024} MB {page.PageUrl}");
            }
            var hold=System.Diagnostics.Stopwatch.StartNew();while(hold.Elapsed.TotalSeconds<seconds){Application.DoEvents();Thread.Sleep(50);}
            Console.WriteLine("DONE");return 0;
        } finally { foreach(var s in shares)s.Dispose(); }
    }
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
    if(args.Contains("--share-remux")||args.Contains("--share-live")||args.Contains("--share-preview")||args.Contains("--share-hold")||args.Contains("--share-embed")) {
        // Synthetic game-fixture clip only: nothing from the real desktop leaves the PC.
        var fixtureClips=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MonoClip","GameVerificationClips");
        var source=Directory.Exists(fixtureClips)?MonoClip.Core.ClipLibrary.LatestClip(Path.Combine(fixtureClips,"MonoClip.GameFixture")):null;
        if(source==null)throw new Exception("run --game first to create a synthetic fixture clip");
        if(args.Contains("--share-hold")) {
            // Diagnostics: share the synthetic clip, print the link and log every request for N seconds.
            int seconds=int.Parse(args[Array.IndexOf(args,"--share-hold")+1]);using var held=new MonoClip.Windows.Share.ClipShare();
            if(args.Length>Array.IndexOf(args,"--share-hold")+2&&File.Exists(args[Array.IndexOf(args,"--share-hold")+2]))source=args[Array.IndexOf(args,"--share-hold")+2];
            var sharing=held.ShareAsync(source,TimeSpan.FromMinutes(Math.Max(5,(seconds+59)/60)),null,new(false,"Test clip"));while(!sharing.IsCompleted){Application.DoEvents();Thread.Sleep(20);}
            var link=sharing.GetAwaiter().GetResult();var srv=(MonoClip.Core.ClipShareServer)typeof(MonoClip.Windows.Share.ClipShare).GetField("server",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(held)!;
            srv.Log=line=>Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");Console.WriteLine("LINK "+link.Url);
            var hold=System.Diagnostics.Stopwatch.StartNew();while(hold.Elapsed.TotalSeconds<seconds){Application.DoEvents();Thread.Sleep(50);}
            Console.WriteLine("DONE");return 0;
        }
        if(args.Contains("--share-embed")) {
            // Embed copy from a real remuxed clip (or a given MP4, optionally with a budget in MB): H.264 + AAC at most 30 fps, fast-start, near the budget.
            var given=args.Length>Array.IndexOf(args,"--share-embed")+1?args[Array.IndexOf(args,"--share-embed")+1]:null;
            long budget=args.Length>Array.IndexOf(args,"--share-embed")+2?long.Parse(args[Array.IndexOf(args,"--share-embed")+2])<<20:MonoClip.Core.SharePolicy.DefaultEmbedBytes;
            var remuxed=Path.Combine(Path.GetTempPath(),"MonoClip-embedsrc-"+Guid.NewGuid()+".mp4");var target=Path.Combine(Path.GetTempPath(),"MonoClip-embed-"+Guid.NewGuid()+".mp4");
            try {
                var input=given!=null&&File.Exists(given)?given:remuxed;if(input==remuxed)MonoClip.Windows.Share.ClipRemux.ToFastStartMp4(source,remuxed);
                var facts=MonoClip.Core.Mp4Info.Read(input)??throw new Exception("no mp4 info");var cpu=System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;var timer=System.Diagnostics.Stopwatch.StartNew();
                var way=MonoClip.Windows.Share.ClipEmbedCopy.Create(input,target,facts.Duration,budget);timer.Stop();int height=Math.Min(MonoClip.Core.SharePolicy.EmbedHeight(MonoClip.Core.SharePolicy.EmbedVideoBitrate(facts.Duration,budget)),facts.Height);
                var copy=MonoClip.Core.Mp4Info.Read(target)??throw new Exception("no mp4 info for copy");long size=new FileInfo(target).Length;
                var bytes=File.ReadAllBytes(target);int moov=bytes.AsSpan().IndexOf("moov"u8),mdat=bytes.AsSpan().IndexOf("mdat"u8);if(moov<0||mdat<0||moov>mdat)throw new Exception("copy is not fast-start");
                if(Math.Abs(copy.Height-height)>2||Math.Abs(copy.Duration.TotalSeconds-facts.Duration.TotalSeconds)>0.5)throw new Exception($"unexpected copy {copy} from {facts}");
                var probe=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffprobe",$"-v error -show_entries stream=codec_name,avg_frame_rate -of csv=p=0 \"{target}\""){RedirectStandardOutput=true,UseShellExecute=false})!;var streams=probe.StandardOutput.ReadToEnd().Trim().Replace("\r","").Replace("\n"," ; ");probe.WaitForExit();
                if(!streams.StartsWith("h264,")||!streams.Contains("aac"))throw new Exception("unexpected streams: "+streams);
                var rate=streams.Split(',',';')[1].Split('/');if(double.Parse(rate[0])/double.Parse(rate[1])>30.5)throw new Exception("copy above 30 fps: "+streams);
                if(size>budget*13/10&&facts.Duration.TotalSeconds<=60)throw new Exception($"copy too large: {size/1024} KB");
                Console.WriteLine($"PASS embed copy {new FileInfo(input).Length/1024} KB {facts.Width}x{facts.Height} -> {size/1024} KB {copy.Width}x{copy.Height} {copy.Duration.TotalSeconds:0.0}s in {timer.ElapsedMilliseconds} ms, CPU {(System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime-cpu).TotalMilliseconds:0} ms via {way} ({streams})");return 0;
            } finally { if(Environment.GetEnvironmentVariable("MONOCLIP_KEEP_EMBED") is {} keep&&File.Exists(target))File.Copy(target,keep,true); foreach(var f in new[]{remuxed,target})if(File.Exists(f))File.Delete(f); }
        }
        if(args.Contains("--share-preview")) {
            // Preview image and page facts from a real remuxed clip (no tunnel involved).
            var target=Path.Combine(Path.GetTempPath(),"MonoClip-preview-"+Guid.NewGuid()+".mp4");
            try {
                MonoClip.Windows.Share.ClipRemux.ToFastStartMp4(source,target);var facts=MonoClip.Core.Mp4Info.Read(target)??throw new Exception("no mp4 info");
                if(facts.Width!=1920||facts.Height!=1080||facts.Duration.TotalSeconds is <4 or >7)throw new Exception("unexpected mp4 info "+facts);
                var jpeg=MonoClip.Windows.Share.ClipThumbnail.CreateJpegAsync(target,1280,720,"Test clip").GetAwaiter().GetResult();
                using var image=new Bitmap(new MemoryStream(jpeg));if(image.Width!=1280||image.Height!=720)throw new Exception("preview size "+image.Size);
                int colored=0;for(int x=0;x<image.Width;x+=8)for(int y=0;y<image.Height;y+=8){var c=image.GetPixel(x,y);if(Math.Max(Math.Abs(c.R-c.G),Math.Max(Math.Abs(c.G-c.B),Math.Abs(c.R-c.B)))>40)colored++;}
                if(colored<50)throw new Exception("preview looks like the placeholder, not a video frame ("+colored+" colored samples)");
                Console.WriteLine($"PASS real preview: Windows thumbnail of the clip, {jpeg.Length/1024} KB JPEG, {colored} colored samples, {facts.Width}x{facts.Height} {facts.Duration.TotalSeconds:0.0}s");return 0;
            } finally { if(File.Exists(target))File.Delete(target); }
        }
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
        // The link is the player page: its preview tags must point at the clip and a real preview image.
        var html=http.GetStringAsync(info.Url).GetAwaiter().GetResult();
        string Tag(string property)=>System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html,"property=\""+System.Text.RegularExpressions.Regex.Escape(property)+"\" content=\"([^\"]*)\"").Groups[1].Value);
        var video=Tag("og:video");var preview=Tag("og:image");
        if(!video.StartsWith(new Uri(info.Url).GetLeftPart(UriPartial.Authority))||Tag("og:video:type")!="video/mp4"||Tag("og:video:width")!="1920"||Tag("og:type")!="video.other")throw new Exception("page tags wrong: "+video);
        var previewJpeg=http.GetByteArrayAsync(preview).GetAwaiter().GetResult();if(previewJpeg.Length<1000||previewJpeg[0]!=0xFF||previewJpeg[1]!=0xD8)throw new Exception("preview image missing");
        Console.WriteLine($"INFO page {html.Length} bytes, preview {previewJpeg.Length/1024} KB, video {video}");
        var got=http.GetAsync(video).GetAwaiter().GetResult();var body=got.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        if((int)got.StatusCode!=200||got.Content.Headers.ContentType?.MediaType!="video/mp4"||!body.SequenceEqual(expected)){var tun=(MonoClip.Windows.Share.CloudflaredTunnel?)typeof(MonoClip.Windows.Share.ClipShare).GetField("tunnel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(share);throw new Exception($"public link did not return the clip: {(int)got.StatusCode} {body.Length}/{expected.Length} {System.Text.Encoding.ASCII.GetString(body.Take(40).ToArray())} | "+string.Join(" | ",tun?.RecentLog.TakeLast(8)??["no tunnel"]));}
        var range=new HttpRequestMessage(HttpMethod.Get,video);range.Headers.Range=new(0,1023);var part=http.Send(range);if((int)part.StatusCode!=206)throw new Exception("range through tunnel: "+(int)part.StatusCode);
        var guess=http.GetAsync(video.Replace(video.Split('/')[3],SharePolicyToken())).GetAwaiter().GetResult();if((int)guess.StatusCode!=404)throw new Exception("wrong token served: "+(int)guess.StatusCode);
        // Second share reuses the working tunnel: same host, new path, much faster; the first link stops working.
        // It goes through the share editor's path: trimmed to 1.5-4.0 s (from the keyframe at 1.0 s), losslessly.
        var again=share.ShareAsync(source,TimeSpan.FromMinutes(5),null,new(false,"Test clip",new MonoClip.Core.ClipEdit(TimeSpan.FromSeconds(1.5),TimeSpan.FromSeconds(4),0)));var second=System.Diagnostics.Stopwatch.StartNew();
        while(!again.IsCompleted&&second.Elapsed.TotalSeconds<120){Application.DoEvents();Thread.Sleep(20);}
        var next=again.GetAwaiter().GetResult();Console.WriteLine($"TIME second link ready after {second.Elapsed.TotalSeconds:0.0} s");
        if(new Uri(next.Url).Host!=new Uri(info.Url).Host||next.Url==info.Url)throw new Exception("tunnel was not reused for the second clip");
        if((int)http.GetAsync(info.Url).GetAwaiter().GetResult().StatusCode!=404)throw new Exception("first link still works after the second share");
        if((int)http.GetAsync(next.Url).GetAwaiter().GetResult().StatusCode!=200)throw new Exception("second link does not work");
        {var page=http.GetStringAsync(next.Url).GetAwaiter().GetResult();var trimmedUrl=System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(page,"<video src=\"([^\"]*)\"").Groups[1].Value);
         var trimmed=http.GetByteArrayAsync(trimmedUrl).GetAwaiter().GetResult();var file=Path.Combine(Path.GetTempPath(),"MonoClip-live-trim-"+Guid.NewGuid()+".mp4");File.WriteAllBytes(file,trimmed);
         try{var facts=MonoClip.Core.Mp4Info.Read(file)!;if(Math.Abs(facts.Duration.TotalSeconds-3)>0.15||trimmed.Length>=expected.Length*0.8)throw new Exception($"edited share not trimmed: {facts.Duration} {trimmed.Length} bytes");
             Console.WriteLine($"INFO edited share served {trimmed.Length/1024} KB, {facts.Duration.TotalSeconds:0.00} s of the {expected.Length/1024} KB clip");}finally{File.Delete(file);}}
        local=Directory.EnumerateFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MonoClip","share"),"*.mp4").Single();info=next;
        share.Stop();Application.DoEvents();
        if(share.Current!=null||File.Exists(local))throw new Exception("stop left the share or its copy behind");
        Thread.Sleep(3000);int after;try{after=(int)http.GetAsync(info.Url).GetAwaiter().GetResult().StatusCode;}catch(HttpRequestException){after=-1;}
        if(after==200)throw new Exception("link still serves the clip after stop");
        Console.WriteLine($"PASS live Cloudflare quick tunnel served {body.Length} bytes publicly, range 206, wrong token 404, tunnel reused for a second (trimmed) clip, offline after stop (status {after})");return 0;
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
}
// MonoClip-like test clip: 6 s of 720p30 with a keyframe per second and three named AAC tracks at different pitches
// (mix 440 Hz, desktop 660 Hz, microphone 880 Hz) to tell them apart.
static void TestClip(string mkv)=>Run("ffmpeg",$"-v error -f lavfi -i testsrc2=s=1280x720:r=30:d=6 -f lavfi -i sine=440:d=6 -f lavfi -i sine=660:d=6 -f lavfi -i sine=880:d=6 -map 0 -map 1 -map 2 -map 3 -c:v libx264 -g 30 -bf 0 -c:a aac -metadata:s:a:0 title=\"Playback mix\" -metadata:s:a:1 title=Desktop -metadata:s:a:2 title=Microphone \"{mkv}\"");
// Runs a tool (ffmpeg/ffprobe) and returns its output; a failure throws with its error text.
static string Run(string exe,string arguments){var p=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe,arguments){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false})!;var errors=p.StandardError.ReadToEndAsync();var output=p.StandardOutput.ReadToEnd();p.WaitForExit();if(p.ExitCode!=0)throw new Exception(exe+" failed: "+errors.Result);return output;}
}
