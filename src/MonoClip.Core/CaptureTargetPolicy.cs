namespace MonoClip.Core;

public enum CaptureKind { Keep, Desktop, Game, Window }
public sealed record WindowInfo(string Class, string Title, string Executable, bool ToolWindow, int Width, int Height);
public static class CaptureTargetPolicy
{
    // The desktop and taskbar mean "this monitor"; menus, tooltips, the start menu and
    // task switcher are transient and must not steal the capture from the real window.
    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
    static readonly HashSet<string> TransientClasses = new(StringComparer.Ordinal) { "#32768", "tooltips_class32", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "ForegroundStaging", "TaskListThumbnailWnd" };
    public static CaptureKind Decide(WindowInfo? window, string hookedExecutable)
    {
        if (window is null || ShellClasses.Contains(window.Class)) return CaptureKind.Desktop;
        if (TransientClasses.Contains(window.Class) || window.ToolWindow || window.Width < 64 || window.Height < 64 || string.IsNullOrWhiteSpace(window.Executable)) return CaptureKind.Keep;
        if (!string.IsNullOrWhiteSpace(hookedExecutable) && string.Equals(window.Executable, hookedExecutable, StringComparison.OrdinalIgnoreCase)) return CaptureKind.Game;
        return CaptureKind.Window;
    }
    // libobs window selector: "title:class:executable", each part with '#' and ':' escaped.
    public static string EncodeWindow(WindowInfo window) => $"{Escape(window.Title)}:{Escape(window.Class)}:{Escape(window.Executable)}";
    static string Escape(string value) => value.Replace("#", "#22").Replace(":", "#3A");
}
