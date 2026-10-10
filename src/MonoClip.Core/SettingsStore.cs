using System.Text.Json;
using System.Text.Json.Serialization;
namespace MonoClip.Core;

public static class SettingsStore
{
    public static AppSettings Load(string path) { if (!File.Exists(path)) return new(); var settings = JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJson.Default.AppSettings) ?? throw new JsonException("Leere Einstellungen"); settings.Validate(); return settings; }
    public static void Save(string path, AppSettings settings) { settings.Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { File.WriteAllText(temp, JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings)); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); } }
}
// Compile-time serializer: starts faster than reflection and needs no runtime code generation.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
