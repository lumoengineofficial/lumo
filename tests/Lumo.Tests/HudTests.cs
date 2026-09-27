using System.Numerics;
using Lumo.Engine.VisualScripting;

namespace Lumo.Tests;

[Collection("Blackboard")]
public class HudTests
{
    private static VisualGraph NewGraph(string name = "HudTest") => new() { Name = name };

    private static VSNode Add(VisualGraph g, string type, double x = 0, double y = 0,
        params (string pin, string value)[] values)
    {
        Assert.True(NodeRegistry.TryGet(type, out NodeDefinition? def), $"unknown node {type}");
        VSNode node = def.Create();
        node.X = x;
        node.Y = y;
        foreach ((string pin, string value) in values)
            node.Values[pin] = value;
        g.AddNode(node);
        return node;
    }

    private static bool Link(VisualGraph g, VSNode a, string aPin, VSNode b, string bPin) =>
        g.AddConnection(a, aPin, b, bPin);

    private static GraphInterpreter Run(VisualGraph g)
    {
        Assert.DoesNotContain(GraphValidator.Validate(g), i => i.IsError);
        var interp = new GraphInterpreter();
        interp.AddGraph(g);
        interp.Start();
        return interp;
    }

    [Fact]
    public void HudLayer_SetOverwritesRemoveClear()
    {
        var hud = new HudLayer();

        hud.Set("score", "10", 0.1f, 0.2f, 32f, new Vector3(1f, 0.5f, 0f));
        Assert.Equal(1, hud.Count);
        HudEntry? entry = hud.Get("score");
        Assert.NotNull(entry);
        Assert.Equal("10", entry!.Text);
        Assert.Equal(0.1f, entry.X, 3);
        Assert.Equal(0.2f, entry.Y, 3);
        Assert.Equal(32f, entry.Size);
        Assert.Equal(new Vector3(1f, 0.5f, 0f), entry.Color);

        hud.Set("score", "20", 0.1f, 0.2f, 32f, Vector3.One);
        Assert.Equal(1, hud.Count);
        Assert.Equal("20", hud.Get("score")!.Text);

        hud.Set("title", "Lumo", 0.5f, 0.1f, 16f, Vector3.One);
        Assert.Equal(2, hud.Count);

        hud.Remove("score");
        Assert.Null(hud.Get("score"));
        Assert.Single(hud.Entries);

        hud.ClearAll();
        Assert.Equal(0, hud.Count);
    }

    [Fact]
    public void HudLayer_SizeClampedToMinimum()
    {
        var hud = new HudLayer();
        hud.Set("tiny", "x", 0, 0, 1f, Vector3.One);
        Assert.Equal(4f, hud.Get("tiny")!.Size);
    }

    [Fact]
    public void HudTextNode_WritesEntryOnStart()
    {
        var g = NewGraph();
        VSNode ev = Add(g, "event.start");
        VSNode hud = Add(g, "hud.text", 300, 0,
            ("id", "score"), ("text", "Hi"), ("x", "0.25"), ("y", "0.5"),
            ("size", "48"), ("r", "1"), ("g", "0.2"), ("b", "0"));
        Assert.True(Link(g, ev, "exec", hud, "in"));

        GraphInterpreter interp = Run(g);

        HudEntry? entry = interp.Hud.Get("score");
        Assert.NotNull(entry);
        Assert.Equal("Hi", entry!.Text);
        Assert.Equal(0.25f, entry.X, 3);
        Assert.Equal(0.5f, entry.Y, 3);
        Assert.Equal(48f, entry.Size);
        Assert.Equal(new Vector3(1f, 0.2f, 0f), entry.Color);
    }

    [Fact]
    public void ConcatNode_FeedsHudTextPin()
    {
        var g = NewGraph();
        VSNode ev = Add(g, "event.start");
        VSNode cat = Add(g, "value.concat", 300, 0, ("a", "Score: "), ("b", "42"));
        VSNode hud = Add(g, "hud.text", 560, 0, ("id", "score"));
        Assert.True(Link(g, ev, "exec", hud, "in"));
        Assert.True(Link(g, cat, "result", hud, "text"));

        GraphInterpreter interp = Run(g);

        Assert.Equal("Score: 42", interp.Hud.Get("score")!.Text);
    }

    [Fact]
    public void HudRemoveNode_DeletesEntryInSameChain()
    {
        var g = NewGraph();
        VSNode ev = Add(g, "event.start");
        VSNode hud = Add(g, "hud.text", 300, 0, ("id", "temp"), ("text", "x"));
        VSNode del = Add(g, "hud.remove", 560, 0, ("id", "temp"));
        Assert.True(Link(g, ev, "exec", hud, "in"));
        Assert.True(Link(g, hud, "exec", del, "in"));

        GraphInterpreter interp = Run(g);

        Assert.Null(interp.Hud.Get("temp"));
        Assert.Equal(0, interp.Hud.Count);
    }

    [Fact]
    public void Start_ClearsPreviousHudEntries()
    {
        var g = NewGraph();
        VSNode ev = Add(g, "event.start");
        VSNode hud = Add(g, "hud.text", 300, 0, ("id", "score"), ("text", "1"));
        Assert.True(Link(g, ev, "exec", hud, "in"));

        var interp = new GraphInterpreter();
        interp.AddGraph(g);
        interp.Start();
        Assert.Equal(1, interp.Hud.Count);

        interp.Hud.Set("stale", "leftover", 0, 0, 20f, Vector3.One);
        interp.Start();
        Assert.Null(interp.Hud.Get("stale"));
        Assert.Equal(1, interp.Hud.Count);
        Assert.Equal("1", interp.Hud.Get("score")!.Text);
    }
}
