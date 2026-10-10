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
        Test("Success feedback contains no balloon notification",()=>{
            var method=typeof(MonoClip.Windows.UI.TrayAppContext).GetMethod("ClipSaved",BindingFlags.Instance|BindingFlags.NonPublic)!;var il=method.GetMethodBody()!.GetILAsByteArray()!;
            for(int i=0;i<il.Length-4;i++)if(il[i] is 0x28 or 0x6f){try{var call=method.Module.ResolveMethod(BitConverter.ToInt32(il,i+1));if(call?.Name=="ShowBalloonTip")throw new InvalidOperationException("success still opens a Windows notification");}catch(ArgumentException){}catch(BadImageFormatException){}}
        });
        Test("Beep is a short real PCM wave with quiet edges",()=>{
            var type=Assembly.GetExecutingAssembly().GetType("MonoClip.Windows.UI.ClipBeepSound")??throw new Exception("ClipBeepSound missing");
            var bytes=(byte[])type.GetMethod("CreateWave")!.Invoke(null,null)!;using var reader=new BinaryReader(new MemoryStream(bytes));
            Equal("RIFF",System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)));Equal(bytes.Length-8,reader.ReadInt32());Equal("WAVE",System.Text.Encoding.ASCII.GetString(reader.ReadBytes(4)));
            reader.BaseStream.Position=22;Equal((short)1,reader.ReadInt16());var rate=reader.ReadInt32();reader.BaseStream.Position=34;Equal((short)16,reader.ReadInt16());reader.BaseStream.Position=40;var size=reader.ReadInt32();if(size*1000d/(rate*2) is <50 or >120)throw new Exception("beep is not short");
            var samples=new List<short>();while(reader.BaseStream.Position<bytes.Length)samples.Add(reader.ReadInt16());if(samples.All(x=>x==0))throw new Exception("silent waveform");if(Math.Abs((int)samples[0])>100||Math.Abs((int)samples[^1])>100)throw new Exception("waveform edges click");
        });
        Test("Beep checkbox is configurable in saved UI values",()=>{
            using var form=new MonoClip.Windows.UI.SettingsForm(new MonoClip.Core.AppSettings{ClipBeep=false},new FakeEngine(),_=>{},()=>{},()=>{});
            var field=typeof(MonoClip.Windows.UI.SettingsForm).GetField("clipBeep",BindingFlags.Instance|BindingFlags.NonPublic)??throw new Exception("Beep checkbox missing");var box=(CheckBox)field.GetValue(form)!;
            Equal(false,form.ReadSettings().ClipBeep);box.Checked=true;Equal(true,form.ReadSettings().ClipBeep);
        });
        Test("Clip beep fires only after success and honors current saved toggle",()=>{
            int played=0;var engine=new FakeEngine();var dir=Path.Combine(Path.GetTempPath(),"MonoClip-cue-test-"+Guid.NewGuid());Directory.CreateDirectory(dir);
            try{var type=typeof(MonoClip.Windows.UI.TrayAppContext);var ctor=type.GetConstructor(new[]{typeof(MonoClip.Windows.IClipEngine),typeof(MonoClip.Core.AppSettings),typeof(string),typeof(bool),typeof(Action)})??throw new Exception("success cue binding missing");
                var settings=new MonoClip.Core.AppSettings{ClipBeep=true,Hotkey="Ctrl+Alt+Shift+F10"};using var ctx=(ApplicationContext)ctor.Invoke(new object[]{engine,settings,Path.Combine(dir,"settings.json"),false,(Action)(()=>played++)});
                Equal(0,played);var success=type.GetMethod("ClipSaved",BindingFlags.Instance|BindingFlags.NonPublic)!;var record=new MonoClip.Core.ClipRecord(Path.Combine(dir,"unit-fixture.mkv"),"Fixture",DateTimeOffset.Now,5,1280,720,30,"test",new("Fixture","fixture.exe","","desktop"));
                var eventArgs=new MonoClip.Windows.ClipSavedEventArgs(record);success.Invoke(ctx,new object?[]{null,eventArgs});Equal(1,played);
                type.GetMethod("SaveSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(ctx,new object[]{settings with{ClipBeep=false}});success.Invoke(ctx,new object?[]{null,eventArgs});Equal(1,played);Equal(false,MonoClip.Core.SettingsStore.Load(Path.Combine(dir,"settings.json")).ClipBeep);
                type.GetMethod("SaveSettings",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(ctx,new object[]{settings with{ClipBeep=true}});success.Invoke(ctx,new object?[]{null,eventArgs});Equal(2,played);
            }finally{Directory.Delete(dir,true);}
        });
        Test("Simple mode hides advanced settings and advanced mode reveals quality slider",()=>{
            using var form=new MonoClip.Windows.UI.SettingsForm(new MonoClip.Core.AppSettings{Quality=MonoClip.Core.QualityPreset.Performance},new FakeEngine(),_=>{},()=>{},()=>{});form.Show();Application.DoEvents();
            Control F(string name)=>(Control)typeof(MonoClip.Windows.UI.SettingsForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(form)!;
            foreach(var name in new[]{"resolution","fps","duration","hotkey","autostart","directory"})if(!F(name).Visible)throw new Exception(name+" missing in simple mode");
            foreach(var name in new[]{"quality","mic","desktop","clipBeep","minimized","bufferOnLaunch","games"})if(F(name).Visible)throw new Exception(name+" visible in simple mode");
            var budget=(Label)F("budget");if(!budget.Visible||!budget.Text.Contains("MB"))throw new Exception("estimated file size missing: "+budget.Text);
            ((CheckBox)F("advanced")).Checked=true;Application.DoEvents();
            foreach(var name in new[]{"quality","mic","desktop","clipBeep","minimized","bufferOnLaunch","games"})if(!F(name).Visible)throw new Exception(name+" hidden in advanced mode");
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
        if(args.Contains("--play-beep"))Test("Real cached beep playback uses Windows audio successfully",()=>{
            using var sound=new MonoClip.Windows.UI.ClipBeepSound();sound.Play();Thread.Sleep(150);
            var player=(System.Media.SoundPlayer?)typeof(MonoClip.Windows.UI.ClipBeepSound).GetField("player",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(sound);if(player==null||!player.IsLoadCompleted)throw new Exception("Windows sound playback could not load cue");
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
