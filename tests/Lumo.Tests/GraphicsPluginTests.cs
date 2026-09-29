using Lumo.Engine.Rendering;
using Lumo.Engine.VisualScripting;
using Lumo.Plugins;

namespace Lumo.Tests;

[Collection("Blackboard")]
public class GraphicsPluginTests
{
    public GraphicsPluginTests()
    {
        FxRegistry.Clear();
        FxRegistry.MeshShading = false;
    }

    private static string PreparePluginDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "LumoGraphicsPluginTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string source = Path.Combine(AppContext.BaseDirectory, "Lumo.GraphicsPlugin.dll");
        Assert.True(File.Exists(source), $"graphics plugin fixture missing: {source}");
        File.Copy(source, Path.Combine(dir, "Lumo.GraphicsPlugin.dll"));
        return dir;
    }

    [Fact]
    public void Load_EnablesFxAndRegistersNodes()
    {
        string dir = PreparePluginDir();
        try
        {
            var loader = new PluginLoader();
            var messages = new List<string>();
            loader.AttachSink(messages.Add);

            loader.LoadDirectory(dir);

            Assert.Contains(loader.Plugins, p => p.Id == "lumo.graphics" && p.Success && p.Error == null);
            Assert.Contains(messages, m => m.Contains("Graphics fx enabled"));

            Assert.True(FxRegistry.MeshShading, "plugin load must turn on mesh shading");
            var effects = FxRegistry.Snapshot();
            FxEffect vignette = Assert.Single(effects, e => e.Id == "vignette");
            Assert.Equal(FxKind.Vignette, vignette.Kind);
            Assert.Equal(0.65f, vignette.Intensity, 3);
            FxEffect grade = Assert.Single(effects, e => e.Id == "grade");
            Assert.Equal(FxKind.ColorGrade, grade.Kind);

            Assert.True(NodeRegistry.TryGet("gfx.vignette", out _), "vignette node must be registered");
            Assert.True(NodeRegistry.TryGet("gfx.grade", out _), "grade node must be registered");
            Assert.True(NodeRegistry.TryGet("gfx.shading", out _), "shading node must be registered");
        }
        finally
        {
            FxRegistry.Clear();
            FxRegistry.MeshShading = false;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Unload_RestoresDefaults()
    {
        string dir = PreparePluginDir();
        try
        {
            var loader = new PluginLoader();
            loader.LoadDirectory(dir);
            Assert.True(FxRegistry.MeshShading);

            loader.UnloadAll();

            Assert.False(FxRegistry.MeshShading);
            Assert.Empty(FxRegistry.Snapshot());
        }
        finally
        {
            FxRegistry.Clear();
            FxRegistry.MeshShading = false;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void GfxNodes_RunInGraph()
    {
        string dir = PreparePluginDir();
        try
        {
            new PluginLoader().LoadDirectory(dir);

            var graph = new VisualGraph { Name = "GfxGraph" };
            VSNode start = NodeRegistry.Get("event.start").Create();
            VSNode shading = NodeRegistry.Get("gfx.shading").Create();
            shading.Values["enabled"] = "false";
            VSNode vignette = NodeRegistry.Get("gfx.vignette").Create();
            vignette.Values["intensity"] = "0.31";
            VSNode grade = NodeRegistry.Get("gfx.grade").Create();
            grade.Values["strength"] = "0.2";
            VSNode log = NodeRegistry.Get("action.log").Create();
            log.Values["message"] = "fx set";
            graph.AddNode(start);
            graph.AddNode(shading);
            graph.AddNode(vignette);
            graph.AddNode(grade);
            graph.AddNode(log);
            Assert.True(graph.AddConnection(start, "exec", shading, "in"));
            Assert.True(graph.AddConnection(shading, "exec", vignette, "in"));
            Assert.True(graph.AddConnection(vignette, "exec", grade, "in"));
            Assert.True(graph.AddConnection(grade, "exec", log, "in"));
            Assert.DoesNotContain(GraphValidator.Validate(graph), i => i.IsError);

            var interp = new GraphInterpreter();
            var logs = new List<string>();
            interp.MessageLogged += logs.Add;
            interp.AddGraph(graph);
            interp.Start();

            Assert.Contains("fx set", logs);
            Assert.False(FxRegistry.MeshShading, "gfx.shading false must apply");
            Assert.Equal(0.31f, Assert.Single(FxRegistry.Snapshot(), e => e.Id == "vignette").Intensity, 3);
            Assert.Equal(0.2f, Assert.Single(FxRegistry.Snapshot(), e => e.Id == "grade").Intensity, 3);
            Assert.Empty(interp.Errors);
        }
        finally
        {
            FxRegistry.Clear();
            FxRegistry.MeshShading = false;
            Directory.Delete(dir, true);
        }
    }
}
