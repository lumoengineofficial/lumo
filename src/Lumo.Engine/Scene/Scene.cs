using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumo.Engine.Scene;

/// <summary>
/// Represents a game scene containing entities.
/// </summary>
public sealed class Scene
{
    public string Name { get; set; } = "Untitled Scene";
    public List<Entity> RootEntities { get; } = [];
    public string? FilePath { get; set; }

    private readonly List<Entity> _allEntities = [];
    public IReadOnlyList<Entity> AllEntities => _allEntities;

    public Entity CreateEntity(string name = "Entity")
    {
        var entity = new Entity(name)
        {
            ParentScene = this
        };
        RootEntities.Add(entity);
        _allEntities.Add(entity);
        return entity;
    }

    public void DestroyEntity(Entity entity)
    {
        RootEntities.Remove(entity);
        _allEntities.Remove(entity);
        entity.ParentScene = null;

        foreach (var child in entity.Children)
        {
            DestroyEntity(child);
        }
        entity.Children.Clear();
    }

    internal void OnEntityAdded(Entity entity)
    {
        if (!_allEntities.Contains(entity))
            _allEntities.Add(entity);
    }

    /// <summary>Register a deserialized entity so it appears in AllEntities.</summary>
    internal void RegisterLoadedEntity(Entity entity) => OnEntityAdded(entity);

    public Entity? FindByName(string name)
    {
        return _allEntities.FirstOrDefault(e => e.Name == name);
    }

    public Entity? FindById(long id)
    {
        return _allEntities.FirstOrDefault(e => e.Id == id);
    }

    public IEnumerable<Entity> GetEntitiesWithComponent<T>() where T : class
    {
        return _allEntities.Where(e => e.HasComponent<T>());
    }

    /// <summary>
    /// Serialize the scene to JSON.
    /// </summary>
    public string Serialize()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        var data = SceneSerializer.Serialize(this);
        return JsonSerializer.Serialize(data, options);
    }

    /// <summary>
    /// Save the scene to a file.
    /// </summary>
    public void Save(string path)
    {
        FilePath = path;
        string json = Serialize();
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Load a scene from JSON.
    /// </summary>
    public static Scene Load(string path)
        => Deserialize(File.ReadAllText(path));

    /// <summary>
    /// Deserialize a scene from JSON text (used by load and undo/redo).
    /// </summary>
    public static Scene Deserialize(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var data = JsonSerializer.Deserialize<SceneData>(json, options);
        if (data == null)
            throw new InvalidOperationException("Failed to deserialize scene");

        return SceneSerializer.Deserialize(data);
    }
}

/// <summary>
/// Serializable scene data format.
/// </summary>
public sealed class SceneData
{
    public string Name { get; set; } = "Untitled Scene";
    public List<EntityData> Entities { get; set; } = [];
}

public sealed class EntityData
{
    public string Name { get; set; } = "Entity";
    public long Id { get; set; }
    public TransformData? Transform { get; set; }
    public MeshRendererData? MeshRenderer { get; set; }
    public SpriteRendererData? SpriteRenderer { get; set; }
    public CameraData? Camera { get; set; }
    public LightData? Light { get; set; }
    public ScriptsData? Scripts { get; set; }
    public long? ParentId { get; set; }
}

public sealed class TransformData
{
    public float[] Position { get; set; } = [0, 0, 0];
    public float[] Rotation { get; set; } = [0, 0, 0, 1];
    public float[] Scale { get; set; } = [1, 1, 1];
}

public sealed class MeshRendererData
{
    public string? MeshName { get; set; }
    public string? MaterialName { get; set; }
    public bool IsVisible { get; set; } = true;
    public float[] Color { get; set; } = [0.55f, 0.62f, 0.75f];
}

public sealed class SpriteRendererData
{
    public string? SpritePath { get; set; }
    public float Width { get; set; } = 1.0f;
    public float Height { get; set; } = 1.0f;
    public int SortOrder { get; set; }
    public bool IsVisible { get; set; } = true;
    public float[] Color { get; set; } = [0.47f, 0.67f, 0.94f];
}

public sealed class CameraData
{
    public bool IsPrimary { get; set; }
    public float FieldOfView { get; set; } = 60.0f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 1000.0f;
}

public sealed class LightData
{
    public string LightType { get; set; } = "Directional";
    public float Intensity { get; set; } = 1.0f;
    public float[] Color { get; set; } = [1, 1, 1];
}

public sealed class ScriptsData
{
    public bool Enabled { get; set; } = true;
    public List<string> ScriptNames { get; set; } = [];
}
