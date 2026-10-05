using System.Numerics;
using Mesh = Lumo.Engine.Rendering.Abstractions.Mesh;

namespace Lumo.TerrainPlugin;

/// <summary>Builds chunk meshes from a heightmap: positions, normals, UVs and
/// height/slope based vertex colors. Chunks keep rebuilds small while sculpting.</summary>
public static class TerrainMesher
{
    private static readonly Vector3 Sand = new(0.78f, 0.71f, 0.50f);
    private static readonly Vector3 Grass = new(0.31f, 0.47f, 0.25f);
    private static readonly Vector3 DryGrass = new(0.45f, 0.47f, 0.28f);
    private static readonly Vector3 Rock = new(0.44f, 0.42f, 0.39f);
    private static readonly Vector3 Snow = new(0.92f, 0.94f, 0.96f);

    /// <summary>Mesh for one chunk; sample indices clamp at map edges so any map size works.</summary>
    public static Mesh BuildChunk(Heightmap map, int cx, int cz, int cells, string meshName)
    {
        int gx0 = cx * cells;
        int gz0 = cz * cells;
        int side = cells + 1;
        int vertexCount = side * side;
        int maxGX = map.Width - 1;
        int maxGZ = map.Height - 1;

        var mesh = new Mesh(meshName)
        {
            Vertices = new float[vertexCount * 3],
            Normals = new float[vertexCount * 3],
            TexCoords = new float[vertexCount * 2],
            VertexColors = new float[vertexCount * 4],
            Indices = new uint[cells * cells * 6],
        };

        float cell = map.CellWorld;
        float heightScale = map.HeightScale;

        for (int j = 0; j <= cells; j++)
        {
            for (int i = 0; i <= cells; i++)
            {
                int gx = Math.Min(gx0 + i, maxGX);
                int gz = Math.Min(gz0 + j, maxGZ);
                int v = j * side + i;
                float wx = map.GridToWorld(gx, map.Width);
                float wz = map.GridToWorld(gz, map.Height);
                float h = map.Get(gx, gz);

                mesh.Vertices[v * 3 + 0] = wx;
                mesh.Vertices[v * 3 + 1] = h;
                mesh.Vertices[v * 3 + 2] = wz;

                float hL = map.Get(gx - 1, gz);
                float hR = map.Get(gx + 1, gz);
                float hD = map.Get(gx, gz - 1);
                float hU = map.Get(gx, gz + 1);
                var normal = Vector3.Normalize(new Vector3(
                    -(hR - hL) / (2f * cell),
                    1f,
                    -(hU - hD) / (2f * cell)));
                mesh.Normals[v * 3 + 0] = normal.X;
                mesh.Normals[v * 3 + 1] = normal.Y;
                mesh.Normals[v * 3 + 2] = normal.Z;

                mesh.TexCoords[v * 2 + 0] = gx / (float)maxGX;
                mesh.TexCoords[v * 2 + 1] = gz / (float)maxGZ;

                var color = ColorFor(h, heightScale, normal);
                mesh.VertexColors[v * 4 + 0] = color.X;
                mesh.VertexColors[v * 4 + 1] = color.Y;
                mesh.VertexColors[v * 4 + 2] = color.Z;
                mesh.VertexColors[v * 4 + 3] = 1f;
            }
        }

        int t = 0;
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                uint a = (uint)(j * side + i);
                uint b = a + 1;
                uint c = a + (uint)side;
                uint d = c + 1;
                mesh.Indices[t++] = a;
                mesh.Indices[t++] = c;
                mesh.Indices[t++] = b;
                mesh.Indices[t++] = b;
                mesh.Indices[t++] = c;
                mesh.Indices[t++] = d;
            }
        }

        return mesh;
    }

    /// <summary>Height gradient (sand → grass → rock → snow) darkened by slope.</summary>
    public static Vector4 ColorFor(float height, float heightScale, Vector3 normal)
    {
        float f = Math.Clamp(heightScale > 0f ? height / heightScale : 0f, 0f, 1f);
        Vector3 c = f switch
        {
            < 0.18f => Vector3.Lerp(Sand, Grass, Math.Clamp(f / 0.18f, 0f, 1f)),
            < 0.55f => Vector3.Lerp(Grass, DryGrass, (f - 0.18f) / 0.37f),
            < 0.80f => Vector3.Lerp(DryGrass, Rock, (f - 0.55f) / 0.25f),
            _ => Vector3.Lerp(Rock, Snow, (f - 0.80f) / 0.20f),
        };

        float slope = 1f - Math.Clamp(normal.Y, 0f, 1f);
        float rockiness = Math.Clamp((slope - 0.16f) / 0.30f, 0f, 1f);
        c = Vector3.Lerp(c, Rock, rockiness * 0.85f);
        return new Vector4(c, 1f);
    }
}
