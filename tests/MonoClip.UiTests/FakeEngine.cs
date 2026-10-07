using MonoClip.Core;
using MonoClip.Windows;
public sealed class FakeEngine:IClipEngine {
 public bool IsRunning{get;private set;} public string Status=>IsRunning?"Puffer aktiv":"Puffer gestoppt";public string EncoderName=>"Test engine";public string CaptureTarget=>"Desktop";
 public event EventHandler? StatusChanged;public event EventHandler<ClipSavedEventArgs>? ClipSaved {add{}remove{}}
 public void Start(AppSettings s){IsRunning=true;StatusChanged?.Invoke(this,EventArgs.Empty);}public void Stop(){IsRunning=false;StatusChanged?.Invoke(this,EventArgs.Empty);}public void SaveClip(){}public void Apply(AppSettings s){}public bool FailDispose {get;set;} public void Dispose(){if(FailDispose)throw new TimeoutException("save still pending");Stop();}public IReadOnlyList<AudioDevice> GetAudioDevices(bool input)=>[new("default","Standard")];
}