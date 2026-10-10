using System.Diagnostics;
using System.Runtime.InteropServices;
namespace MonoClip.Windows;

public static class WindowsIntegration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void SetAutostart(bool enabled, string executablePath, string registryPath = RunKey)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(registryPath, true)
            ?? throw new IOException("Windows-Autostart konnte nicht geöffnet werden.");
        var command = enabled ? BuildAutostartCommand(executablePath) : null;
        if (enabled) key.SetValue("MonoClip", command!, Microsoft.Win32.RegistryValueKind.String);
        else key.DeleteValue("MonoClip", false);
        if (!Equals(key.GetValue("MonoClip"), command))
            throw new IOException("Windows-Autostart konnte nicht bestätigt werden.");
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
    // Selects the newest clip when there is one; otherwise opens the (created) folder.
    public static void OpenClipFolder(string folder, string? preferredClip = null)
    {
        Directory.CreateDirectory(folder);
        var clip = preferredClip != null && File.Exists(preferredClip) && IsBelow(preferredClip, folder) ? preferredClip : MonoClip.Core.ClipLibrary.LatestClip(folder);
        if (clip != null) RevealInExplorer(clip); else Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }
    static bool IsBelow(string file, string folder) => Path.GetFullPath(file).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string BuildAutostartCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"'))
            throw new ArgumentException("Ungültiger Programmpfad.", nameof(executablePath));
        return $"\"{executablePath}\" --minimized";
    }
}
