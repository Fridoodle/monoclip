namespace MonoClip.Core;

public sealed record ClipContext(string Game, string Executable, string WindowTitle, string CaptureMode);
public sealed record ClipRecord(string Path, string Game, DateTimeOffset SavedAt, int RequestedSeconds, int Width, int Height, int Fps, string Encoder, ClipContext Context);
public static class ClipLibrary
{
    static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
    // A folder name for a game or app: no invalid or control characters, not empty, not too long, no reserved device name.
    public static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(value.Select(c => invalid.Contains(c) || c is '/' or '\\' || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (name.Length == 0) name = "Desktop";
        if (name.Length > 100) name = name[..100].TrimEnd(' ', '.');
        if (ReservedNames.Contains(name.Split('.')[0])) name = "_" + name;
        return name;
    }
    public static ClipRecord Store(string sourceFile, string root, ClipContext context, AppSettings settings, string encoder)
    {
        settings.Validate(); var directory = settings.SortByApp ? Path.Combine(Path.GetFullPath(root), SafeName(context.Game)) : Path.GetFullPath(root); Directory.CreateDirectory(directory);
        var time = DateTimeOffset.Now; var destination = Path.Combine(directory, $"{time:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..8]}.mkv");
        try { File.Move(sourceFile, destination); }
        catch (IOException) when (File.Exists(sourceFile) && !File.Exists(destination))
        {
            // Cross-volume moves need a copy. Keep staging until the completed copy exists.
            try { File.Copy(sourceFile, destination, false); File.Delete(sourceFile); }
            catch { if (File.Exists(sourceFile)) TryDelete(destination); throw; }
        }
        // Folder names are the index. Clip details stay in memory, never next to the video.
        return new(destination, context.Game, time, settings.ClipSeconds, settings.Width, settings.Height, settings.Fps, encoder, context);
    }
    static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    // Game folders below root, without hidden ones (".pending" holds clips still being saved).
    static IEnumerable<string> GameFolders(string root) => Directory.EnumerateDirectories(root).Where(d => !Path.GetFileName(d).StartsWith('.'));
    // Newest saved clip below root (any game folder).
    public static string? LatestClip(string root)
    {
        if (!Directory.Exists(root)) return null;
        return GameFolders(root).Prepend(root).SelectMany(d => Directory.EnumerateFiles(d, "*.mkv")).Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal).FirstOrDefault()?.FullName;
    }
    // The most recently written of the given files that exist, e.g. the last clip or screenshot; null without any.
    public static string? Newest(params string?[] files) => files.OfType<string>().Where(File.Exists).Select(f => new FileInfo(f))
        .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
    public static IReadOnlyList<string> GetGames(string root) => Directory.Exists(root)
        ? GameFolders(root).Where(d => Directory.EnumerateFiles(d, "*.mkv").Any()).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray()
        : [];
}
