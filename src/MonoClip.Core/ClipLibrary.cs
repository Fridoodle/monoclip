namespace MonoClip.Core;

public sealed record ClipContext(string Game, string Executable, string WindowTitle, string CaptureMode);
public sealed record ClipRecord(string Path, string Game, DateTimeOffset SavedAt, int RequestedSeconds, int Width, int Height, int Fps, string Encoder, ClipContext Context);
public static class ClipLibrary
{
    public static string SafeName(string value)
    {
        var invalid=Path.GetInvalidFileNameChars();
        var name=new string(value.Select(c=>invalid.Contains(c)||c is '/' or '\\'||char.IsControl(c)?'_' :c).ToArray()).Trim(' ','.');
        if(name.Length==0)name="Desktop";if(name.Length>100)name=name[..100].TrimEnd(' ','.');
        var stem=name.Split('.')[0];if(new[]{"CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9","LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem,StringComparer.OrdinalIgnoreCase))name="_"+name;
        return name;
    }
    public static ClipRecord Store(string sourceFile,string root,ClipContext context,AppSettings settings,string encoder)
    {
        settings.Validate();var directory=Path.Combine(Path.GetFullPath(root),SafeName(context.Game));Directory.CreateDirectory(directory);
        var time=DateTimeOffset.Now;var destination=Path.Combine(directory,$"{time:yyyy-MM-dd_HH-mm-ss}_{Guid.NewGuid().ToString("N")[..8]}.mkv");
        try {File.Move(sourceFile,destination);}
        catch(IOException) when(File.Exists(sourceFile)&&!File.Exists(destination))
        {
            // Cross-volume moves need a copy. Keep staging until the completed copy exists.
            try {File.Copy(sourceFile,destination,false);File.Delete(sourceFile);}
            catch {if(File.Exists(sourceFile))TryDelete(destination);throw;}
        }
        // Folder names are the index. Clip details stay in memory, never next to the video.
        return new(destination,context.Game,time,settings.ClipSeconds,settings.Width,settings.Height,settings.Fps,encoder,context);
    }
    static void TryDelete(string path){try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
    public static IReadOnlyList<string> GetGames(string root)=>Directory.Exists(root)?Directory.EnumerateDirectories(root).Where(d=>!Path.GetFileName(d).StartsWith('.')&&Directory.EnumerateFiles(d,"*.mkv").Any()).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray():[];
}
