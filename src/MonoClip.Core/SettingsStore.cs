using System.Text.Json;
namespace MonoClip.Core;

public static class SettingsStore
{
    public static AppSettings Load(string path) { if (!File.Exists(path)) return new(); var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new JsonException("Leere Einstellungen"); settings.Validate(); return settings; }
    public static void Save(string path, AppSettings settings) { settings.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); } }
}
