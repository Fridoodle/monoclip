using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
internal static class Program
{
    [STAThread]static int Main(string[] args){try{ApplicationConfiguration.Initialize();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.Run(new Game(args.Contains("--exclusive")));return 0;}catch(Exception e){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"fixture-error.log"),e.ToString());return 1;}}
}
internal sealed unsafe class Game:Form
{
    [StructLayout(LayoutKind.Sequential)]struct Mode{public uint Width,Height,Num,Den,Format,Scan,Scale;}
    [StructLayout(LayoutKind.Sequential)]struct Sample{public uint Count,Quality;}
    [StructLayout(LayoutKind.Sequential)]struct SwapDesc{public Mode Mode;public Sample Sample;public uint Usage,Count;public IntPtr Window;public int Windowed;public uint Effect,Flags;}
    [StructLayout(LayoutKind.Sequential)]struct Viewport{public float X,Y,Width,Height,Min,Max;}
    [DllImport("d3d11.dll")]static extern int D3D11CreateDeviceAndSwapChain(IntPtr adapter,int type,IntPtr software,uint flags,IntPtr levels,uint n,uint sdk,ref SwapDesc desc,out IntPtr swap,out IntPtr device,out int level,out IntPtr context);
    [DllImport("d3dcompiler_47.dll",CallingConvention=CallingConvention.StdCall)]static extern int D3DCompile(byte[] data,nuint length,IntPtr name,IntPtr macros,IntPtr include,[MarshalAs(UnmanagedType.LPStr)]string entry,[MarshalAs(UnmanagedType.LPStr)]string target,uint flags,uint flags2,out IntPtr code,out IntPtr errors);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr hwnd);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int GetBuffer(IntPtr self,uint index,ref Guid iid,out IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateView(IntPtr self,IntPtr texture,IntPtr desc,out IntPtr view);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int CreateShader(IntPtr self,IntPtr bytes,nuint size,IntPtr linkage,out IntPtr shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate IntPtr BlobPtr(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate nuint BlobSize(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void SetShader(IntPtr self,IntPtr shader,IntPtr instances,uint n);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void SetTargets(IntPtr self,uint n,ref IntPtr views,IntPtr depth);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void ClearView(IntPtr self,IntPtr view,float* color);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void SetViewport(IntPtr self,uint n,ref Viewport viewport);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Topology(IntPtr self,uint topology);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate void Draw(IntPtr self,uint n,uint start);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Present(IntPtr self,uint sync,uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]delegate int Fullscreen(IntPtr self,int value,IntPtr target);
    static T Method<T>(IntPtr p,int index) where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(p),index*IntPtr.Size));
    static void Check(int hr){Marshal.ThrowExceptionForHR(hr);}
    IntPtr swap,device,context,view,vs,ps;readonly System.Windows.Forms.Timer timer=new(){Interval=16};readonly Stopwatch clock=new();readonly bool exclusive;ClearView? clear;Present? present;Draw? draw;
    public Game(bool exclusive){this.exclusive=exclusive;Text="MonoClip Direct3D test";FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Bounds=Screen.PrimaryScreen!.Bounds;TopMost=true;KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Escape)Close();};Shown+=(_,_)=>Initialize();timer.Tick+=(_,_)=>Render();}
    IntPtr Shader(string source,string target,int index)
    {
        var bytes=Encoding.UTF8.GetBytes(source);Check(D3DCompile(bytes,(nuint)bytes.Length,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,"main",target,0,0,out var blob,out var error));
        try{var pointer=Method<BlobPtr>(blob,3)(blob);var size=Method<BlobSize>(blob,4)(blob);Check(Method<CreateShader>(device,index)(device,pointer,size,IntPtr.Zero,out var shader));return shader;}finally{if(error!=IntPtr.Zero)Marshal.Release(error);Marshal.Release(blob);}
    }
    void Initialize()
    {
        var d=new SwapDesc{Mode=new Mode{Width=(uint)ClientSize.Width,Height=(uint)ClientSize.Height,Num=60,Den=1,Format=28},Sample=new Sample{Count=1},Usage=0x20,Count=2,Window=Handle,Windowed=1,Effect=0};
        Check(D3D11CreateDeviceAndSwapChain(IntPtr.Zero,1,IntPtr.Zero,0,IntPtr.Zero,0,7,ref d,out swap,out device,out _,out context));
        var iid=new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");Check(Method<GetBuffer>(swap,9)(swap,0,ref iid,out var texture));try{Check(Method<CreateView>(device,9)(device,texture,IntPtr.Zero,out view));}finally{Marshal.Release(texture);}
        vs=Shader("float4 main(uint id : SV_VertexID) : SV_POSITION { float2 p[3] = {float2(-.7,-.7),float2(0,.7),float2(.7,-.7)}; return float4(p[id],0,1); }","vs_4_0",12);
        ps=Shader("float4 main() : SV_TARGET { return float4(1,1,1,1); }","ps_4_0",15);
        Method<SetShader>(context,11)(context,vs,IntPtr.Zero,0);Method<SetShader>(context,9)(context,ps,IntPtr.Zero,0);Method<SetTargets>(context,33)(context,1,ref view,IntPtr.Zero);Method<Topology>(context,24)(context,4);var viewport=new Viewport{Width=ClientSize.Width,Height=ClientSize.Height,Max=1};Method<SetViewport>(context,44)(context,1,ref viewport);
        clear=Method<ClearView>(context,50);present=Method<Present>(swap,8);draw=Method<Draw>(context,13);SetForegroundWindow(Handle);
        if(exclusive)Check(Method<Fullscreen>(swap,10)(swap,1,IntPtr.Zero));clock.Start();timer.Start();
    }
    void Render(){float x=(float)(Math.Sin(clock.Elapsed.TotalSeconds)*.15+.2);var color=stackalloc float[]{x,.1f,.25f,1};clear!(context,view,color);draw!(context,3,0);Check(present!(swap,1,0));if(clock.Elapsed.TotalSeconds>24)Close();}
    protected override void Dispose(bool disposing){if(disposing){timer.Dispose();if(swap!=IntPtr.Zero)Method<Fullscreen>(swap,10)(swap,0,IntPtr.Zero);foreach(var p in new[]{ps,vs,view,context,device,swap})if(p!=IntPtr.Zero)Marshal.Release(p);swap=IntPtr.Zero;}base.Dispose(disposing);}
}
