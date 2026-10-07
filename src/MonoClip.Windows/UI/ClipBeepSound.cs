using System.Media;
using System.Text;
namespace MonoClip.Windows.UI;
public sealed class ClipBeepSound : IDisposable
{
    MemoryStream? stream;SoundPlayer? player;bool disposed;
    public static byte[] CreateWave()
    {
        const int rate=16000;int count=(int)(rate*.08);using var data=new MemoryStream();using var writer=new BinaryWriter(data,Encoding.ASCII,true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
        for(int i=0;i<count;i++){double fade=Math.Min(1d,Math.Min(i,count-1-i)/(rate*.006));writer.Write((short)(6000*fade*Math.Sin(2*Math.PI*880*i/rate)));}
        writer.Flush();return data.ToArray();
    }
    public void Play()
    {
        if(disposed)return;
        try{stream??=new MemoryStream(CreateWave(),false);player??=new SoundPlayer(stream);player.Play();}
        catch(Exception e) when(e is InvalidOperationException or IOException or TimeoutException or System.ComponentModel.Win32Exception){ /* A missing audio device must not turn a saved clip into an error. */ }
    }
    public void Dispose(){if(disposed)return;disposed=true;player?.Stop();player?.Dispose();stream?.Dispose();}
}
