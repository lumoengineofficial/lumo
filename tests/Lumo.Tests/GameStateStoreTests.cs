using Lumo.Engine.Scene;
using Lumo.Engine.VisualScripting;

namespace Lumo.Tests;

/// <summary>
/// Runtime save/load: blackboard → JSON persistence, both through the store
/// API and through the system.save / system.load graph nodes.
/// </summary>
[Collection("Blackboard")]
public class GameStateStoreTests
{
    private static VSNode N(VisualGraph g, string type)
    {
        Assert.True(NodeRegistry.TryGet(type, out NodeDefinition? def), $"unknown node {type}");
        VSNode node = def!.Create();
        g.AddNode(node);
        return node;
    }

    [Fact]
    public void SaveLoad_RoundtripPreservesVariables()
    {
        string path = Path.Combine(Path.GetTempPath(), "LumoSaveTest", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var bb = Blackboard.Shared;
            bb.Set("gs_score", "7");
            bb.Set("gs_mode", "hard");

            GameStateStore.Save(path, bb);
            Assert.True(File.Exists(path));

            bb.Set("gs_score", "99");
            Dictionary<string, string> loaded = GameStateStore.Load(path);

            Assert.Equal("7", loaded["gs_score"]);
            Assert.Equal("hard", loaded["gs_mode"]);
            // in-memory value changed, file snapshot kept 7
            Assert.Equal("99", Blackboard.Shared.Get("gs_score"));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
        }
    }

    [Fact]
    public void SaveAndLoadNodes_WriteAndRestoreBlackboard()
    {
        string dir = Path.Combine(Path.GetTempPath(), "LumoSaveNodeTest", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "save.json");
        try
        {
            // event.start -> var.set(gs_score=7) -> system.save
            // (Start() clears the blackboard before dispatching, so the value
            //  must be produced by the graph itself)
            var saveGraph = new VisualGraph { Name = "save" };
            VSNode evSave = N(saveGraph, "event.start");
            VSNode setVar = N(saveGraph, "var.set");
            setVar.Values["name"] = "gs_score";
            setVar.Values["scope"] = "Blackboard";
            setVar.Values["value"] = "7";
            VSNode save = N(saveGraph, "system.save");
            save.Values["path"] = path;
            Assert.True(saveGraph.AddConnection(evSave, "exec", setVar, "in"));
            Assert.True(saveGraph.AddConnection(setVar, "exec", save, "in"));

            var logs = new List<string>();
            var interp1 = new GraphInterpreter { Scene = new Scene(), Input = new Engine.Input.InputState() };
            interp1.MessageLogged += logs.Add;
            interp1.AddGraph(saveGraph);
            interp1.Start();

            Assert.True(File.Exists(path), "system.save should create the file");
            Assert.Equal("7", GameStateStore.Load(path)["gs_score"]);
            Assert.Empty(interp1.Errors);

            // mutate, then event.start -> system.load restores
            Blackboard.Shared.Set("gs_score", "99");

            var loadGraph = new VisualGraph { Name = "load" };
            VSNode evLoad = N(loadGraph, "event.start");
            VSNode load = N(loadGraph, "system.load");
            load.Values["path"] = path;
            Assert.True(loadGraph.AddConnection(evLoad, "exec", load, "in"));

            var interp2 = new GraphInterpreter { Scene = new Scene(), Input = new Engine.Input.InputState() };
            interp2.MessageLogged += logs.Add;
            interp2.AddGraph(loadGraph);
            interp2.Start();

            Assert.Equal("7", Blackboard.Shared.Get("gs_score"));
            Assert.Empty(interp2.Errors);
            Assert.Contains(logs, l => l.Contains("Game loaded", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
