using System.Text.Json;

namespace Lumo.Engine.Core;

public class ProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
    public string Version { get; set; } = "0.1.0";
    public string Thumbnail { get; set; } = string.Empty;
}

public static class ProjectManager
{
    private static readonly string ProjectsRoot;
    private static readonly string RegistryFile;

    static ProjectManager()
    {
        ProjectsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LumoProjects");
        Directory.CreateDirectory(ProjectsRoot);
        RegistryFile = Path.Combine(ProjectsRoot, "projects.json");
    }

    public static string ProjectsRootPath => ProjectsRoot;

    public static List<ProjectInfo> GetAllProjects()
    {
        var projects = new List<ProjectInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(ProjectsRoot))
        {
            foreach (var dir in Directory.GetDirectories(ProjectsRoot))
                TryAddProject(dir, projects, seen);
        }

        foreach (var dir in ReadRegistry())
            TryAddProject(dir, projects, seen);

        return projects;
    }

    private static void TryAddProject(string dir, List<ProjectInfo> projects, HashSet<string> seen)
    {
        if (!seen.Add(Path.GetFullPath(dir))) return;

        var projFile = Path.Combine(dir, "Project.json");
        if (!File.Exists(projFile)) return;

        try
        {
            var json = File.ReadAllText(projFile);
            var info = JsonSerializer.Deserialize<ProjectInfo>(json);
            if (info != null)
            {
                info.Path = dir;
                projects.Add(info);
            }
        }
        catch { }
    }

    /// <summary>
    /// Create a project under <paramref name="basePath"/> (or the default
    /// Documents/LumoProjects root) with the standard folder layout and
    /// Project.json. Returns the project directory.
    /// </summary>
    public static string CreateProject(string name, string description = "", string? basePath = null)
    {
        string root = string.IsNullOrWhiteSpace(basePath) ? ProjectsRoot : basePath;
        Directory.CreateDirectory(root);

        var projectPath = Path.Combine(root, SanitizeName(name));
        if (File.Exists(Path.Combine(projectPath, "Project.json")))
            return projectPath;

        Directory.CreateDirectory(projectPath);
        foreach (string dir in new[] { "Assets", "Scenes", "Graphs", "Scripts", "Plugins" })
            Directory.CreateDirectory(Path.Combine(projectPath, dir));

        var info = new ProjectInfo
        {
            Name = name,
            Path = projectPath,
            Description = description,
            CreatedAt = DateTime.Now,
            LastModified = DateTime.Now,
            Version = EngineConstants.Version
        };

        SaveProjectInfo(info);
        RegisterProject(projectPath);
        return projectPath;
    }

    public static void RegisterProject(string path)
    {
        var list = ReadRegistry();
        string full = Path.GetFullPath(path);
        if (list.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase))) return;
        list.Add(full);
        WriteRegistry(list);
    }

    public static void UnregisterProject(string path)
    {
        string full = Path.GetFullPath(path);
        var list = ReadRegistry();
        list.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        WriteRegistry(list);
    }

    private static List<string> ReadRegistry()
    {
        try
        {
            if (!File.Exists(RegistryFile)) return [];
            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RegistryFile));
            return list ?? [];
        }
        catch { return []; }
    }

    private static void WriteRegistry(List<string> list)
    {
        File.WriteAllText(RegistryFile,
            JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void SaveProjectInfo(ProjectInfo info)
    {
        var projFile = Path.Combine(info.Path, "Project.json");
        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(projFile, json);
    }

    public static ProjectInfo? LoadProjectInfo(string projectPath)
    {
        var projFile = Path.Combine(projectPath, "Project.json");
        if (!File.Exists(projFile)) return null;

        try
        {
            var json = File.ReadAllText(projFile);
            var info = JsonSerializer.Deserialize<ProjectInfo>(json);
            if (info != null)
            {
                info.Path = projectPath;
                return info;
            }
        }
        catch { }
        return null;
    }

    public static void UpdateLastModified(string projectPath)
    {
        var info = LoadProjectInfo(projectPath);
        if (info != null)
        {
            info.LastModified = DateTime.Now;
            SaveProjectInfo(info);
        }
    }

    private static string SanitizeName(string name)
    {
        return string.Concat(name.Split(Path.GetInvalidFileNameChars()))
            .Replace(" ", "_");
    }
}
