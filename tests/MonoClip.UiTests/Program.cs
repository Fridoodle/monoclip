using System.Reflection;

internal static class Program
{
    private static int failures;
    [STAThread]
    private static int Main(string[] args)
    {
        Test("Hotkey parses modifiers and F8 without repeat", () =>
        {
            var type = Assembly.GetExecutingAssembly().GetType("MonoClip.Windows.UI.Hotkey") ?? throw new Exception("Hotkey implementation missing");
            var value = type.GetMethod("Parse")!.Invoke(null, new object[] { "Ctrl+Shift+F8" })!;
            Equal(0x4006u, (uint)type.GetProperty("Modifiers")!.GetValue(value)!);
            Equal(Keys.F8, (Keys)type.GetProperty("Key")!.GetValue(value)!);
            Equal("Ctrl+Shift+F8", value.ToString());
        });
        Test("Hotkey rejects modifier-only keys and ambiguous numeric keys", () =>
        {
            var type = Assembly.GetExecutingAssembly().GetType("MonoClip.Windows.UI.Hotkey")!;
            foreach (var text in new[] { "", "Ctrl", "Ctrl+Shift", "Ctrl+17", "Ctrl+NoSuchKey", "Ctrl+F8+", "Ctrl+Ctrl+F8" })
            {
                try { type.GetMethod("Parse")!.Invoke(null, new object[] { text }); }
                catch (TargetInvocationException e) when (e.InnerException is ArgumentException) { continue; }
                throw new Exception("Accepted invalid hotkey: " + text);
            }
        });
        Test("Autostart quotes executable and forces minimized startup", () =>
        {
            var type = Assembly.GetExecutingAssembly().GetType("MonoClip.Windows.WindowsIntegration") ?? throw new Exception("WindowsIntegration implementation missing");
            Equal("\"C:\\Program Files\\MonoClip\\MonoClip.exe\" --minimized", (string)type.GetMethod("BuildAutostartCommand")!.Invoke(null, new object[] { @"C:\Program Files\MonoClip\MonoClip.exe" })!);
        });
        Test("Autostart opt-in writes and removes only the MonoClip value", () =>
        {
            var type = Assembly.GetExecutingAssembly().GetType("MonoClip.Windows.WindowsIntegration")!;
            var method = type.GetMethod("SetAutostart") ?? throw new Exception("SetAutostart missing");
            var path = @"Software\MonoClip.UiTests\" + Guid.NewGuid();
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path)) key.SetValue("Other", "untouched");
                method.Invoke(null, new object[] { true, @"C:\Program Files\MonoClip\MonoClip.exe", path });
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path)) Equal("\"C:\\Program Files\\MonoClip\\MonoClip.exe\" --minimized", key!.GetValue("MonoClip"));
                method.Invoke(null, new object[] { false, @"C:\Program Files\MonoClip\MonoClip.exe", path });
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(path)) { Equal<object?>(null, key!.GetValue("MonoClip")); Equal("untouched", key.GetValue("Other")); }
            }
            finally { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(path, false); }
        });
        Test("Global hotkey conflict preserves original registration", () =>
        {
            var type = Assembly.GetExecutingAssembly().GetType("MonoClip.Windows.UI.HotkeyRegistration") ?? throw new Exception("HotkeyRegistration missing");
            using var first = (IDisposable)Activator.CreateInstance(type, new object[] { (Action)(() => { }) })!;
            using var second = (IDisposable)Activator.CreateInstance(type, new object[] { (Action)(() => { }) })!;
            var parse = Assembly.GetExecutingAssembly().GetType("MonoClip.Windows.UI.Hotkey")!.GetMethod("Parse")!;
            var a = parse.Invoke(null, new object[] { "Ctrl+Alt+Shift+F11" })!;
            var b = parse.Invoke(null, new object[] { "Ctrl+Alt+Shift+F12" })!;
            type.GetMethod("Set")!.Invoke(first, new[] { a });
            type.GetMethod("Set")!.Invoke(second, new[] { b });
            try { type.GetMethod("Set")!.Invoke(first, new[] { b }); throw new Exception("Conflicting registration accepted"); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
            Equal(a, type.GetProperty("Current")!.GetValue(first));
            try { type.GetMethod("Set")!.Invoke(second, new[] { a }); throw new Exception("Original registration was lost"); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        });
        Test("Settings form validates values and closes to hidden instead of exiting", () => {
            var type=Assembly.GetExecutingAssembly().GetType("MonoClip.Windows.UI.SettingsForm")??throw new Exception("SettingsForm missing");
            using var form=(Form)Activator.CreateInstance(type,new object[]{new MonoClip.Core.AppSettings(),new FakeEngine(),(Action<MonoClip.Core.AppSettings>)(_=>{}),(Action)(()=>{}),(Action)(()=>{})})!;
            form.Show();Application.DoEvents();if(!form.Visible)throw new Exception("settings not visible");
            if(((CheckBox)type.GetField("desktop",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!).FlatStyle!=FlatStyle.Flat)throw new Exception("system accent color leaks into monochrome controls");
            if(((ComboBox)type.GetField("resolution",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!).DrawMode!=DrawMode.OwnerDrawFixed)throw new Exception("system accent leaks into dropdown selections");
            var disabled=(Button)type.GetField("clipButton",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            using(var image=new Bitmap(disabled.Width,disabled.Height)){disabled.DrawToBitmap(image,disabled.ClientRectangle);bool visibleText=false;for(int x=5;x<image.Width-5;x++)for(int y=5;y<image.Height-5;y++)if(image.GetPixel(x,y).R>90)visibleText=true;if(!visibleText)throw new Exception("disabled action label has no readable contrast");}
            var read=(MonoClip.Core.AppSettings)type.GetMethod("ReadSettings")!.Invoke(form,null)!;read.Validate();
            ((TextBox)type.GetField("directory",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!).Text="";
            ((ComboBox)type.GetField("fps",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!).SelectedIndex=0;
            try{type.GetMethod("ReadSettings")!.Invoke(form,null);throw new Exception("empty destination accepted");}catch(TargetInvocationException e) when(e.InnerException is ArgumentException){}
            form.Close();Application.DoEvents();if(form.IsDisposed||form.Visible)throw new Exception("closing exited or did not hide");
        });
        Test("Tray-only launch has no visible taskbar window and settings can reopen",()=>{
            var type=Assembly.GetExecutingAssembly().GetType("MonoClip.Windows.UI.TrayAppContext")??throw new Exception("TrayAppContext missing");
            var dir=Path.Combine(Path.GetTempPath(),"MonoClip-ui-"+Guid.NewGuid());Directory.CreateDirectory(dir);
            try {using var ctx=(ApplicationContext)Activator.CreateInstance(type,new object[]{new FakeEngine(),new MonoClip.Core.AppSettings{Hotkey="Alt+Shift+F10",StartMinimized=true,StartBufferOnLaunch=false},Path.Combine(dir,"settings.json")})!;
                var tray=(NotifyIcon)type.GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ctx)!;
                var color=((ToolStripProfessionalRenderer)tray.ContextMenuStrip!.Renderer).ColorTable.MenuItemSelected;
                if(color.R!=color.G||color.G!=color.B)throw new Exception("colored tray menu selection");
                Application.DoEvents();if(Application.OpenForms.Cast<Form>().Any(f=>f.Visible))throw new Exception("window visible on minimized launch");
                type.GetMethod("ShowSettings")!.Invoke(ctx,null);Application.DoEvents();var form=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Visible);if(form==null)throw new Exception("settings failed to open");
                form.Close();Application.DoEvents();if(form.Visible||form.IsDisposed)throw new Exception("close-to-tray broken");
                ((ToolStripMenuItem)tray.ContextMenuStrip!.Items[0]).PerformClick();if(!((FakeEngine)type.GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ctx)!).IsRunning)throw new Exception("tray start failed");
                ((ToolStripMenuItem)tray.ContextMenuStrip.Items[0]).PerformClick();if(((FakeEngine)type.GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ctx)!).IsRunning)throw new Exception("tray stop failed");
                var fake=(FakeEngine)type.GetField("engine",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ctx)!;fake.FailDispose=true;
                try{((ToolStripMenuItem)tray.ContextMenuStrip.Items[^1]).PerformClick();throw new Exception("exit ignored pending shutdown failure");}catch(TimeoutException){}
                if(!tray.Visible)throw new Exception("failed exit stranded capture without accessible tray");
                fake.FailDispose=false;
                ((ToolStripMenuItem)tray.ContextMenuStrip.Items[^1]).PerformClick();if(tray.Visible)throw new Exception("exit left a tray icon");
            }finally{Directory.Delete(dir,true);}
        });
        Test("Tray never opens pop-up notifications",()=>{
            // Scan the tray class and its compiler-generated lambdas/state machines for ShowBalloonTip calls.
            var types=typeof(MonoClip.Windows.UI.TrayAppContext).Assembly.GetTypes().Where(t=>t==typeof(MonoClip.Windows.UI.TrayAppContext)||t.FullName!.StartsWith("MonoClip.Windows.UI.TrayAppContext+"));
            foreach(var method in types.SelectMany(t=>t.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)).Concat<MethodBase>(types.SelectMany(t=>t.GetConstructors(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)))){
                var il=method.GetMethodBody()?.GetILAsByteArray();if(il==null)continue;
                for(int i=0;i<il.Length-4;i++)if(il[i] is 0x28 or 0x6f){try{if(method.Module.ResolveMethod(BitConverter.ToInt32(il,i+1))?.Name=="ShowBalloonTip")throw new InvalidOperationException(method.Name+" opens a Windows pop-up");}catch(ArgumentException){}catch(BadImageFormatException){}}
            }
        });
        Test("Four distinct, short, clean sound cues",()=>{
            var waves=new Dictionary<MonoClip.Windows.UI.Sound,short[]>();
            foreach(var sound in Enum.GetValues<MonoClip.Windows.UI.Sound>()){
                var bytes=MonoClip.Windows.UI.SoundEffects.CreateWave(sound);using var reader=new BinaryReader(new MemoryStream(bytes));
                Equal("RIFF",System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)));Equal(bytes.Length-8,reader.ReadInt32());Equal("WAVE",System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)));
                reader.BaseStream.Position=22;Equal((short)1,reader.ReadInt16());var rate=reader.ReadInt32();reader.BaseStream.Position=34;Equal((short)16,reader.ReadInt16());reader.BaseStream.Position=40;var size=reader.ReadInt32();
                double ms=size*1000d/(rate*2);if(ms is <60 or >600)throw new Exception($"{sound} is {ms:0} ms long");
                var samples=new short[size/2];for(int i=0;i<samples.Length;i++)samples[i]=reader.ReadInt16();
                int peak=samples.Max(x=>Math.Abs((int)x));if(peak<5000)throw new Exception(sound+" is nearly silent");if(peak>16000)throw new Exception(sound+" is too loud");
                if(samples.Take(3).Concat(samples.TakeLast(3)).Any(x=>Math.Abs((int)x)>200))throw new Exception(sound+" edges click");
                waves[sound]=samples;
            }
            if(waves.Values.Select(w=>w.Length).Distinct().Count()<4)throw new Exception("cues are not distinguishable");
            if(waves[MonoClip.Windows.UI.Sound.Link].Length<=waves[MonoClip.Windows.UI.Sound.Clip].Length)throw new Exception("link cue should ring out longer than the clip cue");
        });
        Test("Beep checkbox is configurable in saved UI values",()=>{
            using var form=new MonoClip.Windows.UI.SettingsForm(new MonoClip.Core.AppSettings{ClipBeep=false},new FakeEngine(),_=>{},()=>{},()=>{});
            var field=typeof(MonoClip.Windows.UI.SettingsForm).GetField("clipBeep",BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("Sound checkbox missing");var box=(CheckBox)field.GetValue(form)!;
            Equal(false,form.ReadSettings().ClipBeep);box.Checked=true;Equal(true,form.ReadSettings().ClipBeep);
        });
        Test("Clip sound fires only after success and honors the current sound toggle",()=>{
            var played=new List<MonoClip.Windows.UI.Sound>();var engine=new FakeEngine();var dir=Path.Combine(Path.GetTempPath(),"MonoClip-cue-test-"+Guid.NewGuid());Directory.CreateDirectory(dir);
            try{var type=typeof(MonoClip.Windows.UI.TrayAppContext);
                var settings=new MonoClip.Core.AppSettings{ClipBeep=true,Hotkey="Ctrl+Alt+Shift+F10",StartBufferOnLaunch=false};using var ctx=new MonoClip.Windows.UI.TrayAppContext(engine,settings,Path.Combine(dir,"settings.json"),false,played.Add);
                Equal(0,played.Count);var success=type.GetMethod("ClipSaved",BindingFlags.Instance|BindingFlags.NonPublic)!;var record=new MonoClip.Core.ClipRecord(Path.Combine(dir,"unit-fixture.mkv"),"Fixture",DateTimeOffset.Now,5,1280,720,30,"test",new("Fixture","fixture.exe","","desktop"));
                var eventArgs=new MonoClip.Windows.ClipSavedEventArgs(record);success.Invoke(ctx,new object?[]{null,eventArgs});Equal("Clip",string.Join(",",played));
                type.GetMethod("SaveSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(ctx,new object[]{settings with{ClipBeep=false}});success.Invoke(ctx,new object?[]{null,eventArgs});Equal(1,played.Count);Equal(false,MonoClip.Core.SettingsStore.Load(Path.Combine(dir,"settings.json")).ClipBeep);
                type.GetMethod("SaveSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(ctx,new object[]{settings with{ClipBeep=true}});success.Invoke(ctx,new object?[]{null,eventArgs});Equal(2,played.Count);
            }finally{Directory.Delete(dir,true);}
        });
        Test("Auto-share shares each new clip, copies the link and plays clip, upload, link",()=>{
            var dir=Path.Combine(Path.GetTempPath(),"MonoClip-auto-"+Guid.NewGuid());Directory.CreateDirectory(dir);
            try{var sharer=new FakeSharer();var played=new List<MonoClip.Windows.UI.Sound>();string? copied=null;
                using var ctx=new MonoClip.Windows.UI.TrayAppContext(new FakeEngine(),new MonoClip.Core.AppSettings{AutoShare=true,ShareMinutes=10,Hotkey="Ctrl+Alt+Shift+F6",StartBufferOnLaunch=false,ClipsDirectory=dir},Path.Combine(dir,"settings.json"),false,played.Add,sharer){CopyText=t=>copied=t};
                var clip=Path.Combine(dir,"auto.mkv");File.WriteAllBytes(clip,new byte[]{0x1A,0x45,0xDF,0xA3,0,0,0,0,0,0,0,0});
                typeof(MonoClip.Windows.UI.TrayAppContext).GetMethod("ClipSaved",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(ctx,new object?[]{null,new MonoClip.Windows.ClipSavedEventArgs(new(clip,"Desktop",DateTimeOffset.Now,5,1920,1080,60,"test",new("Desktop","","","desktop")))});Application.DoEvents();
                Equal(clip,sharer.SharedClip);Equal(TimeSpan.FromMinutes(10),sharer.Duration);Equal("https://fake-host.trycloudflare.com/t/clip.mp4",copied);Equal("Clip,Upload,Link",string.Join(",",played));
                using var off=new MonoClip.Windows.UI.TrayAppContext(new FakeEngine(),new MonoClip.Core.AppSettings{Hotkey="Ctrl+Alt+Shift+F5",StartBufferOnLaunch=false,ClipsDirectory=dir},Path.Combine(dir,"b.json"),false,_=>{},sharer);sharer.Stop();sharer.SharedClip=null;
                typeof(MonoClip.Windows.UI.TrayAppContext).GetMethod("ClipSaved",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(off,new object?[]{null,new MonoClip.Windows.ClipSavedEventArgs(new(clip,"Desktop",DateTimeOffset.Now,5,1920,1080,60,"test",new("Desktop","","","desktop")))});
                Equal<string?>(null,sharer.SharedClip);
            }finally{Directory.Delete(dir,true);}
        });
        Test("Manually chosen files are checked before anything is shared",()=>{
            var dir=Path.Combine(Path.GetTempPath(),"MonoClip-pick-"+Guid.NewGuid());Directory.CreateDirectory(dir);
            try{var sharer=new FakeSharer();var played=new List<MonoClip.Windows.UI.Sound>();
                using var ctx=new MonoClip.Windows.UI.TrayAppContext(new FakeEngine(),new MonoClip.Core.AppSettings{Hotkey="Ctrl+Alt+Shift+F4",StartBufferOnLaunch=false,ClipsDirectory=dir},Path.Combine(dir,"settings.json"),false,played.Add,sharer){CopyText=_=>{}};
                var fake=Path.Combine(dir,"notes.mp4");File.WriteAllText(fake,"definitely not a video, just text");
                ctx.ShareClip(fake,chosen:true).GetAwaiter().GetResult();Equal<string?>(null,sharer.SharedClip);Equal("Error",string.Join(",",played));
                var tray=(NotifyIcon)typeof(MonoClip.Windows.UI.TrayAppContext).GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ctx)!;if(!tray.Text.Contains("Keine gültige"))throw new Exception("error reason not shown in tooltip: "+tray.Text);
                var real=Path.Combine(dir,"real.mp4");File.WriteAllBytes(real,new byte[]{0,0,0,24,(byte)'f',(byte)'t',(byte)'y',(byte)'p',(byte)'i',(byte)'s',(byte)'o',(byte)'m'});played.Clear();
                ctx.ShareClip(real,chosen:true).GetAwaiter().GetResult();Equal(real,sharer.SharedClip);Equal("Upload,Link",string.Join(",",played));
            }finally{Directory.Delete(dir,true);}
        });
        Test("Simple mode hides advanced settings and advanced mode reveals quality slider",()=>{
            using var form=new MonoClip.Windows.UI.SettingsForm(new MonoClip.Core.AppSettings{Quality=MonoClip.Core.QualityPreset.Performance},new FakeEngine(),_=>{},()=>{},()=>{});form.Show();Application.DoEvents();
            Control F(string name)=>(Control)typeof(MonoClip.Windows.UI.SettingsForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            foreach(var name in new[]{"duration","hotkey","autostart","directory"})if(!F(name).Visible)throw new Exception(name+" missing in simple mode");
            foreach(var name in new[]{"resolution","fps","quality","mic","desktop","clipBeep","minimized","bufferOnLaunch","games","shareMinutes","autoShare","pickButton"})if(F(name).Visible)throw new Exception(name+" visible in simple mode");
            var budget=(Label)F("budget");if(!budget.Visible||!budget.Text.Contains("MB"))throw new Exception("estimated file size missing: "+budget.Text);
            ((CheckBox)F("advanced")).Checked=true;Application.DoEvents();
            foreach(var name in new[]{"resolution","fps","quality","mic","desktop","clipBeep","minimized","bufferOnLaunch","games","shareMinutes","autoShare","pickButton"})if(!F(name).Visible)throw new Exception(name+" hidden in advanced mode");
            Equal(1920,form.ReadSettings().Width);Equal(60,form.ReadSettings().Fps);
            if(!budget.Text.Contains("Mbit/s"))throw new Exception("advanced estimate lacks bitrate: "+budget.Text);
            var slider=F("quality");Equal(MonoClip.Core.QualityPreset.Performance,form.ReadSettings().Quality);var before=budget.Text;
            slider.GetType().GetProperty("Value")!.SetValue(slider,2);Equal(MonoClip.Core.QualityPreset.Quality,form.ReadSettings().Quality);Equal(true,form.ReadSettings().AdvancedMode);if(budget.Text==before)throw new Exception("estimate ignores quality");
            using(var image=new Bitmap(slider.Width,slider.Height)){slider.DrawToBitmap(image,slider.ClientRectangle);for(int x=0;x<image.Width;x++)for(int y=0;y<image.Height-slider.Font.Height-6;y++){var c=image.GetPixel(x,y);if(Math.Max(c.R,Math.Max(c.G,c.B))-Math.Min(c.R,Math.Min(c.G,c.B))>12)throw new Exception("slider is not monochrome");}}
            form.Close();
        });
        Test("Default launch starts the replay buffer",()=>{
            var dir=Path.Combine(Path.GetTempPath(),"MonoClip-ui-"+Guid.NewGuid());Directory.CreateDirectory(dir);var engine=new FakeEngine();
            try{using var ctx=new MonoClip.Windows.UI.TrayAppContext(engine,new MonoClip.Core.AppSettings{Hotkey="Ctrl+Alt+Shift+F9"},Path.Combine(dir,"settings.json"));if(!engine.IsRunning)throw new Exception("buffer not started by default");}finally{Directory.Delete(dir,true);}
        });
        Test("Tray shares the newest clip, copies the link and stops on demand",()=>{
            var dir=Path.Combine(Path.GetTempPath(),"MonoClip-share-ui-"+Guid.NewGuid());Directory.CreateDirectory(Path.Combine(dir,"Game"));var clip=Path.Combine(dir,"Game","newest.mkv");File.WriteAllText(clip,"fixture");
            try{var sharer=new FakeSharer();string? copied=null;
                using(var plain=new MonoClip.Windows.UI.TrayAppContext(new FakeEngine(),new MonoClip.Core.AppSettings{Hotkey="Ctrl+Alt+Shift+F7",StartBufferOnLaunch=false,ClipsDirectory=dir},Path.Combine(dir,"a.json"))){var t=(NotifyIcon)typeof(MonoClip.Windows.UI.TrayAppContext).GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(plain)!;if(t.ContextMenuStrip!.Items.Cast<ToolStripItem>().Any(i=>i.Text!.Contains("teilen")&&i.Available))throw new Exception("share item shown without a sharer");}
                using var ctx=new MonoClip.Windows.UI.TrayAppContext(new FakeEngine(),new MonoClip.Core.AppSettings{Hotkey="Ctrl+Alt+Shift+F8",StartBufferOnLaunch=false,ClipsDirectory=dir},Path.Combine(dir,"settings.json"),false,_=>{},sharer){CopyText=text=>copied=text};
                var tray=(NotifyIcon)typeof(MonoClip.Windows.UI.TrayAppContext).GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ctx)!;var items=tray.ContextMenuStrip!.Items.Cast<ToolStripItem>().ToList();
                var share=(ToolStripMenuItem)items.Single(i=>i.Text!.StartsWith("Letzten Clip teilen"));Equal("Letzten Clip teilen · 15 Min.",share.Text);if(items.IndexOf(share)<1||items[^1].Text!="Beenden")throw new Exception("share item misplaced");
                share.PerformClick();Application.DoEvents();
                Equal(clip,sharer.SharedClip);Equal(TimeSpan.FromMinutes(15),sharer.Duration);Equal("https://fake-host.trycloudflare.com/t/clip.mp4",copied);if(!share.Text!.StartsWith("Teilen beenden · noch 15 Min."))throw new Exception("active share not shown: "+share.Text);
                var again=(ToolStripMenuItem)items.Single(i=>i.Text=="Link erneut kopieren");copied=null;again.PerformClick();Equal("https://fake-host.trycloudflare.com/t/clip.mp4",copied);
                share.PerformClick();Application.DoEvents();Equal<object?>(null,sharer.Current);if(!share.Text!.StartsWith("Letzten Clip teilen"))throw new Exception("stopped share still shown");
            }finally{Directory.Delete(dir,true);}
        });
        Test("Tray symbol is a black tile with a filled or hollow white dot",()=>{
            foreach(var size in new[]{16,20,24,32}){
                using var on=MonoClip.Windows.UI.TrayAppContext.MakeIcon(MonoClip.Windows.UI.TrayIconKind.Recording,size);using var off=MonoClip.Windows.UI.TrayAppContext.MakeIcon(MonoClip.Windows.UI.TrayIconKind.Stopped,size);using var a=on.ToBitmap();using var b=off.ToBitmap();
                Color P(Bitmap bmp,int x,int y)=>bmp.GetPixel(x,y);
                // Tile: opaque and dark near the edges (corners stay rounded), white dot in the middle.
                foreach(var (x,y) in new[]{(size/2,1),(1,size/2),(size-2,size/2),(size/2,size-2)})foreach(var bmp in new[]{a,b})if(P(bmp,x,y).A<200||P(bmp,x,y).R>60)throw new Exception($"no black tile at {x},{y} size {size}");
                if(P(a,0,0).A>200)throw new Exception("tile corners not rounded");
                var center=P(a,size/2,size/2);if(center.A<200||center.R<200)throw new Exception("recording dot not filled white");
                if(P(b,size/2,size/2).R>60)throw new Exception("stopped symbol is not a ring");
                bool ring=false;for(int x=0;x<size/2;x++)if(P(b,x,size/2).R>150)ring=true;if(!ring)throw new Exception("ring outline missing");
            }
        });
        Test("Tray symbol shows creating, online and error states",()=>{
            foreach(var size in new[]{16,24,32}){
                Bitmap B(MonoClip.Windows.UI.TrayIconKind kind,bool online=false,int frame=0){using var icon=MonoClip.Windows.UI.TrayAppContext.MakeIcon(kind,size,online,frame);return icon.ToBitmap();}
                int Diff(Bitmap a,Bitmap b){int n=0;for(int x=0;x<size;x++)for(int y=0;y<size;y++)if(Math.Abs(a.GetPixel(x,y).R-b.GetPixel(x,y).R)>60)n++;return n;}
                using var rec=B(MonoClip.Windows.UI.TrayIconKind.Recording);using var online=B(MonoClip.Windows.UI.TrayIconKind.Recording,true);using var f0=B(MonoClip.Windows.UI.TrayIconKind.Busy);using var f3=B(MonoClip.Windows.UI.TrayIconKind.Busy,frame:3);using var error=B(MonoClip.Windows.UI.TrayIconKind.Error);
                if(Diff(rec,online)<3)throw new Exception("online badge not visible at "+size);
                bool badgeTopRight=false;for(int x=size*2/3;x<size;x++)for(int y=0;y<size/3;y++)if(online.GetPixel(x,y).R>200)badgeTopRight=true;if(!badgeTopRight)throw new Exception("badge not in the top-right corner");
                if(Diff(f0,f3)<3)throw new Exception("spinner frames do not move");
                if(error.GetPixel(size/2,size*2/5).R<200||error.GetPixel(size/2,size/2).R<120)throw new Exception("error mark missing");if(Diff(error,rec)<size)throw new Exception("error symbol looks like the recording dot");
            }
        });
        Test("Settings window shares, copies the link and shows remaining minutes",()=>{
            var sharer=new FakeSharer();int toggles=0,copies=0;using var form=new MonoClip.Windows.UI.SettingsForm(new MonoClip.Core.AppSettings{ShareMinutes=20},new FakeEngine(),_=>{},()=>{},()=>{});form.Show();Application.DoEvents();
            Control F(string name)=>(Control)typeof(MonoClip.Windows.UI.SettingsForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            if(F("shareButton").Visible)throw new Exception("share button shown without sharing support");
            string? picked=null;form.AttachSharing(sharer,()=>toggles++,()=>copies++,path=>picked=path);
            if(!F("pickButton").Enabled)throw new Exception("pick button not enabled with sharing support");var share=(Button)F("shareButton");var copy=(Button)F("copyLinkButton");
            if(!share.Visible||share.Text!="Letzten Clip teilen · 20 Min."||copy.Visible)throw new Exception("idle share controls wrong: "+share.Text);
            share.PerformClick();Equal(1,toggles);
            sharer.ShareAsync("clip.mkv",TimeSpan.FromMinutes(20));form.UpdateStatus();
            var status=(Label)F("shareStatus");if(!copy.Visible||share.Text!="Teilen beenden"||!status.Visible||!status.Text.Contains("noch 20 Min.")||!status.Text.Contains("fake-host.trycloudflare.com"))throw new Exception("active share not shown: "+status.Text);
            copy.PerformClick();Equal(1,copies);
            Equal(20,form.ReadSettings().ShareMinutes);((NumericUpDown)F("shareMinutes")).Value=30;Equal(30,form.ReadSettings().ShareMinutes);
            Equal(false,form.ReadSettings().AutoShare);((CheckBox)F("autoShare")).Checked=true;Equal(true,form.ReadSettings().AutoShare);
            form.Close();
        });
        Test("Advanced mode toggle persists immediately",()=>{
            MonoClip.Core.AppSettings? saved=null;using var form=new MonoClip.Windows.UI.SettingsForm(new MonoClip.Core.AppSettings(),new FakeEngine(),s=>saved=s,()=>{},()=>{});
            ((CheckBox)typeof(MonoClip.Windows.UI.SettingsForm).GetField("advanced",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!).Checked=true;
            if(saved?.AdvancedMode!=true)throw new Exception("advanced mode not saved");
        });
        Test("Estimated size formatting",()=>{
            Equal("5.0 MB",MonoClip.Windows.UI.SettingsForm.FormatSize(5).Replace(',','.'));Equal("58 MB",MonoClip.Windows.UI.SettingsForm.FormatSize(57.6));Equal("1.5 GB",MonoClip.Windows.UI.SettingsForm.FormatSize(1536).Replace(',','.'));
        });
        if(args.Contains("--reveal"))Test("Clip folder opens in Explorer with the newest clip selected",()=>{
            var dir=Path.Combine(Path.GetTempPath(),"MonoClip-reveal-"+Guid.NewGuid());Directory.CreateDirectory(Path.Combine(dir,"Game"));Directory.CreateDirectory(Path.Combine(dir,"Desktop"));
            var old=Path.Combine(dir,"Desktop","old.mkv");File.WriteAllText(old,"fixture");File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddHours(-1));var newest=Path.Combine(dir,"Game","newest.mkv");File.WriteAllText(newest,"fixture");
            dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;string? selected=null;
            try{MonoClip.Windows.WindowsIntegration.OpenClipFolder(dir);var clock=System.Diagnostics.Stopwatch.StartNew();
                while(selected==null&&clock.ElapsedMilliseconds<8000){Application.DoEvents();Thread.Sleep(200);foreach(dynamic w in shell.Windows()){try{if(string.Equals((string)w.Document.Folder.Self.Path,Path.GetDirectoryName(newest),StringComparison.OrdinalIgnoreCase)){foreach(dynamic item in w.Document.SelectedItems())selected=(string)item.Path;if(selected!=null)w.Quit();}}catch(Exception){}}}
                Equal(newest,selected);
            }finally{Thread.Sleep(300);Directory.Delete(dir,true);}
        });
        // Opt-in visual check: renders the settings window (simple/advanced, sharing) and tray symbols to PNG.
        if(args.Contains("--render"))Test("Render settings window and tray symbols",()=>{
            var dir=args[Array.IndexOf(args,"--render")+1];Directory.CreateDirectory(dir);
            foreach(var advanced in new[]{false,true}){
                var sharer=new FakeSharer();using var form=new MonoClip.Windows.UI.SettingsForm(new MonoClip.Core.AppSettings{AdvancedMode=advanced},new FakeEngine(),_=>{},()=>{},()=>{});form.Show();
                form.AttachSharing(sharer,()=>{},()=>{},_=>{});if(advanced){sharer.ShareAsync("c.mkv",TimeSpan.FromMinutes(15));form.UpdateStatus();}
                form.ClientSize=new Size(form.ClientSize.Width,advanced?1500:820);Application.DoEvents();
                using var image=new Bitmap(form.Width,form.Height);form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size));image.Save(Path.Combine(dir,advanced?"settings-advanced.png":"settings-simple.png"));
                if(advanced){var table=(Control)typeof(MonoClip.Windows.UI.SettingsForm).GetField("table",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;using var all=new Bitmap(table.Width,table.Height);table.DrawToBitmap(all,new Rectangle(Point.Empty,table.Size));all.Save(Path.Combine(dir,"settings-advanced-full.png"));}
                form.Close();
            }
            // Row per taskbar colour: recording, stopped, recording+online, creating (2 frames), error.
            var states=new (MonoClip.Windows.UI.TrayIconKind Kind,bool Online,int Frame)[]{(MonoClip.Windows.UI.TrayIconKind.Recording,false,0),(MonoClip.Windows.UI.TrayIconKind.Stopped,false,0),(MonoClip.Windows.UI.TrayIconKind.Recording,true,0),(MonoClip.Windows.UI.TrayIconKind.Busy,false,0),(MonoClip.Windows.UI.TrayIconKind.Busy,false,4),(MonoClip.Windows.UI.TrayIconKind.Error,false,0)};
            using var sheet=new Bitmap(states.Length*46+8,140);using(var g=Graphics.FromImage(sheet)){g.FillRectangle(new SolidBrush(Color.FromArgb(32,32,32)),0,0,sheet.Width,70);g.FillRectangle(new SolidBrush(Color.FromArgb(238,238,238)),0,70,sheet.Width,70);
                for(int row=0;row<2;row++)for(int i=0;i<states.Length;i++){using var icon=MonoClip.Windows.UI.TrayAppContext.MakeIcon(states[i].Kind,32,states[i].Online,states[i].Frame);g.DrawIcon(icon,8+i*46,row*70+6);using var small=MonoClip.Windows.UI.TrayAppContext.MakeIcon(states[i].Kind,16,states[i].Online,states[i].Frame);g.DrawIcon(small,16+i*46,row*70+46);}}
            sheet.Save(Path.Combine(dir,"tray.png"));
        });
        if(args.Contains("--play-sounds"))Test("All sound cues play through Windows audio",()=>{
            using var sounds=new MonoClip.Windows.UI.SoundEffects();foreach(var sound in Enum.GetValues<MonoClip.Windows.UI.Sound>())sounds.Play(sound);
            var clock=System.Diagnostics.Stopwatch.StartNew();while(clock.ElapsedMilliseconds<2500){Application.DoEvents();Thread.Sleep(20);}
        });
        Console.WriteLine($"UI tests: {failures} failure(s)");
        return failures == 0 ? 0 : 1;
    }
    private static void Test(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + (e.InnerException ?? e).Message); }
    }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
}
