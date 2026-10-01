using Lumo.Engine.Assets;

namespace Lumo.Tests;

public class GltfImporterTests
{
    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "LumoGltfTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Writes a .gltf with one triangle in an external .bin buffer.</summary>
    private static string WriteTriangleGltf(string dir, string name = "tri")
    {
        // Layout: 36 bytes positions, 36 bytes zero normals, 6 bytes ushort indices.
        var payload = new byte[78];
        Buffer.BlockCopy(BitConverter.GetBytes(0f), 0, payload, 0, 4);   // v0.x
        Buffer.BlockCopy(BitConverter.GetBytes(1f), 0, payload, 12, 4);  // v1.x
        Buffer.BlockCopy(BitConverter.GetBytes(1f), 0, payload, 28, 4);  // v2.y
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)1), 0, payload, 72, 2);
        Buffer.BlockCopy(BitConverter.GetBytes((ushort)2), 0, payload, 74, 2);

        File.WriteAllBytes(Path.Combine(dir, name + ".bin"), payload);

        string json = $$"""
            {
              "asset": { "version": "2.0" },
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "mesh": 0, "translation": [ 10, 0, 0 ] } ],
              "meshes": [ {
                "primitives": [ {
                  "attributes": { "POSITION": 0, "NORMAL": 1 },
                  "indices": 2
                } ]
              } ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3",
                  "min": [ 0, 0, 0 ], "max": [ 1, 1, 0 ] },
                { "bufferView": 1, "componentType": 5126, "count": 3, "type": "VEC3" },
                { "bufferView": 2, "componentType": 5123, "count": 3, "type": "SCALAR" }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0,  "byteLength": 36 },
                { "buffer": 0, "byteOffset": 36, "byteLength": 36 },
                { "buffer": 0, "byteOffset": 72, "byteLength": 6 }
              ],
              "buffers": [ { "uri": "{{name}}.bin", "byteLength": 78 } ]
            }
            """;
        string file = Path.Combine(dir, name + ".gltf");
        File.WriteAllText(file, json);
        return file;
    }

    [Fact]
    public void Load_ReadsGeometryAndBakesNodeTransform()
    {
        string dir = TempDir();
        try
        {
            string file = WriteTriangleGltf(dir);

            var mesh = GltfImporter.Load(file);

            Assert.Equal("tri", mesh.Name);
            Assert.Equal(3, mesh.VertexCount);
            Assert.Equal(1, mesh.TriangleCount);
            Assert.Equal(9, mesh.Normals.Length);
            // node translation [10,0,0] must be baked into positions
            Assert.Equal(10f, mesh.Vertices[0], 3);
            Assert.Equal(11f, mesh.Vertices[3], 3);
            Assert.Equal(10f, mesh.Vertices[6], 3);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            GltfImporter.Load(Path.Combine(Path.GetTempPath(), "LumoNope_" + Guid.NewGuid().ToString("N") + ".gltf")));
    }

    [Fact]
    public void Load_UnderBudget_LeavesMeshUntouched()
    {
        string dir = TempDir();
        try
        {
            string file = WriteTriangleGltf(dir);

            var mesh = GltfImporter.Load(file, out int original);

            Assert.Equal(1, original);
            Assert.Equal(1, mesh.TriangleCount);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_DenseMesh_IsDecimatedTowardBudget()
    {
        string dir = TempDir();
        try
        {
            string file = WriteDenseGridGltf(dir, grid: 120); // 28,800 triangles

            var mesh = GltfImporter.Load(file, out int original, maxTriangles: 2_000);

            Assert.Equal(28_800, original);
            Assert.True(mesh.TriangleCount < original, $"expected {mesh.TriangleCount} < {original}");
            Assert.True(mesh.TriangleCount > 0, "decimated mesh must keep geometry");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GetReferencedUris_ReturnsBufferAndImageUris_SkipsDataUris()
    {
        string dir = TempDir();
        try
        {
            string file = WriteTriangleGltf(dir);
            string json = File.ReadAllText(file).Replace(
                "\"buffers\": [",
                "\"images\": [ { \"uri\": \"textures/tree_diff.jpg\" } ], \"buffers\": [");
            File.WriteAllText(file, json);

            var uris = GltfImporter.GetReferencedUris(file);

            Assert.Contains("tri.bin", uris);
            Assert.Contains("textures/tree_diff.jpg", uris);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RegisterDirectory_RegistersGltf_SkipsBroken()
    {
        string dir = TempDir();
        try
        {
            WriteTriangleGltf(dir, "good");
            File.WriteAllText(Path.Combine(dir, "broken.gltf"), "{ not json");

            int count = GltfImporter.RegisterDirectory(dir);

            Assert.Equal(1, count);
            Assert.True(MeshLibrary.Contains("good"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RegisterDirectory_MissingDirectory_ReturnsZero()
    {
        Assert.Equal(0, GltfImporter.RegisterDirectory(
            Path.Combine(Path.GetTempPath(), "LumoGltfNoDir_" + Guid.NewGuid().ToString("N"))));
    }

    /// <summary>Writes a .gltf whose POSITION buffer is a grid of quads (2 tris each).</summary>
    private static string WriteDenseGridGltf(string dir, int grid)
    {
        int quads = grid * grid;
        int verts = (grid + 1) * (grid + 1);
        var payload = new byte[verts * 12 + quads * 6 * 2];

        int p = 0;
        for (int y = 0; y <= grid; y++)
            for (int x = 0; x <= grid; x++)
            {
                WriteF(payload, ref p, x);
                WriteF(payload, ref p, y);
                WriteF(payload, ref p, 0f);
            }

        int ip = verts * 12;
        for (int y = 0; y < grid; y++)
            for (int x = 0; x < grid; x++)
            {
                ushort a = (ushort)(y * (grid + 1) + x);
                ushort b = (ushort)(a + 1);
                ushort c = (ushort)(a + grid + 1);
                ushort d = (ushort)(c + 1);
                WriteU(payload, ref ip, a); WriteU(payload, ref ip, b); WriteU(payload, ref ip, c);
                WriteU(payload, ref ip, b); WriteU(payload, ref ip, d); WriteU(payload, ref ip, c);
            }

        File.WriteAllBytes(Path.Combine(dir, "grid.bin"), payload);

        int posLen = verts * 12;
        string json = $$"""
            {
              "asset": { "version": "2.0" },
              "scenes": [ { "nodes": [ 0 ] } ],
              "nodes": [ { "mesh": 0 } ],
              "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1 } ] } ],
              "accessors": [
                { "bufferView": 0, "componentType": 5126, "count": {{verts}}, "type": "VEC3" },
                { "bufferView": 1, "componentType": 5123, "count": {{quads * 6}}, "type": "SCALAR" }
              ],
              "bufferViews": [
                { "buffer": 0, "byteOffset": 0, "byteLength": {{posLen}} },
                { "buffer": 0, "byteOffset": {{posLen}}, "byteLength": {{quads * 6 * 2}} }
              ],
              "buffers": [ { "uri": "grid.bin", "byteLength": {{payload.Length}} } ]
            }
            """;
        string file = Path.Combine(dir, "grid.gltf");
        File.WriteAllText(file, json);
        return file;
    }

    private static void WriteF(byte[] buf, ref int p, float v)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(v), 0, buf, p, 4);
        p += 4;
    }

    private static void WriteU(byte[] buf, ref int p, ushort v)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(v), 0, buf, p, 2);
        p += 2;
    }
}
