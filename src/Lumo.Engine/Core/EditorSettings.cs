using System.Text.Json;

namespace Lumo.Engine.Core;

/// <summary>
/// User-editable editor preferences persisted as editor-settings.json next to
/// projects.json (Documents/LumoProjects). Empty values mean "use the default".
/// </summary>
public static class EditorSettings
{
    private static readonly string FilePath;
    private static readonly object Sync = new();

    private sealed class Data
    {
        public string ProjectsRoot { get; set; } = "";
        public string StoreUrl { get; set; } = "";
    }

    private static Data _data = new();

    /// <summary>Override for the projects folder; empty = Documents/LumoProjects.</summary>
    public static string ProjectsRoot
    {
        get => _data.ProjectsRoot;
        set => _data.ProjectsRoot = value ?? "";
    }

    /// <summary>Asset Store base URL override; empty = the official store.</summary>
    public static string StoreUrl
    {
        get => _data.StoreUrl;
        set => _data.StoreUrl = value ?? "";
    }

    static EditorSettings()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LumoProjects");
        FilePath = Path.Combine(dir, "editor-settings.json");
        Load();
    }

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }
        catch { _data = new Data(); }
    }

    public static void Save()
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_data,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
