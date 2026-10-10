using MonoClip.Core;
using System.Runtime.InteropServices;

// Keeps the portable folder tidy: only this starter and folders. The app itself runs from app\,
// because OBS looks for its helper programs next to the running EXE.
// Plain blittable Win32 calls instead of System.Diagnostics.Process keep the starter tiny (NativeAOT).
var app = Path.Combine(AppContext.BaseDirectory, "app", "MonoClip.exe");
if (!File.Exists(app)) return Fail("Der Ordner \"app\" fehlt oder ist unvollständig. Bitte das komplette MonoClip-ZIP erneut in einen neuen Ordner entpacken.");
// CreateProcessW may write into the command line buffer, so it must be a mutable, terminated copy.
var command = (CommandLine.Build(app, args) + '\0').ToCharArray();
var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
if (!CreateProcessW(app, command, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, Path.GetDirectoryName(app)!, ref startup, out var process))
    return Fail("MonoClip konnte nicht gestartet werden (Windows-Fehler " + Marshal.GetLastPInvokeError() + ").");
CloseHandle(process.Process); CloseHandle(process.Thread);
return 0;

static int Fail(string text) { MessageBoxW(IntPtr.Zero, text, "MonoClip", 0x10); return 1; }

[StructLayout(LayoutKind.Sequential)] struct StartupInfo { public int Size; public IntPtr Reserved, Desktop, Title; public int X, Y, Width, Height, CountChars, CountLines, FillAttribute, Flags; public short ShowWindow, Reserved2; public IntPtr Reserved3, StdInput, StdOutput, StdError; }
[StructLayout(LayoutKind.Sequential)] struct ProcessInformation { public IntPtr Process, Thread; public int ProcessId, ThreadId; }
partial class Program
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcessW(string application, char[] commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation information);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
