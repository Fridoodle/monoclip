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
    public static string BuildAutostartCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"'))
            throw new ArgumentException("Ungültiger Programmpfad.", nameof(executablePath));
        return $"\"{executablePath}\" --minimized";
    }
}
