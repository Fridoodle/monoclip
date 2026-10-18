using System.Diagnostics;
using System.Runtime.InteropServices;
namespace MonoClip.Windows;

public static class WindowsIntegration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void SetAutostart(bool enabled, string executablePath, string registryPath = RunKey)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryPath, true)
            ?? throw new IOException("Could not open the Windows startup key.");
        var command = enabled ? BuildAutostartCommand(executablePath) : null;
        if (enabled) key.SetValue("MonoClip", command!, Microsoft.Win32.RegistryValueKind.String);
        else key.DeleteValue("MonoClip", false);
        if (!Equals(key.GetValue("MonoClip"), command))
            throw new IOException("Could not verify the Windows startup entry.");
    }
    // Makes the startup entry match the setting and point at this copy of MonoClip. With several
    // versions extracted side by side, the one started last wins. Returns whether anything changed.
    public static bool SyncAutostart(bool enabled, string executablePath, string registryPath = RunKey)
    {
        string? current;
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(registryPath)) current = key?.GetValue("MonoClip") as string;
        var wanted = enabled ? BuildAutostartCommand(executablePath) : null;
        if (current == wanted) return false;
        SetAutostart(enabled, executablePath, registryPath); return true;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")] static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[]? items, uint flags);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr pidl);

    // Opens the folder with the file selected. Reuses an Explorer window already showing it.
    public static void RevealInExplorer(string file)
    {
        if (SHParseDisplayName(Path.GetFullPath(file), IntPtr.Zero, out var pidl, 0, out _) == 0)
            try { if (SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0) return; } finally { CoTaskMemFree(pidl); }
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(file)}\"") { UseShellExecute = false });
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, nint min, nint max);
    // After one-off work (sharing, exporting) Windows' media and shell components stay loaded with memory MonoClip no
    // longer touches. Hand those pages back; anything still in use is paged in again on demand. -1: this process.
    public static void TrimMemory() => SetProcessWorkingSetSize(-1, -1, -1);
    // Selects the newest clip when there is one; otherwise opens the (created) folder.
    public static void OpenClipFolder(string folder, string? preferredClip = null) =>
        Open(folder, preferredClip != null && File.Exists(preferredClip) && IsBelow(preferredClip, folder) ? preferredClip : MonoClip.Core.ClipLibrary.LatestClip(folder));
    // Selects the newest screenshot when there is one; otherwise opens the (created) folder.
    public static void OpenScreenshotFolder(string folder) => Open(folder, MonoClip.Core.Screenshots.Latest(folder));
    // "Open folder": whatever was saved last, the last clip or the last screenshot, selected in Explorer; without either,
    // the clip folder.
    public static void OpenLatest(MonoClip.Core.AppSettings settings, string? lastClip = null)
    {
        var clip = lastClip != null && File.Exists(lastClip) ? lastClip : MonoClip.Core.ClipLibrary.LatestClip(settings.ClipsDirectory);
        Open(settings.ClipsDirectory, MonoClip.Core.ClipLibrary.Newest(clip, MonoClip.Core.Screenshots.Latest(settings.ScreenshotFolder())));
    }
    static void Open(string folder, string? select)
    {
        if (select != null) { RevealInExplorer(select); return; }
        Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
    static bool IsBelow(string file, string folder) => Path.GetFullPath(file).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string BuildAutostartCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"'))
            throw new ArgumentException("Invalid program path.", nameof(executablePath));
        return $"\"{executablePath}\" --minimized";
    }
}
