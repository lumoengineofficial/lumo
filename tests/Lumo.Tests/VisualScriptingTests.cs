using System.Numerics;
using Lumo.Engine.Input;
using Lumo.Engine.Scene;
using Lumo.Engine.VisualScripting;

namespace Lumo.Tests;

[Collection("Blackboard")]
public class VisualScriptingTests
{
    private static VisualGraph NewGraph(string name = "Test") => new() { Name = name };

    private static T Node<T>(VisualGraph graph, double x = 0, double y = 0) where T : VSNode
    {
        VSNode node = NodeRegistry.Create(NodeRegistry.GetByClrType(typeof(T)).Type);
        node.X = x;
        node.Y = y;
        graph.AddNode(node);
        return (T)node;
    }

    private static bool Link(VisualGraph graph, VSNode from, string fromPin, VSNode to, string toPin) =>
        graph.AddConnection(from, fromPin, to, toPin);

    private static (GraphInterpreter interp, List<string> logs) Run(Action<VisualGraph> build, Scene? scene = null, InputState? input = null)
    {
        var graph = NewGraph();
        build(graph);
        Assert.DoesNotContain(GraphValidator.Validate(graph), i => i.IsError);

        var interp = new GraphInterpreter { Scene = scene, Input = input };
        var logs = new List<string>();
        interp.MessageLogged += logs.Add;
        interp.AddGraph(graph);
        interp.Start();
        return (interp, logs);
    }

    [Fact]
    public void Registry_ContainsBuiltins()
    {
        Assert.True(NodeRegistry.TryGet("flow.branch", out _));
        Assert.True(NodeRegistry.TryGet("event.tick", out _));
        Assert.True(NodeRegistry.TryGet("action.log", out _));
        Assert.Contains(NodeRegistry.All, d => d.Category == "Events");
        Assert.Contains(NodeRegistry.All, d => d.Category == "Math");
    }

    [Fact]
    public void Graph_SaveLoad_Roundtrip()
    {
        var graph = NewGraph("Round");
        var start = Node<EventStartNode>(graph, 10, 20);
        var log = Node<LogNode>(graph, 200, 40);
        log.Values["message"] = "saved";
        Link(graph, start, "exec", log, "in");
        graph.Variables.Add(new GraphVariable { Name = "speed", DataType = PinDataType.Float, DefaultValue = "3.5" });

        VisualGraph loaded = VisualGraph.FromJson(graph.ToJson());

        Assert.Equal("Round", loaded.Name);
        Assert.Equal(2, loaded.Nodes.Count);
        Assert.Single(loaded.Connections);
        Assert.Single(loaded.Variables);
        var loadedLog = Assert.IsType<LogNode>(loaded.Nodes[1]);
        Assert.Equal("saved", loadedLog.Values["message"]);
        Assert.Equal(10, loaded.Nodes[0].X);
        Assert.DoesNotContain(GraphValidator.Validate(loaded), i => i.IsError);
    }

    [Fact]
    public void Interpreter_RunsBranchFlow()
    {
        var (interp, logs) = Run(g =>
        {
            var start = Node<EventStartNode>(g);
            var branch = Node<BranchNode>(g);
            branch.Values["condition"] = "true";
            var yes = Node<LogNode>(g);
            yes.Values["message"] = "yes";
            var no = Node<LogNode>(g);
            no.Values["message"] = "no";
            Link(g, start, "exec", branch, "in");
            Link(g, branch, "true", yes, "in");
            Link(g, branch, "false", no, "in");
        });

        Assert.Equal(new[] { "yes" }, logs);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Interpreter_DataPull_TravelsThroughMath()
    {
        var scene = new Scene { Name = "VS" };
        var entity = scene.CreateEntity("Mover");
        var (interp, _) = Run(g =>
        {
            var tick = Node<EventTickNode>(g);
            var add = Node<MathAddNode>(g);
            add.Values["a"] = "1";
            add.Values["b"] = "2";
            var self = Node<GetSelfNode>(g);
            var translate = Node<TranslateNode>(g);
            translate.Values["dx"] = "0";
            Link(g, tick, "exec", translate, "in");
            Link(g, add, "result", translate, "dx");
            Link(g, self, "entity", translate, "target");
        }, scene);

        interp.Self = entity;
        interp.Tick(0.016f);

        Assert.Equal(new Vector3(3, 0, 0), entity.Transform.Position);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Interpreter_Sequence_RunsInOrder()
    {
        var (_, logs) = Run(g =>
        {
            var start = Node<EventStartNode>(g);
            var seq = Node<SequenceNode>(g);
            var a = Node<LogNode>(g);
            a.Values["message"] = "1";
            var b = Node<LogNode>(g);
            b.Values["message"] = "2";
            var c = Node<LogNode>(g);
            c.Values["message"] = "3";
            Link(g, start, "exec", seq, "in");
            Link(g, seq, "1", a, "in");
            Link(g, seq, "2", b, "in");
            Link(g, seq, "3", c, "in");
        });

        Assert.Equal(new[] { "1", "2", "3" }, logs);
    }

    [Fact]
    public void Interpreter_DoOnce_FiresSingleTime()
    {
        var graph = NewGraph();
        var start = Node<EventStartNode>(graph);
        var tick = Node<EventTickNode>(graph);
        var once = Node<DoOnceNode>(graph);
        var log = Node<LogNode>(graph);
        log.Values["message"] = "once";
        Link(graph, start, "exec", once, "in");
        graph.Connections.Add(new Connection { FromNode = tick.Id, FromPin = "exec", ToNode = once.Id, ToPin = "in" });
        Link(graph, once, "exec", log, "in");

        var interp = new GraphInterpreter();
        var logs = new List<string>();
        interp.MessageLogged += logs.Add;
        interp.AddGraph(graph);
        interp.Start();
        interp.Tick(0.016f);
        interp.Tick(0.016f);

        Assert.Equal(new[] { "once" }, logs);
    }

    [Fact]
    public void Interpreter_LocalAndBlackboardVariables()
    {
        var (interp, logs) = Run(g =>
        {
            var start = Node<EventStartNode>(g);
            var setLocal = Node<SetVariableNode>(g);
            setLocal.Values["name"] = "hp";
            setLocal.Values["value"] = "42";
            setLocal.Values["scope"] = "Local";
            var setBoard = Node<SetVariableNode>(g);
            setBoard.Values["name"] = "level";
            setBoard.Values["value"] = "3";
            setBoard.Values["scope"] = "Blackboard";
            Link(g, start, "exec", setLocal, "in");
            Link(g, setLocal, "exec", setBoard, "in");

            var tick = Node<EventTickNode>(g);
            var getLocal = Node<GetVariableNode>(g);
            getLocal.Values["name"] = "hp";
            var getBoard = Node<GetVariableNode>(g);
            getBoard.Values["name"] = "level";
            getBoard.Values["scope"] = "Blackboard";
            var add = Node<MathAddNode>(g);
            var log = Node<LogNode>(g);
            Link(g, tick, "exec", log, "in");
            Link(g, getLocal, "value", add, "a");
            Link(g, getBoard, "value", add, "b");
            Link(g, add, "result", log, "message");
        });

        interp.Tick(0.016f);

        Assert.Equal(new[] { "45" }, logs);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Interpreter_EventKey_FiresOnPress()
    {
        var input = new InputState();
        var (interp, logs) = Run(g =>
        {
            var key = Node<EventKeyNode>(g);
            key.Values["key"] = "Space";
            var log = Node<LogNode>(g);
            log.Values["message"] = "pressed";
            Link(g, key, "exec", log, "in");
        }, input: input);

        input.BeginFrame();
        input.KeyPressed(Key.Space);
        interp.Tick(0.016f);
        Assert.Equal(new[] { "pressed" }, logs);

        input.BeginFrame();
        interp.Tick(0.016f);
        Assert.Single(logs);
    }

    [Fact]
    public void Interpreter_StepLimit_BreaksExecLoop()
    {
        var graph = NewGraph();
        var tick = Node<EventTickNode>(graph);
        var seq = Node<SequenceNode>(graph);
        Link(graph, tick, "exec", seq, "in");
        graph.Connections.Add(new Connection { FromNode = seq.Id, FromPin = "1", ToNode = seq.Id, ToPin = "in" });

        var interp = new GraphInterpreter();
        interp.AddGraph(graph);
        interp.Start();
        interp.Tick(0.016f);

        Assert.Contains(interp.Errors, e => e.Contains("Step limit"));
    }

    [Fact]
    public void Interpreter_DataCycle_ReportsError()
    {
        var graph = NewGraph();
        var tick = Node<EventTickNode>(graph);
        var a = Node<MathAddNode>(graph);
        var b = Node<MathAddNode>(graph);
        var log = Node<LogNode>(graph);
        Link(graph, tick, "exec", log, "in");
        Link(graph, a, "result", b, "a");
        Link(graph, b, "result", a, "a");
        Link(graph, a, "result", log, "message");

        var issues = GraphValidator.Validate(graph);
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("cycle"));

        var interp = new GraphInterpreter();
        interp.AddGraph(graph);
        interp.Start();
        interp.Tick(0.016f);
        Assert.Contains(interp.Errors, e => e.Contains("cycle"));
    }

    [Fact]
    public void Validator_DetectsTypeMismatch()
    {
        var graph = NewGraph();
        var compare = Node<CompareNode>(graph);
        var find = Node<FindEntityNode>(graph);
        var log = Node<LogNode>(graph);
        var setVar = Node<SetVariableNode>(graph);
        var time = Node<GetTimeNode>(graph);

        Assert.True(Link(graph, compare, "result", log, "message"), "bool -> Any must connect");
        Assert.True(Link(graph, time, "time", setVar, "value"), "float -> Any must connect");
        Assert.False(Link(graph, compare, "result", find, "name"), "bool -> string must fail");
        Assert.False(Link(graph, time, "time", find, "name"), "float -> string must fail");
        Assert.False(Link(graph, log, "exec", compare, "a"), "exec -> data must fail");
        Assert.False(Link(graph, compare, "result", log, "exec"), "data -> exec must fail");
        Assert.False(Link(graph, compare, "result", time, "time"), "data output -> data output must fail");
        Assert.Equal(2, graph.Connections.Count);
    }

    [Fact]
    public void Validator_IntWidensToFloat()
    {
        Assert.True(GraphValidator.Compatible(PinDataType.Int, PinDataType.Float));
        Assert.False(GraphValidator.Compatible(PinDataType.Float, PinDataType.Int));
        Assert.False(GraphValidator.Compatible(PinDataType.Bool, PinDataType.Float));
        Assert.True(GraphValidator.Compatible(PinDataType.Any, PinDataType.Vector3));
    }

    [Fact]
    public void Validator_ExecInputSingleIncoming()
    {
        var graph = NewGraph();
        var a = Node<EventStartNode>(graph);
        var b = Node<EventTickNode>(graph);
        var log = Node<LogNode>(graph);
        graph.Connections.Add(new Connection { FromNode = a.Id, FromPin = "exec", ToNode = log.Id, ToPin = "in" });
        graph.Connections.Add(new Connection { FromNode = b.Id, FromPin = "exec", ToNode = log.Id, ToPin = "in" });

        var issues = GraphValidator.Validate(graph);
        Assert.Contains(issues, i => i.Message.Contains("max 1"));
    }

    [Fact]
    public void Interpreter_FindEntity_ResolvesByName()
    {
        var scene = new Scene { Name = "VS" };
        var entity = scene.CreateEntity("Target");
        var (interp, _) = Run(g =>
        {
            var start = Node<EventStartNode>(g);
            var find = Node<FindEntityNode>(g);
            find.Values["name"] = "Target";
            var setPos = Node<SetPositionNode>(g);
            setPos.Values["y"] = "5";
            Link(g, start, "exec", setPos, "in");
            Link(g, find, "entity", setPos, "target");
        }, scene);

        Assert.Equal(new Vector3(0, 5, 0), entity.Transform.Position);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Registry_ContainsSnakeNodes()
    {
        Assert.True(NodeRegistry.TryGet("entity.getPosition", out _));
        Assert.True(NodeRegistry.TryGet("math.distance", out _));
        Assert.True(NodeRegistry.TryGet("value.random", out _));
    }

    [Fact]
    public void Interpreter_GetPosition_Distance_BetweenEntities()
    {
        var scene = new Scene { Name = "VS" };
        var head = scene.CreateEntity("Head");
        var food = scene.CreateEntity("Food");
        head.Transform.Position = new Vector3(1, 2, 0);
        food.Transform.Position = new Vector3(4, 6, 0);

        var (interp, logs) = Run(g =>
        {
            var start = Node<EventStartNode>(g);
            var findA = Node<FindEntityNode>(g);
            findA.Values["name"] = "Head";
            var findB = Node<FindEntityNode>(g);
            findB.Values["name"] = "Food";
            var posA = Node<GetPositionNode>(g);
            var posB = Node<GetPositionNode>(g);
            var dist = Node<MathDistanceNode>(g);
            var log = Node<LogNode>(g);
            Link(g, start, "exec", log, "in");
            Link(g, findA, "entity", posA, "entity");
            Link(g, findB, "entity", posB, "entity");
            Link(g, posA, "position", dist, "a");
            Link(g, posB, "position", dist, "b");
            Link(g, dist, "result", log, "message");
        }, scene);

        Assert.Equal(new[] { "5" }, logs);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Interpreter_RandomRange_StaysWithinBounds()
    {
        var (interp, logs) = Run(g =>
        {
            var start = Node<EventStartNode>(g);
            var rnd = Node<RandomRangeNode>(g);
            rnd.Values["min"] = "-2";
            rnd.Values["max"] = "2";
            var log = Node<LogNode>(g);
            Link(g, start, "exec", log, "in");
            Link(g, rnd, "value", log, "message");
        });

        Assert.Single(logs);
        float value = float.Parse(logs[0], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(value, -2f, 2f);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Scene_SpriteRenderer_SurvivesRoundtrip()
    {
        var scene = new Scene { Name = "Sprites" };
        var e = scene.CreateEntity("Head");
        e.Transform.Position = new Vector3(3, 4, 0);
        e.SpriteRenderer = new SpriteRendererComponent
        {
            Width = 0.55f,
            Height = 0.5f,
            Color = new Vector3(0.2f, 0.8f, 0.35f),
            IsVisible = true
        };

        string json = scene.Serialize();
        var data = System.Text.Json.JsonSerializer.Deserialize<SceneData>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Scene loaded = SceneSerializer.Deserialize(data);

        Entity back = Assert.IsType<Entity>(loaded.FindByName("Head"));
        Assert.NotNull(back.SpriteRenderer);
        Assert.Equal(0.55f, back.SpriteRenderer!.Width);
        Assert.Equal(new Vector3(0.2f, 0.8f, 0.35f), back.SpriteRenderer.Color);
        Assert.Equal(new Vector3(3, 4, 0), back.Transform.Position);
    }
}
