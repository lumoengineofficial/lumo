using System.Globalization;
using System.Numerics;
using Lumo.Engine.Rendering.Abstractions;

namespace Lumo.Engine.Assets;

/// <summary>
/// Parses Wavefront .obj files into Mesh objects (positions + triangle indices).
/// </summary>
public static class ObjImporter
{
    public static Mesh Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("OBJ file not found", path);

        var positions = new List<Vector3>();
        var indices = new List<uint>();

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    positions.Add(new Vector3(
                        Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                    break;

                case "f" when parts.Length >= 4:
                    // Fan-triangulate polygon faces; support v, v/vt, v/vt/vn, v//vn.
                    uint first = VertIndex(parts[1], positions.Count);
                    for (int i = 2; i < parts.Length - 1; i++)
                    {
                        indices.Add(first);
                        indices.Add(VertIndex(parts[i], positions.Count));
                        indices.Add(VertIndex(parts[i + 1], positions.Count));
                    }
                    break;
            }
        }

        if (positions.Count == 0 || indices.Count == 0)
            throw new InvalidDataException($"OBJ contains no geometry: {path}");

        var mesh = new Mesh(Path.GetFileNameWithoutExtension(path));
        var verts = new float[positions.Count * 3];
        for (int i = 0; i < positions.Count; i++)
        {
            verts[i * 3] = positions[i].X;
            verts[i * 3 + 1] = positions[i].Y;
            verts[i * 3 + 2] = positions[i].Z;
        }
        mesh.Vertices = verts;
        mesh.Indices = indices.ToArray();
        return mesh;
    }

    /// <summary>
    /// Registers every *.obj under <paramref name="directory"/> (recursive) into
    /// the MeshLibrary so saved scenes keep their models after a restart.
    /// Broken files are skipped; returns the number of meshes registered.
    /// </summary>
    public static int RegisterDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return 0;

        int count = 0;
        foreach (string file in Directory.GetFiles(directory, "*.obj", SearchOption.AllDirectories))
        {
            try
            {
                MeshLibrary.Register(Load(file));
                count++;
            }
            catch
            {
                // A broken file must not hide the other models.
            }
        }
        return count;
    }

    private static uint VertIndex(string token, int count)
    {
        var head = token.Split('/')[0];
        int idx = int.Parse(head, CultureInfo.InvariantCulture);
        // OBJ is 1-based; negative indices count from the end.
        return idx > 0 ? (uint)(idx - 1) : (uint)(count + idx);
    }

    private static float Parse(string s)
        => float.Parse(s, CultureInfo.InvariantCulture);
}
