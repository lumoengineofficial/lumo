using System.Numerics;
using Lumo.Engine.Physics;
using Lumo.Engine.Scene;
using Lumo.Engine.Scripting;
using Lumo.Engine.VisualScripting;

namespace Lumo.Tests;

/// <summary>Tests for the gameplay bridges: script HUD, world height, slide movement.</summary>
[Collection("Blackboard")]
public class GameScriptApiTests
{
    private const string HudScript = """
        using Lumo.Engine.Scripting;
        using System.Numerics;
        public class Huder : LumoScript
        {
            public override void OnStart() => SetHud("score", "3/10", 0.05f, 0.05f, 18f, new Vector3(1f, 0.9f, 0.2f));
            public override void OnDestroy() => RemoveHud("score");
        }
        """;

    private const string SlideScript = """
        using Lumo.Engine.Scripting;
        using System.Numerics;
        public class Slider : LumoScript
        {
            public override void OnStart()
            {
                Position = SlideMove(new Vector3(2f, 0f, 0f), 0.3f, out _);
                Position = SlideMove(new Vector3(0f, 0f, 10f), 0.3f, out _);
            }
        }
        """;

    private static Scene NewScene(string name = "Test") => new() { Name = name };

    private static Entity NewMeshEntity(Scene scene, string name, Vector3? pos = null, Vector3? scale = null)
    {
        var e = scene.CreateEntity(name);
        e.MeshRenderer = new MeshRendererComponent { MeshName = "Cube", IsVisible = true };
        if (pos is Vector3 p) e.Transform.Position = p;
        if (scale is Vector3 s) e.Transform.Scale = s;
        return e;
    }

    [Fact]
    public void World_HeightAt_DefaultsToZero_WithoutTerrain()
    {
        try
        {
            World.TerrainHeight = null;
            Assert.Equal(0f, World.HeightAt(5f, -3f));
        }
        finally { World.TerrainHeight = null; }
    }

    [Fact]
    public void World_HeightAt_UsesRegisteredSampler()
    {
        try
        {
            World.TerrainHeight = (x, z) => x * 2f + z;
            Assert.Equal(14f, World.HeightAt(5f, 4f));
        }
        finally { World.TerrainHeight = null; }
    }

    [Fact]
    public void Script_WritesAndRemovesHudThroughHost()
    {
        var hud = new HudLayer();
        ScriptHost.Hud = hud;
        try
        {
            var host = new ScriptHost();
            Assert.True(host.Compile(new[] { HudScript }), string.Join("; ", host.Errors));

            var scene = NewScene();
            var entity = scene.CreateEntity("Player");
            entity.Scripts = new ScriptComponent();
            entity.Scripts.ScriptNames.Add("Huder");
            host.Bind(scene);

            host.Start();
            var entry = hud.Get("score");
            Assert.NotNull(entry);
            Assert.Equal("3/10", entry!.Text);
            Assert.Equal(18f, entry.Size);

            host.Stop();
            Assert.Null(hud.Get("score"));
        }
        finally { ScriptHost.Hud = null; }
    }

    [Fact]
    public void PhysicsHelper_Collect_SkipsExcludedPrefix()
    {
        var scene = NewScene();
        NewMeshEntity(scene, "Wall");
        NewMeshEntity(scene, "Terrain_c0_0");

        var all = PhysicsHelper.Collect(scene, null, null);
        Assert.Equal(2, all.Count);

        var noTerrain = PhysicsHelper.Collect(scene, null, null, "", "Terrain");
        var obstacle = Assert.Single(noTerrain);
        Assert.Equal("Wall", obstacle.Entity.Name);
    }

    [Fact]
    public void Script_SlideMove_BlocksWall_SkipsTerrainChunks()
    {
        var host = new ScriptHost();
        Assert.True(host.Compile(new[] { SlideScript }), string.Join("; ", host.Errors));

        var scene = NewScene();
        var mover = scene.CreateEntity("Mover");
        mover.Scripts = new ScriptComponent();
        mover.Scripts.ScriptNames.Add("Slider");
        NewMeshEntity(scene, "Wall", pos: new Vector3(2f, 0f, 0f));
        NewMeshEntity(scene, "Terrain_c0_0", pos: new Vector3(0f, -3f, 0f), scale: new Vector3(64f, 6f, 64f));

        host.Bind(scene);
        host.Start();

        var p = mover.Transform.Position;
        Assert.Equal(0f, p.X, 3);   // wall blocked the +X move
        Assert.Equal(10f, p.Z, 3);  // terrain chunk AABB did not block

        host.Stop();
    }
}
