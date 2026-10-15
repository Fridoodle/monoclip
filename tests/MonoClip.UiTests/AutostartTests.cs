using Microsoft.Win32;
using MonoClip.Windows;
using static UiTest;

// Each test writes to its own throwaway key under HKCU\Software\MonoClip.UiTests, never to the real Run key.
internal static class AutostartTests
{
    const string Exe = @"C:\Program Files\MonoClip\MonoClip.exe";

    public static void Run()
    {
        Test("Autostart quotes executable and forces minimized startup", QuotesExecutable);
        Test("Autostart opt-in writes and removes only the MonoClip value", OptInTouchesOnlyItsValue);
        Test("Startup entry follows the running copy and is only rewritten when it differs", SyncFollowsRunningCopy);
    }

    static void QuotesExecutable()
    {
        Equal("\"C:\\Program Files\\MonoClip\\MonoClip.exe\" --minimized", WindowsIntegration.BuildAutostartCommand(Exe));
    }

    static void OptInTouchesOnlyItsValue()
    {
        var path = @"Software\MonoClip.UiTests\" + Guid.NewGuid();
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(path)) key.SetValue("Other", "untouched");
            WindowsIntegration.SetAutostart(true, Exe, path);
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                Equal("\"C:\\Program Files\\MonoClip\\MonoClip.exe\" --minimized", key!.GetValue("MonoClip"));
            }
            WindowsIntegration.SetAutostart(false, Exe, path);
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                Equal<object?>(null, key!.GetValue("MonoClip"));
                Equal("untouched", key.GetValue("Other"));
            }
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }

    static void SyncFollowsRunningCopy()
    {
        var path = @"Software\MonoClip.UiTests\" + Guid.NewGuid();
        try
        {
            if (!WindowsIntegration.SyncAutostart(true, @"C:\Old\app\MonoClip.exe", path)) throw new Exception("first enable not written");
            if (WindowsIntegration.SyncAutostart(true, @"C:\Old\app\MonoClip.exe", path)) throw new Exception("unchanged entry rewritten");
            if (!WindowsIntegration.SyncAutostart(true, @"C:\New Version\app\MonoClip.exe", path)) throw new Exception("new location not picked up");
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                Equal("\"C:\\New Version\\app\\MonoClip.exe\" --minimized", key!.GetValue("MonoClip"));
            }
            if (!WindowsIntegration.SyncAutostart(false, @"C:\New Version\app\MonoClip.exe", path)) throw new Exception("disable not applied");
            using (var key = Registry.CurrentUser.OpenSubKey(path))
            {
                Equal<object?>(null, key!.GetValue("MonoClip"));
            }
            if (WindowsIntegration.SyncAutostart(false, @"C:\New Version\app\MonoClip.exe", path)) throw new Exception("absent entry touched");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
    }
}
