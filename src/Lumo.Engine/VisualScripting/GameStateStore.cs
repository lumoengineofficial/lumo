using System.Text.Json;

namespace Lumo.Engine.VisualScripting;

/// <summary>
/// Serializes blackboard variables to JSON so games can persist progress.
/// File format: a flat string-to-string JSON object.
/// </summary>
public static class GameStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(string path, Blackboard blackboard)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(blackboard.Values, Options));
    }

    public static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string>(StringComparer.Ordinal);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
