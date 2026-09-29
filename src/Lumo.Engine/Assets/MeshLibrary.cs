using System.Numerics;
using Lumo.Engine.Rendering.Abstractions;

namespace Lumo.Engine.Assets;

/// <summary>
/// Global registry of meshes by name. Built-in primitives are pre-registered;
/// imported meshes (OBJ) are added on import.
/// </summary>
public static class MeshLibrary
{
    private static readonly Dictionary<string, Mesh> _meshes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<Mesh, (Vector3 Min, Vector3 Max)> _bounds = new();

    static MeshLibrary()
    {
        Register(Mesh.CreateCube());
        Register(Mesh.CreateQuad());
        Register(Mesh.CreateTriangle());
    }

    public static void Register(Mesh mesh)
    {
        _meshes[mesh.Name] = mesh;
        _bounds.Remove(mesh);
    }

    public static Mesh? Get(string? name)
        => name != null && _meshes.TryGetValue(name, out var m) ? m : null;

    public static bool Contains(string name) => _meshes.ContainsKey(name);

    public static IReadOnlyCollection<string> Names => _meshes.Keys;

    /// <summary>Local-space min/max of a mesh's vertices (cached per instance).</summary>
    public static (Vector3 Min, Vector3 Max) GetBounds(Mesh mesh)
    {
        if (_bounds.TryGetValue(mesh, out var cached))
            return cached;

        var verts = mesh.Vertices;
        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i + 2 < verts.Length; i += 3)
        {
            var v = new Vector3(verts[i], verts[i + 1], verts[i + 2]);
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }
        if (min.X > max.X)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
        }

        var bounds = (min, max);
        _bounds[mesh] = bounds;
        return bounds;
    }
}
