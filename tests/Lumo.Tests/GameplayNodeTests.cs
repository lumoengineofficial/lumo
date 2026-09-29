using System.Numerics;
using Lumo.Engine.Input;
using Lumo.Engine.Scene;
using Lumo.Engine.VisualScripting;

namespace Lumo.Tests;

/// <summary>Unit tests for the gameplay nodes (input, math, physics, actions).</summary>
[Collection("Blackboard")]
public class GameplayNodeTests
{
    private sealed class G
    {
        public VisualGraph Graph = new() { Name = "GameplayNodes" };

        public VSNode N(string type, double x, double y, params (string Key, string Val)[] vals)
        {
            Assert.True(NodeRegistry.TryGet(type, out NodeDefinition? def), $"unknown node {type}");
            VSNode node = def!.Create();
            node.X = x;
            node.Y = y;
            foreach ((string key, string val) in vals)
                node.Values[key] = val;
            Graph.AddNode(node);
            return node;
        }

        public void C(VSNode from, string fromPin, VSNode to, string toPin) =>
            Assert.True(Graph.AddConnection(from, fromPin, to, toPin), $"{from.TypeId}.{fromPin} -> {to.TypeId}.{toPin}");
    }

    private static (GraphInterpreter Interp, List<string> Logs, Scene Scene) Run(
        VisualGraph graph, InputState? input = null, Scene? scene = null)
    {
        scene ??= new Scene { Name = "GameplayNodeTest" };
        var interp = new GraphInterpreter { Scene = scene, Input = input };
        var logs = new List<string>();
        interp.MessageLogged += logs.Add;
        interp.AddGraph(graph);
        interp.Start();
        return (interp, logs, scene);
    }

    private static Entity Cube(Scene scene, string name, Vector3 pos, Vector3 scale)
    {
        Entity e = scene.CreateEntity(name);
        e.Transform.Position = pos;
        e.Transform.Scale = scale;
        e.MeshRenderer = new MeshRendererComponent { MeshName = "Cube", IsVisible = true };
        return e;
    }

    private static void AssertVec(Vector3 expected, Vector3 actual, float tol = 0.02f)
    {
        Assert.True((expected - actual).Length() <= tol, $"expected {expected}, got {actual}");
    }

    // ---------------------------------------------------------------- input

    [Fact]
    public void InputNodes_ReportKeysAxisMouseAndDelta()
    {
        var g = new G();
        var input = new InputState();

        // chain 1: W held?
        VSNode tick1 = g.N("event.tick", 20, 40);
        VSNode isDown = g.N("input.isDown", 220, 40, ("key", "W"));
        VSNode branchW = g.N("flow.branch", 440, 40);
        VSNode logW = g.N("action.log", 660, 40, ("message", "W down"));
        g.C(tick1, "exec", branchW, "in");
        g.C(isDown, "down", branchW, "condition");
        g.C(branchW, "true", logW, "in");

        // chain 2: forward axis (W - S) == 1?
        VSNode tick2 = g.N("event.tick", 20, 220);
        VSNode axis = g.N("input.axis", 220, 220, ("positive", "W"), ("negative", "S"));
        VSNode cmpFwd = g.N("logic.compare", 440, 220, ("b", "1"), ("op", "=="));
        VSNode branchF = g.N("flow.branch", 660, 220);
        VSNode logF = g.N("action.log", 880, 220, ("message", "axis 1"));
        g.C(tick2, "exec", branchF, "in");
        g.C(axis, "axis", cmpFwd, "a");
        g.C(cmpFwd, "result", branchF, "condition");
        g.C(branchF, "true", logF, "in");

        // chain 3: axis == -1?
        VSNode tick3 = g.N("event.tick", 20, 400);
        VSNode cmpBack = g.N("logic.compare", 440, 400, ("b", "-1"), ("op", "=="));
        VSNode branchB = g.N("flow.branch", 660, 400);
        VSNode logB = g.N("action.log", 880, 400, ("message", "axis -1"));
        g.C(tick3, "exec", branchB, "in");
        g.C(axis, "axis", cmpBack, "a");
        g.C(cmpBack, "result", branchB, "condition");
        g.C(branchB, "true", logB, "in");

        // chain 4: mouse held / clicked?
        VSNode tick4 = g.N("event.tick", 20, 580);
        VSNode mouse = g.N("input.mouseButton", 220, 580, ("button", "Left"));
        VSNode branchDown = g.N("flow.branch", 440, 580);
        VSNode logDown = g.N("action.log", 660, 580, ("message", "mouse down"));
        VSNode branchClick = g.N("flow.branch", 440, 720);
        VSNode logClick = g.N("action.log", 660, 720, ("message", "mouse click"));
        g.C(tick4, "exec", branchDown, "in");
        g.C(tick4, "exec", branchClick, "in");
        g.C(mouse, "down", branchDown, "condition");
        g.C(branchDown, "true", logDown, "in");
        g.C(mouse, "pressed", branchClick, "condition");
        g.C(branchClick, "true", logClick, "in");

        // chain 5: mouse delta x == 5?
        VSNode tick5 = g.N("event.tick", 20, 900);
        VSNode mdelta = g.N("input.mouseDelta", 220, 900);
        VSNode cmpDx = g.N("logic.compare", 440, 900, ("b", "5"), ("op", "=="));
        VSNode branchD = g.N("flow.branch", 660, 900);
        VSNode logD = g.N("action.log", 880, 900, ("message", "delta 5"));
        g.C(tick5, "exec", branchD, "in");
        g.C(mdelta, "dx", cmpDx, "a");
        g.C(cmpDx, "result", branchD, "condition");
        g.C(branchD, "true", logD, "in");

        var (interp, logs, _) = Run(g.Graph, input);

        // frame 1: W held, no mouse press, delta (5, -3)
        input.BeginFrame();
        input.KeyPressed(Key.W);
        input.AddMouseDelta(5, -3);
        interp.Tick(1f / 60f);
        Assert.Contains("W down", logs);
        Assert.Contains("axis 1", logs);
        Assert.Contains("delta 5", logs);
        Assert.DoesNotContain("axis -1", logs);
        Assert.DoesNotContain("mouse down", logs);
        Assert.DoesNotContain("mouse click", logs);
        Assert.Empty(interp.Errors);

        // frame 2: release W, press S + left click
        logs.Clear();
        input.BeginFrame();
        input.KeyReleased(Key.W);
        input.KeyPressed(Key.S);
        input.MousePressed(MouseButton.Left);
        interp.Tick(1f / 60f);
        Assert.Contains("axis -1", logs);
        Assert.DoesNotContain("W down", logs);
        Assert.Contains("mouse down", logs);
        Assert.Contains("mouse click", logs);
        Assert.Empty(interp.Errors);

        // frame 3: button still held, click edge is gone
        logs.Clear();
        input.BeginFrame();
        interp.Tick(1f / 60f);
        Assert.Contains("mouse down", logs);
        Assert.DoesNotContain("mouse click", logs);
        Assert.Empty(interp.Errors);
    }

    // ---------------------------------------------------------------- math / logic

    [Fact]
    public void VectorNodes_SubtractAndNormalize()
    {
        var g = new G();
        var scene = new Scene { Name = "V" };
        Entity probe = scene.CreateEntity("Probe");

        VSNode tick = g.N("event.tick", 20, 40);
        VSNode sub = g.N("math.vsubtract", 220, 40, ("a", "5,3,2"), ("b", "1,1,1"));
        VSNode norm = g.N("math.vnormalize", 440, 40);
        VSNode brk = g.N("value.breakVector3", 660, 40);
        VSNode setPos = g.N("action.setPosition", 880, 40);
        VSNode findProbe = g.N("entity.find", 220, 200, ("name", "Probe"));
        g.C(tick, "exec", setPos, "in");
        g.C(sub, "result", norm, "a");
        g.C(norm, "result", brk, "value");
        g.C(brk, "x", setPos, "x");
        g.C(brk, "y", setPos, "y");
        g.C(brk, "z", setPos, "z");
        g.C(findProbe, "entity", setPos, "target");

        var (interp, _, s) = Run(g.Graph, scene: scene);
        interp.Tick(1f / 60f);

        Vector3 expected = Vector3.Normalize(new Vector3(4, 2, 1));
        AssertVec(expected, s.FindByName("Probe")!.Transform.Position);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void VectorNormalize_ZeroInput_ReturnsZero()
    {
        var g = new G();
        var scene = new Scene { Name = "V0" };
        Entity probe = scene.CreateEntity("Probe");

        VSNode tick = g.N("event.tick", 20, 40);
        VSNode norm = g.N("math.vnormalize", 220, 40, ("a", "0,0,0"));
        VSNode brk = g.N("value.breakVector3", 440, 40);
        VSNode setPos = g.N("action.setPosition", 660, 40);
        VSNode findProbe = g.N("entity.find", 220, 200, ("name", "Probe"));
        g.C(tick, "exec", setPos, "in");
        g.C(norm, "result", brk, "value");
        g.C(brk, "x", setPos, "x");
        g.C(brk, "y", setPos, "y");
        g.C(brk, "z", setPos, "z");
        g.C(findProbe, "entity", setPos, "target");

        var (interp, _, s) = Run(g.Graph, scene: scene);
        interp.Tick(1f / 60f);

        AssertVec(Vector3.Zero, s.FindByName("Probe")!.Transform.Position);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void TextEquals_IsCaseInsensitive()
    {
        var g = new G();
        VSNode tick = g.N("event.tick", 20, 40);
        VSNode eq = g.N("logic.equals", 220, 40, ("a", "Enemy2"), ("b", "enemy2"));
        VSNode branch = g.N("flow.branch", 440, 40);
        VSNode log = g.N("action.log", 660, 40, ("message", "equal"));
        g.C(tick, "exec", branch, "in");
        g.C(eq, "result", branch, "condition");
        g.C(branch, "true", log, "in");

        var (interp, logs, _) = Run(g.Graph);
        interp.Tick(1f / 60f);

        Assert.Contains("equal", logs);
        Assert.Empty(interp.Errors);
    }

    // ---------------------------------------------------------------- entity

    [Fact]
    public void EntityDirection_Yaw90_FacesNegativeX()
    {
        var g = new G();
        var scene = new Scene { Name = "Dir" };
        Entity player = scene.CreateEntity("Player");
        player.Transform.SetRotationFromEuler(0, 90, 0);
        Entity probe = scene.CreateEntity("Probe");

        VSNode tick = g.N("event.tick", 20, 40);
        VSNode findPlayer = g.N("entity.find", 220, 40, ("name", "Player"));
        VSNode dir = g.N("entity.direction", 440, 40);
        VSNode brk = g.N("value.breakVector3", 660, 40);
        VSNode setPos = g.N("action.setPosition", 880, 40);
        VSNode findProbe = g.N("entity.find", 220, 220, ("name", "Probe"));
        g.C(tick, "exec", setPos, "in");
        g.C(findPlayer, "entity", dir, "entity");
        g.C(dir, "forward", brk, "value");
        g.C(brk, "x", setPos, "x");
        g.C(brk, "y", setPos, "y");
        g.C(brk, "z", setPos, "z");
        g.C(findProbe, "entity", setPos, "target");

        var (interp, _, s) = Run(g.Graph, scene: scene);
        interp.Tick(1f / 60f);

        AssertVec(new Vector3(1, 0, 0), s.FindByName("Probe")!.Transform.Position, 0.01f);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void EntityRotation_ReadsBackEuler()
    {
        var g = new G();
        var scene = new Scene { Name = "Rot" };
        Entity probe = scene.CreateEntity("Probe");
        probe.Transform.SetRotationFromEuler(12, 34, 0);

        VSNode tick = g.N("event.tick", 20, 40);
        VSNode findProbe = g.N("entity.find", 220, 40, ("name", "Probe"));
        VSNode rot = g.N("entity.getRotation", 440, 40);
        VSNode cmpP = g.N("logic.compare", 660, 40, ("b", "11"), ("op", ">"));
        VSNode cmpP2 = g.N("logic.compare", 660, 180, ("b", "13"), ("op", "<"));
        VSNode and = g.N("logic.and", 880, 40);
        VSNode branch = g.N("flow.branch", 1100, 40);
        VSNode log = g.N("action.log", 1320, 40, ("message", "pitch ok"));
        g.C(tick, "exec", branch, "in");
        g.C(findProbe, "entity", rot, "entity");
        g.C(rot, "pitch", cmpP, "a");
        g.C(rot, "pitch", cmpP2, "a");
        g.C(cmpP, "result", and, "a");
        g.C(cmpP2, "result", and, "b");
        g.C(and, "result", branch, "condition");
        g.C(branch, "true", log, "in");

        var (interp, logs, _) = Run(g.Graph, scene: scene);
        interp.Tick(1f / 60f);

        Assert.Contains("pitch ok", logs);
        Assert.Empty(interp.Errors);
    }

    // ---------------------------------------------------------------- actions

    [Fact]
    public void SetVisible_HidesMesh()
    {
        var g = new G();
        var scene = new Scene { Name = "Vis" };
        Entity target = Cube(scene, "Box", Vector3.Zero, Vector3.One);

        VSNode tick = g.N("event.tick", 20, 40);
        VSNode find = g.N("entity.find", 220, 40, ("name", "Box"));
        VSNode setVisible = g.N("action.setVisible", 440, 40, ("visible", "false"));
        g.C(tick, "exec", setVisible, "in");
        g.C(find, "entity", setVisible, "target");

        var (interp, _, s) = Run(g.Graph, scene: scene);
        interp.Tick(1f / 60f);

        Assert.False(s.FindByName("Box")!.MeshRenderer!.IsVisible);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void SetRotation_SetsEuler()
    {
        var g = new G();
        var scene = new Scene { Name = "RotSet" };
        Entity target = scene.CreateEntity("Box");

        VSNode tick = g.N("event.tick", 20, 40);
        VSNode find = g.N("entity.find", 220, 40, ("name", "Box"));
        VSNode setRot = g.N("action.setRotation", 440, 40, ("pitch", "30"), ("yaw", "60"), ("roll", "0"));
        g.C(tick, "exec", setRot, "in");
        g.C(find, "entity", setRot, "target");

        var (interp, _, s) = Run(g.Graph, scene: scene);
        interp.Tick(1f / 60f);

        Vector3 euler = s.FindByName("Box")!.Transform.GetEulerAngles();
        Assert.Equal(30, euler.X, 0);
        Assert.Equal(60, euler.Y, 0);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Look_TurnsAndClampsPitch()
    {
        var g = new G();
        var scene = new Scene { Name = "Look" };
        Entity target = scene.CreateEntity("Player");

        VSNode tick = g.N("event.tick", 20, 40);
        VSNode find = g.N("entity.find", 220, 40, ("name", "Player"));
        VSNode look = g.N("action.look", 440, 40, ("dx", "10"), ("dy", "100"), ("sensitivity", "1"));
        g.C(tick, "exec", look, "in");
        g.C(find, "entity", look, "target");

        var (interp, _, s) = Run(g.Graph, scene: scene);
        interp.Tick(1f / 60f);

        Vector3 euler = s.FindByName("Player")!.Transform.GetEulerAngles();
        Assert.Equal(85, euler.X, 0);  // 0 + 100 clamped to +85 (screen down looks down)
        Assert.Equal(10, euler.Y, 0);  // 0 + 10 (screen right turns right)
        Assert.Empty(interp.Errors);
    }

    // ---------------------------------------------------------------- physics

    [Fact]
    public void MoveSlide_BlocksIntoWall()
    {
        var g = new G();
        var scene = new Scene { Name = "Slide" };
        Entity mover = scene.CreateEntity("Mover");
        mover.Transform.Position = Vector3.Zero;
        Cube(scene, "Wall", new Vector3(2, 0, 0), new Vector3(1, 2, 2));

        VSNode tick = g.N("event.tick", 20, 40);
        VSNode findMover = g.N("entity.find", 220, 40, ("name", "Mover"));
        VSNode move = g.N("physics.moveSlide", 440, 40, ("delta", "2,0,0"), ("radius", "0.35"));
        VSNode brk = g.N("value.breakVector3", 660, 40);
        VSNode setPos = g.N("action.setPosition", 880, 40);
        VSNode branch = g.N("flow.branch", 660, 200);
        VSNode log = g.N("action.log", 880, 200, ("message", "blocked"));
        // branch first: 'blocked' must be read before setPosition moves the mover.
        g.C(tick, "exec", branch, "in");
        g.C(branch, "false", setPos, "in");
        g.C(branch, "true", log, "in");
        g.C(findMover, "entity", move, "entity");
        g.C(move, "position", brk, "value");
        g.C(brk, "x", setPos, "x");
        g.C(brk, "y", setPos, "y");
        g.C(brk, "z", setPos, "z");
        g.C(findMover, "entity", setPos, "target");
        g.C(move, "blocked", branch, "condition");

        var (interp, logs, s) = Run(g.Graph, scene: scene);
        interp.Tick(1f / 60f);

        Assert.Contains("blocked", logs);
        AssertVec(Vector3.Zero, s.FindByName("Mover")!.Transform.Position);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void MoveSlide_SlidesFreeWhenClear()
    {
        var g = new G();
        var scene = new Scene { Name = "Slide2" };
        Entity mover = scene.CreateEntity("Mover");
        mover.Transform.Position = Vector3.Zero;
        Cube(scene, "Wall", new Vector3(2, 0, 0), new Vector3(1, 2, 2));

        VSNode tick = g.N("event.tick", 20, 40);
        VSNode findMover = g.N("entity.find", 220, 40, ("name", "Mover"));
        VSNode move = g.N("physics.moveSlide", 440, 40, ("delta", "0.8,0,0"), ("radius", "0.35"));
        VSNode brk = g.N("value.breakVector3", 660, 40);
        VSNode setPos = g.N("action.setPosition", 880, 40);
        VSNode branch = g.N("flow.branch", 660, 200);
        VSNode log = g.N("action.log", 880, 200, ("message", "blocked"));
        // branch first: 'blocked' must be read before setPosition moves the mover.
        g.C(tick, "exec", branch, "in");
        g.C(branch, "false", setPos, "in");
        g.C(branch, "true", log, "in");
        g.C(findMover, "entity", move, "entity");
        g.C(move, "position", brk, "value");
        g.C(brk, "x", setPos, "x");
        g.C(brk, "y", setPos, "y");
        g.C(brk, "z", setPos, "z");
        g.C(findMover, "entity", setPos, "target");
        g.C(move, "blocked", branch, "condition");

        var (interp, logs, s) = Run(g.Graph, scene: scene);
        interp.Tick(1f / 60f);

        Assert.DoesNotContain("blocked", logs);
        AssertVec(new Vector3(0.8f, 0, 0), s.FindByName("Mover")!.Transform.Position);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Raycast_RespectsPrefixesAndIgnores()
    {
        var g = new G();
        var scene = new Scene { Name = "Ray" };
        Cube(scene, "Wall1", new Vector3(0, 0, -3), new Vector3(2, 4, 0.5f));
        Cube(scene, "Enemy1", new Vector3(0, 0, -5), Vector3.One);
        Cube(scene, "Enemy2", new Vector3(0, 0, -8), Vector3.One);

        // Each chain logs "tag:EntityName" for its own ray configuration.
        void RayChain(string tag, double y, string direction,
            VSNode? findIgnore, VSNode? findIgnore2,
            string namePrefix = "", string excludePrefix = "")
        {
            VSNode tick = g.N("event.tick", 20, y);
            VSNode ray = g.N("physics.raycast", 220, y,
                ("direction", direction), ("maxDistance", "50"),
                ("namePrefix", namePrefix), ("excludePrefix", excludePrefix));
            VSNode concat = g.N("value.concat", 440, y, ("a", tag + ":"));
            VSNode log = g.N("action.log", 660, y);
            g.C(tick, "exec", log, "in");
            g.C(ray, "name", concat, "b");
            g.C(concat, "result", log, "message");
            if (findIgnore is not null)
                g.C(findIgnore, "entity", ray, "ignore");
            if (findIgnore2 is not null)
                g.C(findIgnore2, "entity", ray, "ignore2");
        }

        VSNode findWall = g.N("entity.find", 20, 1400, ("name", "Wall1"));
        RayChain("enemyRay", 40, "0,0,-1", null, null, namePrefix: "Enemy");
        RayChain("wallRay", 260, "0,0,-1", null, null, excludePrefix: "Enemy");
        RayChain("allRay", 480, "0,0,-1", null, null);
        RayChain("awayRay", 700, "0,0,1", null, null);
        RayChain("ignoreRay", 920, "0,0,-1", findWall, null, excludePrefix: "Enemy");

        var (interp, logs, _) = Run(g.Graph, scene: scene);
        interp.Tick(1f / 60f);

        Assert.Contains("enemyRay:Enemy1", logs);
        Assert.Contains("wallRay:Wall1", logs);
        Assert.Contains("allRay:Wall1", logs);   // closest overall is the wall
        Assert.Contains("awayRay:", logs);       // pointing away: no hit, empty name
        Assert.Contains("ignoreRay:", logs);     // wall ignored + enemies excluded: no hit
        Assert.Empty(interp.Errors);
    }
}
