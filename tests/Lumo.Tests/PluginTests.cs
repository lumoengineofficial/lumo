using Lumo.Engine.VisualScripting;
using Lumo.Plugins;

namespace Lumo.Tests;

[Collection("Blackboard")]
public class PluginTests
{
    private static string PreparePluginDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "LumoPluginTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string source = Path.Combine(AppContext.BaseDirectory, "Lumo.TestPlugin.dll");
        Assert.True(File.Exists(source), $"test plugin fixture missing: {source}");
        File.Copy(source, Path.Combine(dir, "Lumo.TestPlugin.dll"));
        return dir;
    }

    [Fact]
    public void LoadsPlugins_AndRunsLifecycle()
    {
        string dir = PreparePluginDir();
        try
        {
            var loader = new PluginLoader();
            var messages = new List<string>();
            loader.AttachSink(messages.Add);

            loader.LoadDirectory(dir);

            Assert.Contains(loader.Plugins, p => p.Id == "lumo.test.ok" && p.Success && p.Error == null);
            Assert.Contains(messages, m => m.Contains("Test OK Plugin"));
            Assert.True(NodeRegistry.TryGet("test.echo", out _), "plugin node must be registered");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FailingPlugin_DoesNotBlockOthers()
    {
        string dir = PreparePluginDir();
        try
        {
            var loader = new PluginLoader();
            var messages = new List<string>();
            loader.AttachSink(messages.Add);

            loader.LoadDirectory(dir);

            LoadedPlugin ok = Assert.Single(loader.Plugins, p => p.Id == "lumo.test.ok");
            Assert.True(ok.Success);
            LoadedPlugin bad = Assert.Single(loader.Plugins, p => p.Id == "lumo.test.throw");
            Assert.False(bad.Success);
            Assert.Contains("boom", bad.Error);
            Assert.Contains(messages, m => m.Contains("Plugin failed") && m.Contains("boom"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadDirectory_Twice_IsIdempotent()
    {
        string dir = PreparePluginDir();
        try
        {
            var loader = new PluginLoader();
            loader.LoadDirectory(dir);
            int pluginCount = loader.Plugins.Count;
            int messageCount = loader.Messages.Count;

            loader.LoadDirectory(dir);

            Assert.Equal(pluginCount, loader.Plugins.Count);
            Assert.Equal(messageCount, loader.Messages.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingDirectory_IsIgnored()
    {
        var loader = new PluginLoader();
        loader.LoadDirectory(Path.Combine(Path.GetTempPath(), "LumoNoPlugins" + Guid.NewGuid().ToString("N")));
        Assert.Empty(loader.Plugins);
        Assert.Empty(loader.Messages);
    }

    [Fact]
    public void PluginNode_RunsInInterpreter()
    {
        string dir = PreparePluginDir();
        try
        {
            new PluginLoader().LoadDirectory(dir);

            var graph = new VisualGraph { Name = "PluginGraph" };
            VSNode start = NodeRegistry.Get("event.start").Create();
            VSNode echo = NodeRegistry.Get("test.echo").Create();
            echo.Values["a"] = "7";
            VSNode log = NodeRegistry.Get("action.log").Create();
            graph.AddNode(start);
            graph.AddNode(echo);
            graph.AddNode(log);
            Assert.True(graph.AddConnection(start, "exec", log, "in"));
            Assert.True(graph.AddConnection(echo, "result", log, "message"));
            Assert.DoesNotContain(GraphValidator.Validate(graph), i => i.IsError);

            var interp = new GraphInterpreter();
            var logs = new List<string>();
            interp.MessageLogged += logs.Add;
            interp.AddGraph(graph);
            interp.Start();

            Assert.Contains("7", logs);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DetachSink_StopsDelivery()
    {
        string dir = PreparePluginDir();
        try
        {
            var loader = new PluginLoader();
            var messages = new List<string>();
            Action<string> sink = messages.Add;
            loader.AttachSink(sink);
            loader.LoadDirectory(dir);
            int afterLoad = messages.Count;
            Assert.True(afterLoad > 0);

            loader.DetachSink(sink);

            // Same loader, different path → loads again and emits, but the
            // detached sink must not see anything.
            string dir2 = PreparePluginDir();
            try { loader.LoadDirectory(dir2); }
            finally { Directory.Delete(dir2, true); }

            Assert.Equal(afterLoad, messages.Count);
        }
        finally { Directory.Delete(dir, true); }
    }
}
