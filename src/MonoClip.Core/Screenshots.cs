namespace MonoClip.Core;

// Where screenshots go and what they are called: "2026-10-18_21-04-05_Valorant.png", named after the app in front.
public static class Screenshots
{
    public const string DefaultFolderName = "Screenshots";
    // Creates the (empty) file right away, so two quick screenshots never pick the same name; a second one in the same
    // second gets "_2", and so on. The picture is written into it afterwards.
    public static string Reserve(string folder, string app, DateTimeOffset time)
    {
        Directory.CreateDirectory(folder);
        var stem = $"{time:yyyy-MM-dd_HH-mm-ss}_{ClipLibrary.SafeName(app)}";
        for (int n = 1; ; n++)
        {
            var path = Path.Combine(folder, (n == 1 ? stem : $"{stem}_{n}") + ".png");
            try { using (new FileStream(path, FileMode.CreateNew, FileAccess.Write)) { } return path; }
            catch (IOException) when (File.Exists(path)) { }
        }
    }
}
