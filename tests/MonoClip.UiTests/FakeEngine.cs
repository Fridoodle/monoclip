using MonoClip.Core;
using MonoClip.Windows;
public sealed class FakeSharer:IClipSharer {
 public ShareInfo? Current{get;private set;} public bool IsStarting=>false; public bool FirstUse=>false; public event EventHandler? Changed; public string? SharedClip{get;set;} public TimeSpan Duration; public int Stops;
 public Task<ShareInfo> ShareAsync(string clip,TimeSpan duration,IProgress<string>? progress=null){SharedClip=clip;Duration=duration;progress?.Report("test");Current=new("https://fake-host.trycloudflare.com/t/clip.mp4",DateTimeOffset.Now+duration,clip);Changed?.Invoke(this,EventArgs.Empty);return Task.FromResult(Current);}
 public void Stop(){Stops++;if(Current==null)return;Current=null;Changed?.Invoke(this,EventArgs.Empty);} public void Dispose()=>Stop();
}
public sealed class FakeEngine:IClipEngine {
 public bool IsRunning{get;private set;} public string Status=>IsRunning?"Puffer aktiv":"Puffer gestoppt";public string EncoderName=>"Test engine";public string CaptureTarget=>"Desktop";
 public event EventHandler? StatusChanged;public event EventHandler<ClipSavedEventArgs>? ClipSaved {add{}remove{}}
 public void Start(AppSettings s){IsRunning=true;StatusChanged?.Invoke(this,EventArgs.Empty);}public void Stop(){IsRunning=false;StatusChanged?.Invoke(this,EventArgs.Empty);}public void SaveClip(){}public void Apply(AppSettings s){}public bool FailDispose {get;set;} public void Dispose(){if(FailDispose)throw new TimeoutException("save still pending");Stop();}public IReadOnlyList<AudioDevice> GetAudioDevices(bool input)=>[new("default","Standard")];
}