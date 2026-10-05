using Lumo.Engine.Assets;
using Lumo.Engine.VisualScripting;
using Lumo.Plugins;
using Lumo.TerrainPlugin;
using System.Numerics;

namespace Lumo.Tests;

public class TerrainTests
{
    private sealed class FakeBridge : IHostBridge
    {
        public string? ProjectPath { get; set; }
        public List<string> Messages { get; } = [];
        public HashSet<string> Entities { get; } = new(StringComparer.Ordinal);
        public List<string> Created { get; } = [];
        public List<string> DeletedPrefixes { get; } = [];
        public int UndoCount { get; private set; }
        public int SaveCount { get; private set; }
        public int RefreshCount { get; private set; }

        public void Log(string message) => Messages.Add(message);
        public void PushUndo() => UndoCount++;
        public void SaveProject() => SaveCount++;
        public void RefreshUi() => RefreshCount++;
        public bool EntityExists(string entityName) => Entities.Contains(entityName);

        public void CreateOrUpdateMeshEntity(string entityName, string meshName)
        {
            Entities.Add(entityName);
            Created.Add(entityName);
        }

        public void DeleteEntitiesByPrefix(string prefix)
        {
            DeletedPrefixes.Add(prefix);
            Entities.RemoveWhere(e => e.StartsWith(prefix, StringComparison.Ordinal));
        }

        public Task<string?> PickOpenFileAsync(string title, params (string Label, string[] Patterns)[] filters)
            => Task.FromResult<string?>(null);
    }

    public TerrainTests()
    {
        TerrainSystem.Reset();
        PluginCommands.UnregisterPlugin("lumo.terrain");
    }

    [Fact]
    public void Generator_SameSeed_IsDeterministic_DifferentSeed_Differs()
    {
        var a = TerrainGenerator.Generate(65, 64f, 14f, seed: 42);
        var b = TerrainGenerator.Generate(65, 64f, 14f, seed: 42);
        var c = TerrainGenerator.Generate(65, 64f, 14f, seed: 43);

        Assert.Equal(a.Data, b.Data);
        Assert.NotEqual(a.Data, c.Data);
    }

    [Fact]
    public void Generator_HeightsStayWithinScale()
    {
        var map = TerrainGenerator.Generate(97, 64f, 14f, seed: 7);
        Assert.All(map.Data, h => Assert.InRange(h, 0f, 14f));
    }

    [Fact]
    public void Heightmap_SaveLoad_RoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), "lumo_terrain_" + Guid.NewGuid().ToString("N") + ".hmap");
        try
        {
            var map = new Heightmap(9, 7, 32f, 8f);
            var rng = new Random(5);
            for (int i = 0; i < map.Data.Length; i++) map.Data[i] = (float)rng.NextDouble() * 8f;
            map.Save(path);

            var loaded = Heightmap.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal(9, loaded.Width);
            Assert.Equal(7, loaded.Height);
            Assert.Equal(32f, loaded.WorldSize);
            Assert.Equal(8f, loaded.HeightScale);
            Assert.Equal(map.Data, loaded.Data);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Mesher_BuildsValidChunk()
    {
        var map = TerrainGenerator.Generate(129, 64f, 14f, seed: 11);
        var mesh = TerrainMesher.BuildChunk(map, 0, 0, 32, "Terrain_c0_0");

        Assert.Equal(33 * 33, mesh.VertexCount);
        Assert.Equal(32 * 32 * 2, mesh.TriangleCount);
        Assert.Equal(mesh.VertexCount * 4, mesh.VertexColors.Length);
        Assert.Equal(mesh.VertexCount * 3, mesh.Normals.Length);
        Assert.All(mesh.VertexColors, v => Assert.InRange(v, 0f, 1f));
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0u, (uint)mesh.VertexCount - 1));
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            float x = mesh.Vertices[v * 3];
            float y = mesh.Vertices[v * 3 + 1];
            float z = mesh.Vertices[v * 3 + 2];
            Assert.InRange(x, -32f, 32f);
            Assert.InRange(z, -32f, 32f);
            Assert.InRange(y, 0f, 14f);
        }
    }

    [Fact]
    public void Mesher_ClampsAtEdges_ForNonAlignedMaps()
    {
        var map = new Heightmap(100, 60, 48f, 10f);
        var mesh = TerrainMesher.BuildChunk(map, 3, 1, 32, "Edge");

        Assert.Equal(33 * 33, mesh.VertexCount);
        Assert.All(mesh.Indices, i => Assert.InRange(i, 0u, (uint)mesh.VertexCount - 1));
    }

    [Fact]
    public void Brush_Raise_OnlyTouchesRadiusAndMarksDirtyChunks()
    {
        var map = new Heightmap(129, 129, 64f, 14f);
        for (int i = 0; i < map.Data.Length; i++) map.Data[i] = 7f;
        var dirty = new HashSet<(int X, int Z)>();

        TerrainBrush.Apply(map, BrushOp.Raise, 0f, 0f, radius: 6f, strength: 2f, flattenTarget: 0f, cellsPerChunk: 32, dirty);

        Assert.Equal(7f, map.Data[0]);
        Assert.True(map.Data[64 * 129 + 64] > 7f, "center should rise");
        Assert.NotEmpty(dirty);
        Assert.Contains((1, 1), dirty);
        Assert.Contains((2, 2), dirty);
        Assert.DoesNotContain((0, 0), dirty);
    }

    [Fact]
    public void Brush_Lower_ClampsAtZero()
    {
        var map = new Heightmap(65, 65, 64f, 14f);
        var dirty = new HashSet<(int X, int Z)>();

        TerrainBrush.Apply(map, BrushOp.Lower, 0f, 0f, 32f, strength: 100f, flattenTarget: 0f, cellsPerChunk: 32, dirty);

        Assert.All(map.Data, h => Assert.True(h >= 0f, $"height went negative: {h}"));
    }

    [Fact]
    public void Generate_CreatesEntitiesMeshesAndHeightmapFile()
    {
        string project = Path.Combine(Path.GetTempPath(), "lumo_terrain_proj_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(project);
        try
        {
            var bridge = new FakeBridge { ProjectPath = project };

            TerrainSystem.Generate(bridge, seed: 7);

            Assert.Contains("Terrain", bridge.DeletedPrefixes);
            int expectedChunks = TerrainSystem.ChunkCountX * TerrainSystem.ChunkCountZ;
            Assert.Equal(expectedChunks, bridge.Created.Count);
            Assert.Equal(expectedChunks, bridge.Entities.Count);
            Assert.True(MeshLibrary.Contains("Terrain_c0_0"));
            Assert.True(File.Exists(TerrainSystem.HeightmapPath(project)));
            Assert.True(bridge.UndoCount > 0);
            Assert.True(bridge.SaveCount > 0);
            Assert.True(bridge.RefreshCount > 0);
            Assert.Contains(bridge.Messages, m => m.StartsWith("Terrain generated"));

            TerrainSystem.Reset();
            TerrainSystem.TryRestore(project, bridge.Log);
            Assert.NotNull(TerrainSystem.Map);
            Assert.True(MeshLibrary.Contains("Terrain_c0_0"));

            TerrainSystem.SyncEntities(bridge, replace: false);
            Assert.Equal(expectedChunks, bridge.Entities.Count);
            int undoAfterFirstSync = bridge.UndoCount;
            TerrainSystem.SyncEntities(bridge, replace: false);
            Assert.Equal(undoAfterFirstSync, bridge.UndoCount);
        }
        finally { Directory.Delete(project, true); TerrainSystem.Reset(); }
    }

    [Fact]
    public async Task Import_WithoutPickedFile_LogsCancellation()
    {
        var bridge = new FakeBridge { ProjectPath = Path.GetTempPath() };
        await TerrainSystem.ImportAsync(bridge);
        Assert.Contains(bridge.Messages, m => m.Contains("cancelled"));
        Assert.Null(TerrainSystem.Map);
    }

    [Fact]
    public void Raycast_StraightDown_HitsTerrainHeight()
    {
        var bridge = new FakeBridge { ProjectPath = Path.GetTempPath() };
        TerrainSystem.Generate(bridge, seed: 3);

        float h = TerrainSystem.SampleHeight(0f, 0f);
        bool hit = TerrainSystem.Raycast(new Vector3(0f, h + 10f, 0f), new Vector3(0f, -1f, 0f), out var point);

        Assert.True(hit);
        Assert.Equal(h, point.Y, 3);
        Assert.False(TerrainSystem.Raycast(new Vector3(0f, h + 10f, 0f), new Vector3(0f, 1f, 0f), out _));
    }

    [Fact]
    public void HandlePointer_SculptStroke_RaisesTerrain()
    {
        var bridge = new FakeBridge { ProjectPath = Path.GetTempPath() };
        TerrainSystem.Generate(bridge, seed: 5);
        TerrainSystem.SetBrush("raise");
        float before = TerrainSystem.SampleHeight(0f, 0f);

        var down = new ViewportPointerArgs
        {
            Kind = ViewportPointerKind.Down,
            LeftPressed = true,
            HasRay = true,
            RayOrigin = new Vector3(0f, before + 10f, 0f),
            RayDir = new Vector3(0f, -1f, 0f),
        };
        Assert.True(TerrainSystem.HandlePointer(down, bridge));

        float after = TerrainSystem.SampleHeight(0f, 0f);
        Assert.True(after > before, $"expected raise: {before} -> {after}");
        int undoBeforeMove = bridge.UndoCount;

        var move = down with { Kind = ViewportPointerKind.Move };
        Assert.True(TerrainSystem.HandlePointer(move, bridge));
        Assert.Equal(undoBeforeMove, bridge.UndoCount);

        var up = down with { Kind = ViewportPointerKind.Up, LeftPressed = false };
        Assert.True(TerrainSystem.HandlePointer(up, bridge));
        Assert.False(TerrainSystem.HandlePointer(move, bridge));

        Assert.True(TerrainSystem.HandlePointer(down with { LeftPressed = true }, bridge));
        Assert.True(TerrainSystem.HandlePointer(up, bridge));

        TerrainSystem.SetBrush(null);
        Assert.False(TerrainSystem.HandlePointer(down with { LeftPressed = true }, bridge));
    }

    [Fact]
    public void PluginCommands_RegisterInvoke_AndDynamicLabel()
    {
        bool toggled = false;
        int runs = 0;
        int changes = 0;
        void OnChanged() => changes++;
        PluginCommands.Changed += OnChanged;
        try
        {
            PluginCommands.Register(new PluginCommand
            {
                PluginId = "test.thing",
                Id = "test.thing.cmd",
                Label = "Cmd",
                DynamicLabel = () => toggled ? "● Cmd" : "Cmd",
                Callback = () => { runs++; toggled = !toggled; },
            });
            Assert.Equal(1, changes);
            Assert.Contains(PluginCommands.Commands, c => c.Id == "test.thing.cmd" && c.Display == "Cmd");

            Assert.True(PluginCommands.TryInvoke("test.thing.cmd"));
            Assert.Equal(1, runs);
            Assert.Contains(PluginCommands.Commands, c => c.Id == "test.thing.cmd" && c.Display == "● Cmd");

            PluginCommands.UnregisterPlugin("test.thing");
            Assert.Equal(2, changes);
            Assert.False(PluginCommands.TryInvoke("test.thing.cmd"));
        }
        finally { PluginCommands.Changed -= OnChanged; PluginCommands.UnregisterPlugin("test.thing"); }
    }

    [Fact]
    public void TerrainPlugin_Loads_RegistersCommandsNodesAndRestoresHeightmap()
    {
        string pluginDir = Path.Combine(Path.GetTempPath(), "LumoTerrainPluginTests", Guid.NewGuid().ToString("N"));
        string projectDir = Path.Combine(pluginDir, "Project");
        Directory.CreateDirectory(pluginDir);
        Directory.CreateDirectory(projectDir);
        try
        {
            string source = Path.Combine(AppContext.BaseDirectory, "Lumo.TerrainPlugin.dll");
            Assert.True(File.Exists(source), $"terrain plugin missing: {source}");
            File.Copy(source, Path.Combine(pluginDir, "Lumo.TerrainPlugin.dll"));

            new Heightmap(33, 33, 64f, 14f).Save(TerrainSystem.HeightmapPath(projectDir));

            var loader = new PluginLoader();
            var messages = new List<string>();
            loader.AttachSink(messages.Add);
            loader.LoadDirectory(pluginDir, projectDir);

            Assert.Contains(loader.Plugins, p => p.Id == "lumo.terrain" && p.Success && p.Error == null);
            Assert.Contains(PluginCommands.Commands, c => c.Id == "lumo.terrain.generate");
            Assert.Contains(PluginCommands.Commands, c => c.Id == "lumo.terrain.brush.raise");
            Assert.Contains(messages, m => m.Contains("Terrain restored"));
            Assert.True(MeshLibrary.Contains("Terrain_c0_0"), "restore must register chunk meshes");
            Assert.True(NodeRegistry.TryGet("terrain.height", out _));
        }
        finally
        {
            PluginCommands.UnregisterPlugin("lumo.terrain");
            Directory.Delete(pluginDir, true);
        }
    }
}
