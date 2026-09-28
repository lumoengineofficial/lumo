using Lumo.Engine.Assets;

namespace Lumo.Tests;

public class ObjImporterTests
{
    private const string TriangleObj = """
        # simple triangle
        v 0 0 0
        v 1 0 0
        v 0 1 0
        f 1 2 3
        """;

    private static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "LumoObjTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Load_ReadsPositionsAndFaces()
    {
        string dir = TempDir();
        try
        {
            string file = Path.Combine(dir, "tri.obj");
            File.WriteAllText(file, TriangleObj);

            var mesh = ObjImporter.Load(file);

            Assert.Equal("tri", mesh.Name);
            Assert.Equal(3, mesh.VertexCount);
            Assert.Equal(1, mesh.TriangleCount);
            Assert.Equal(9, mesh.Vertices.Length);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ObjImporter.Load(Path.Combine(Path.GetTempPath(), "LumoNope_" + Guid.NewGuid().ToString("N") + ".obj")));
    }

    [Fact]
    public void RegisterDirectory_RegistersGoodFiles_SkipsBroken()
    {
        string dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "good.obj"), TriangleObj);
            File.WriteAllText(Path.Combine(dir, "broken.obj"), "v 0 0 0\n"); // no faces
            string sub = Path.Combine(dir, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "nested.obj"), TriangleObj);

            int count = ObjImporter.RegisterDirectory(dir);

            Assert.Equal(2, count);
            Assert.True(MeshLibrary.Contains("good"));
            Assert.True(MeshLibrary.Contains("nested"));
            Assert.False(MeshLibrary.Contains("broken"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void RegisterDirectory_MissingDirectory_ReturnsZero()
    {
        Assert.Equal(0, ObjImporter.RegisterDirectory(
            Path.Combine(Path.GetTempPath(), "LumoNoDir_" + Guid.NewGuid().ToString("N"))));
    }
}
