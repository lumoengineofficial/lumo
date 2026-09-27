using Lumo.Engine.Scene;
using Lumo.Engine.VisualScripting;
using Lumo.Runtime;

namespace Lumo.Tests;

/// <summary>
/// Headless tests for the standalone GameRuntime: project loading (scene +
/// graph files), graph dispatch on Tick, and clean lifecycle.
/// </summary>
public class RuntimeTests
{
    [Fact]
    public void LoadsProjectRunsGraphsAndTicks()
    {
        string dir = Path.Combine(Path.GetTempPath(), "LumoRuntimeTest", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "Project.json"), """{"Name":"RuntimeTest"}""");

            var scene = new Scene { Name = "Test" };
            Entity player = scene.CreateEntity("Player");
            player.Transform.Position = new System.Numerics.Vector3(0, 0, 0);
            player.SpriteRenderer = new SpriteRendererComponent
            {
                Width = 1,
                Height = 1,
                Color = System.Numerics.Vector3.One,
                IsVisible = true
            };
            Directory.CreateDirectory(Path.Combine(dir, "Scenes"));
            scene.Save(Path.Combine(dir, "Scenes", "main.scene.json"));

            Directory.CreateDirectory(Path.Combine(dir, "Graphs"));
            var graph = new VisualGraph { Name = "game" };
            Assert.True(NodeRegistry.TryGet("event.tick", out NodeDefinition? evDef), "event.tick exists");
            Assert.True(NodeRegistry.TryGet("action.log", out NodeDefinition? logDef), "action.log exists");
            VSNode ev = evDef!.Create();
            ev.X = 40;
            ev.Y = 40;
            VSNode log = logDef!.Create();
            log.X = 300;
            log.Y = 40;
            log.Values["message"] = "tick";
            graph.AddNode(ev);
            graph.AddNode(log);
            Assert.True(graph.AddConnection(ev, "exec", log, "in"));
            graph.Save(Path.Combine(dir, "Graphs", "game.graph.json"));

            using var runtime = new GameRuntime();
            var lines = new List<string>();
            runtime.MessageLogged += msg => { lock (lines) lines.Add(msg); };

            runtime.LoadProject(dir);
            Assert.Equal("RuntimeTest", runtime.Title);
            Assert.Single(runtime.Scene.AllEntities);

            runtime.Start();
            Assert.Equal(1, runtime.GraphCount);
            Assert.Empty(runtime.GraphErrors);

            for (int i = 0; i < 3; i++)
                runtime.Tick();

            lock (lines)
                Assert.Contains(lines, l => l.Contains("tick", StringComparison.Ordinal));
            Assert.Empty(runtime.GraphErrors);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void SnakeSampleProjectLoadsIfPresent()
    {
        string snake = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "LumoProjects", "SnakeSample");
        if (!Directory.Exists(snake))
            return;

        using var runtime = new GameRuntime();
        runtime.LoadProject(snake);
        Assert.Null(runtime.LoadError);
        Assert.True(runtime.Scene.AllEntities.Count >= 8);

        runtime.Start();
        Assert.True(runtime.GraphCount >= 1);
        Assert.Empty(runtime.GraphErrors);

        for (int i = 0; i < 10; i++)
            runtime.Tick();

        Assert.Empty(runtime.GraphErrors);
    }
}
