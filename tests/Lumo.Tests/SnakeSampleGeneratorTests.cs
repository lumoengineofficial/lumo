using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Lumo.Engine.Core;
using Lumo.Engine.Input;
using Lumo.Engine.Scene;
using Lumo.Engine.VisualScripting;

namespace Lumo.Tests;

/// <summary>
/// Generates the "SnakeSample" starter project (scene + visual-scripting game
/// graph) under the system temp folder. The output is copied into
/// Documents\LumoProjects by the tooling. Every gameplay rule lives in the
/// graph: movement, steering with reverse-guard, food pickup, wall death.
/// </summary>
[Collection("Blackboard")]
public class SnakeSampleGeneratorTests
{
    private const float WallX = 3.8f;
    private const float WallY = 2.15f;
    private const float FoodMinX = -3.6f;
    private const float FoodMaxX = 3.6f;
    private const float FoodMinY = -1.95f;
    private const float FoodMaxY = 1.95f;
    private const string OutDir = "LumoSnakeGen";

    private static string Root => Path.Combine(Path.GetTempPath(), OutDir, "SnakeSample");

    // ---------------------------------------------------------------- scene

    private static Scene BuildSnakeScene()
    {
        var scene = new Scene { Name = "Snake" };

        Entity Spr(string name, float x, float y, float w, float h, Vector3 color)
        {
            Entity e = scene.CreateEntity(name);
            e.Transform.Position = new Vector3(x, y, 0);
            e.SpriteRenderer = new SpriteRendererComponent { Width = w, Height = h, Color = color, IsVisible = true };
            return e;
        }

        var headGreen = new Vector3(0.20f, 0.82f, 0.36f);
        var bodyGreen = new Vector3(0.15f, 0.63f, 0.30f);
        var foodRed = new Vector3(0.93f, 0.25f, 0.20f);
        var wallGrey = new Vector3(0.34f, 0.38f, 0.47f);

        Spr("SnakeHead", 0f, 0f, 0.55f, 0.55f, headGreen);
        Spr("S1", -0.55f, 0f, 0.5f, 0.5f, bodyGreen);
        Spr("S2", -1.10f, 0f, 0.5f, 0.5f, bodyGreen);
        Spr("S3", -1.65f, 0f, 0.5f, 0.5f, bodyGreen);
        Spr("Food", 2.5f, 1.2f, 0.45f, 0.45f, foodRed);

        Spr("WallTop", 0f, 2.38f, 8.32f, 0.16f, wallGrey);
        Spr("WallBottom", 0f, -2.38f, 8.32f, 0.16f, wallGrey);
        Spr("WallLeft", -4.08f, 0f, 0.16f, 4.6f, wallGrey);
        Spr("WallRight", 4.08f, 0f, 0.16f, 4.6f, wallGrey);

        Entity cam = scene.CreateEntity("Camera");
        cam.Transform.Position = new Vector3(0, 0, 10);
        cam.Camera = new CameraComponent { IsPrimary = true };

        return scene;
    }

    // ---------------------------------------------------------------- graph

    private sealed class G
    {
        public VisualGraph Graph = new() { Name = "SnakeGame" };

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

    private static VisualGraph BuildSnakeGraph()
    {
        var g = new G();

        // ---- On Start: reset run state ------------------------------------
        VSNode evStart = g.N("event.start", 40, 120);
        VSNode setDirX = g.N("var.set", 300, 40, ("name", "dirX"), ("scope", "Blackboard"), ("value", "1"));
        VSNode setDirY = g.N("var.set", 300, 170, ("name", "dirY"), ("scope", "Blackboard"), ("value", "0"));
        VSNode setAlive = g.N("var.set", 300, 300, ("name", "alive"), ("scope", "Blackboard"), ("value", "true"));
        VSNode setScore = g.N("var.set", 300, 430, ("name", "score"), ("scope", "Blackboard"), ("value", "0"));
        g.C(evStart, "exec", setDirX, "in");
        g.C(setDirX, "exec", setDirY, "in");
        g.C(setDirY, "exec", setAlive, "in");
        g.C(setAlive, "exec", setScore, "in");

        // ---- shared entity/data band (bottom-left) ------------------------
        VSNode findHead = g.N("entity.find", 40, 1750, ("name", "SnakeHead"));
        VSNode getPosHead = g.N("entity.getPosition", 280, 1750);
        VSNode brkHead = g.N("value.breakVector3", 520, 1750);
        g.C(findHead, "entity", getPosHead, "entity");
        g.C(getPosHead, "position", brkHead, "value");

        VSNode findS1 = g.N("entity.find", 40, 1930, ("name", "S1"));
        VSNode findS2 = g.N("entity.find", 40, 2110, ("name", "S2"));

        VSNode findS3 = g.N("entity.find", 40, 2290, ("name", "S3"));
        VSNode findFood = g.N("entity.find", 40, 2470, ("name", "Food"));
        VSNode getPosFood = g.N("entity.getPosition", 280, 2470);
        g.C(findFood, "entity", getPosFood, "entity");

        // ---- On Tick: gate on alive ---------------------------------------
        VSNode evTick = g.N("event.tick", 40, 760);
        VSNode getAlive = g.N("var.get", 300, 640, ("name", "alive"), ("scope", "Blackboard"));
        VSNode branchAlive = g.N("flow.branch", 540, 760);
        g.C(evTick, "exec", branchAlive, "in");
        g.C(getAlive, "value", branchAlive, "condition");

        // ---- wall gate first: predicted next position (no one-frame tunnel)
        VSNode branchWall = g.N("flow.branch", 780, 760);
        g.C(branchAlive, "true", branchWall, "in");

        // ---- head moves after the body is placed (wiring below) ----------
        VSNode translate = g.N("action.translate", 1740, 950);

        // ---- head step: dir * speed * delta --------------------------------
        VSNode getTime = g.N("value.time", 1020, 1150);
        VSNode mulStep = g.N("math.multiply", 1260, 1150, ("b", "1.6"));
        VSNode getDirX = g.N("var.get", 760, 1150, ("name", "dirX"), ("scope", "Blackboard"));
        VSNode mulX = g.N("math.multiply", 1500, 1150, ("b", "1"));
        VSNode getDirY = g.N("var.get", 760, 1290, ("name", "dirY"), ("scope", "Blackboard"));
        VSNode mulY = g.N("math.multiply", 1500, 1290, ("b", "1"));
        g.C(getTime, "delta", mulStep, "a");
        g.C(getDirX, "value", mulX, "a");
        g.C(mulStep, "result", mulX, "b");
        g.C(getDirY, "value", mulY, "a");
        g.C(mulStep, "result", mulY, "b");
        g.C(findHead, "entity", translate, "target");
        g.C(mulX, "result", translate, "dx");
        g.C(mulY, "result", translate, "dy");

        // ---- body: each segment sits 0.55*k behind the head along dir ----
        // (chain-follow collapses to speed*dt spacing in continuous movement,
        //  so the trail is recomputed from the direction each tick.)
        VSNode setPosS3 = g.N("action.setPosition", 1020, 950);
        VSNode setPosS2 = g.N("action.setPosition", 1260, 950);
        VSNode setPosS1 = g.N("action.setPosition", 1500, 950);

        g.C(branchWall, "false", setPosS3, "in");
        g.C(setPosS3, "exec", setPosS2, "in");
        g.C(setPosS2, "exec", setPosS1, "in");
        g.C(setPosS1, "exec", translate, "in");

        void Body(VSNode setPos, VSNode findSeg, string gap, double x, double y)
        {
            VSNode mx = g.N("math.multiply", x, y, ("b", gap));
            VSNode my = g.N("math.multiply", x, y + 140, ("b", gap));
            VSNode sx = g.N("math.subtract", x + 240, y);
            VSNode sy = g.N("math.subtract", x + 240, y + 140);
            g.C(getDirX, "value", mx, "a");
            g.C(getDirY, "value", my, "a");
            g.C(brkHead, "x", sx, "a");
            g.C(mx, "result", sx, "b");
            g.C(brkHead, "y", sy, "a");
            g.C(my, "result", sy, "b");
            g.C(findSeg, "entity", setPos, "target");
            g.C(sx, "result", setPos, "x");
            g.C(sy, "result", setPos, "y");
        }

        Body(setPosS3, findS3, "1.65", 800, 1930);
        Body(setPosS2, findS2, "1.1", 1280, 1930);
        Body(setPosS1, findS1, "0.55", 1760, 1930);

        // ---- eat check (after the move) ------------------------------------
        VSNode branchEat = g.N("flow.branch", 1980, 870);
        g.C(translate, "exec", branchEat, "in");
        VSNode dist = g.N("math.distance", 2220, 1050);
        VSNode cmpEat = g.N("logic.compare", 2460, 1050, ("b", "0.45"), ("op", "<"));
        g.C(getPosHead, "position", dist, "a");
        g.C(getPosFood, "position", dist, "b");
        g.C(dist, "result", cmpEat, "a");
        g.C(cmpEat, "result", branchEat, "condition");

        VSNode getScore = g.N("var.get", 1980, 1230, ("name", "score"), ("scope", "Blackboard"));
        VSNode addScore = g.N("math.add", 2220, 1230, ("b", "1"));
        VSNode setScoreAdd = g.N("var.set", 2220, 800, ("name", "score"), ("scope", "Blackboard"));
        VSNode randX = g.N("value.random", 2460, 1230, ("min", FoodMinX.ToString(CultureInfo.InvariantCulture)), ("max", FoodMaxX.ToString(CultureInfo.InvariantCulture)));
        VSNode randY = g.N("value.random", 2700, 1230, ("min", FoodMinY.ToString(CultureInfo.InvariantCulture)), ("max", FoodMaxY.ToString(CultureInfo.InvariantCulture)));
        VSNode setFoodPos = g.N("action.setPosition", 2460, 800);
        VSNode logScore = g.N("action.log", 2700, 800);

        g.C(branchEat, "true", setScoreAdd, "in");
        g.C(setScoreAdd, "exec", setFoodPos, "in");
        g.C(setFoodPos, "exec", logScore, "in");
        g.C(getScore, "value", addScore, "a");
        g.C(addScore, "result", setScoreAdd, "value");
        g.C(addScore, "result", logScore, "message");
        g.C(findFood, "entity", setFoodPos, "target");
        g.C(randX, "value", setFoodPos, "x");
        g.C(randY, "value", setFoodPos, "y");

        // ---- wall condition on PREDICTED position: head + step ------------
        VSNode addX = g.N("math.add", 1740, 1650);
        VSNode addY = g.N("math.add", 1740, 1830);

        VSNode cmpXp = g.N("logic.compare", 1980, 1650, ("b", WallX.ToString(CultureInfo.InvariantCulture)), ("op", ">"));
        VSNode cmpXn = g.N("logic.compare", 2220, 1650, ("b", (-WallX).ToString(CultureInfo.InvariantCulture)), ("op", "<"));
        VSNode orX = g.N("logic.or", 2460, 1650);
        VSNode cmpYp = g.N("logic.compare", 1980, 1830, ("b", WallY.ToString(CultureInfo.InvariantCulture)), ("op", ">"));
        VSNode cmpYn = g.N("logic.compare", 2220, 1830, ("b", (-WallY).ToString(CultureInfo.InvariantCulture)), ("op", "<"));
        VSNode orY = g.N("logic.or", 2460, 1830);
        VSNode orWall = g.N("logic.or", 2700, 1740);
        g.C(brkHead, "x", addX, "a");
        g.C(mulX, "result", addX, "b");
        g.C(brkHead, "y", addY, "a");
        g.C(mulY, "result", addY, "b");
        g.C(addX, "result", cmpXp, "a");
        g.C(addX, "result", cmpXn, "a");
        g.C(addY, "result", cmpYp, "a");
        g.C(addY, "result", cmpYn, "a");
        g.C(cmpXp, "result", orX, "a");
        g.C(cmpXn, "result", orX, "b");
        g.C(cmpYp, "result", orY, "a");
        g.C(cmpYn, "result", orY, "b");
        g.C(orX, "result", orWall, "a");
        g.C(orY, "result", orWall, "b");
        g.C(orWall, "result", branchWall, "condition");

        VSNode setAliveFalse = g.N("var.set", 2220, 1160, ("name", "alive"), ("scope", "Blackboard"), ("value", "false"));
        VSNode logOver = g.N("action.log", 2460, 1160, ("message", "Game Over!"));
        g.C(branchWall, "true", setAliveFalse, "in");
        g.C(setAliveFalse, "exec", logOver, "in");

        // ---- steering: 4 key blocks with reverse + dead guards -------------
        void KeyBlock(string key, string guardVar, string guardVal, string dx, string dy, double y)
        {
            VSNode evKey = g.N("event.key", 40, y, ("key", key));
            VSNode getGuard = g.N("var.get", 300, y - 80, ("name", guardVar), ("scope", "Blackboard"));
            VSNode getAliveK = g.N("var.get", 300, y + 60, ("name", "alive"), ("scope", "Blackboard"));
            VSNode cmp = g.N("logic.compare", 540, y - 80, ("b", guardVal), ("op", "!="));
            VSNode and = g.N("logic.and", 780, y - 10);
            VSNode branchK = g.N("flow.branch", 1020, y);
            VSNode setDX = g.N("var.set", 1260, y - 70, ("name", "dirX"), ("scope", "Blackboard"), ("value", dx));
            VSNode setDY = g.N("var.set", 1500, y - 70, ("name", "dirY"), ("scope", "Blackboard"), ("value", dy));
            g.C(evKey, "exec", branchK, "in");
            g.C(getGuard, "value", cmp, "a");
            g.C(cmp, "result", and, "a");
            g.C(getAliveK, "value", and, "b");
            g.C(and, "result", branchK, "condition");
            g.C(branchK, "true", setDX, "in");
            g.C(setDX, "exec", setDY, "in");
        }

        KeyBlock("Up", "dirY", "-1", "0", "1", 2900);
        KeyBlock("Down", "dirY", "1", "0", "-1", 3260);
        KeyBlock("Left", "dirX", "1", "-1", "0", 3620);
        KeyBlock("Right", "dirX", "-1", "1", "0", 3980);

        return g.Graph;
    }

    // ---------------------------------------------------------------- tests

    [Fact]
    public void GenerateSnakeSampleProject()
    {
        string root = Root;
        if (Directory.Exists(root))
            Directory.Delete(root, true);
        Directory.CreateDirectory(Path.Combine(root, "Scenes"));
        Directory.CreateDirectory(Path.Combine(root, "Graphs"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));

        ProjectManager.SaveProjectInfo(new ProjectInfo
        {
            Name = "Snake Sample",
            Path = root,
            Description = "Classic snake - every rule is a visual graph.",
            CreatedAt = DateTime.Now,
            LastModified = DateTime.Now,
            Version = EngineConstants.Version
        });

        BuildSnakeScene().Save(Path.Combine(root, "Scenes", "scene.json"));

        VisualGraph graph = BuildSnakeGraph();
        Assert.DoesNotContain(GraphValidator.Validate(graph), i => i.IsError);
        Assert.True(graph.Nodes.Count >= 80, $"expected a full game graph, got {graph.Nodes.Count}");
        graph.Save(Path.Combine(root, "Graphs", "SnakeGame.graph.json"));

        Scene loaded = Scene.Load(Path.Combine(root, "Scenes", "scene.json"));
        Assert.NotNull(loaded.FindByName("SnakeHead")?.SpriteRenderer);
        Assert.NotNull(loaded.FindByName("Food")?.SpriteRenderer);

        VisualGraph reloaded = VisualGraph.Load(Path.Combine(root, "Graphs", "SnakeGame.graph.json"));
        Assert.Equal(graph.Nodes.Count, reloaded.Nodes.Count);
        Assert.Equal(graph.Connections.Count, reloaded.Connections.Count);
        Assert.DoesNotContain(GraphValidator.Validate(reloaded), i => i.IsError);
    }

    private static (GraphInterpreter Interp, List<string> Logs, Scene Scene) RunSnake(InputState? input = null)
    {
        var scene = BuildSnakeScene();
        VisualGraph graph = VisualGraph.FromJson(BuildSnakeGraph().ToJson());
        var interp = new GraphInterpreter { Scene = scene, Input = input };
        var logs = new List<string>();
        interp.MessageLogged += logs.Add;
        interp.AddGraph(graph);
        interp.Start();
        return (interp, logs, scene);
    }

    [Fact]
    public void Snake_Start_SetsRunState()
    {
        var (interp, _, scene) = RunSnake();
        Assert.Equal("true", Blackboard.Shared.Get("alive"));
        Assert.Equal("1", Blackboard.Shared.Get("dirX"));
        Assert.Equal("0", Blackboard.Shared.Get("score"));
        Assert.Empty(interp.Errors);
        Assert.NotNull(scene.FindByName("SnakeHead"));
    }

    [Fact]
    public void Snake_Tick_MovesHeadAndBody()
    {
        var (interp, _, scene) = RunSnake();
        Entity head = scene.FindByName("SnakeHead")!;
        Vector3 startHead = head.Transform.Position;
        Vector3 startS1 = scene.FindByName("S1")!.Transform.Position;

        interp.Tick(1f / 60f);
        interp.Tick(1f / 60f);

        float headX = head.Transform.Position.X;
        float s1X = scene.FindByName("S1")!.Transform.Position.X;
        Assert.True(headX > startHead.X, "head should move +x");
        Assert.True(s1X > startS1.X, "S1 should follow head");
        Assert.True(headX > s1X, "head should lead the body");
        // body derives from the pre-move head (wall predict memoizes it first),
        // so spacing carries a one-tick lag of speed*dt (~0.026) — invisible.
        Assert.InRange(headX - s1X, 0.55f, 0.55f + 1.6f / 60f + 0.001f);
        float s3X = scene.FindByName("S3")!.Transform.Position.X;
        Assert.InRange(headX - s3X, 1.65f, 1.65f + 1.6f / 60f + 0.001f);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Snake_Eat_ScoresAndRespawnsFood()
    {
        var (interp, logs, scene) = RunSnake();
        Entity food = scene.FindByName("Food")!;
        food.Transform.Position = new Vector3(0.2f, 0f, 0f);

        interp.Tick(1f / 60f);

        Assert.Equal("1", Blackboard.Shared.Get("score"));
        Assert.Contains("1", logs);
        float fx = food.Transform.Position.X;
        Assert.InRange(fx, FoodMinX, FoodMaxX);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Snake_Wall_KillsRun()
    {
        var (interp, logs, scene) = RunSnake();
        scene.FindByName("SnakeHead")!.Transform.Position = new Vector3(WallX - 0.01f, 0f, 0f);

        interp.Tick(1f / 60f);

        Assert.Equal("false", Blackboard.Shared.Get("alive"));
        Assert.Contains("Game Over!", logs);

        Vector3 frozen = scene.FindByName("SnakeHead")!.Transform.Position;
        interp.Tick(1f / 60f);
        interp.Tick(1f / 60f);
        Assert.Equal(frozen, scene.FindByName("SnakeHead")!.Transform.Position);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Snake_KeySteering_UpdatesDirection()
    {
        var input = new InputState();
        var (interp, _, _) = RunSnake(input);

        input.BeginFrame();
        input.KeyPressed(Key.Up);
        interp.Tick(1f / 60f);
        Assert.Equal("1", Blackboard.Shared.Get("dirY"));
        Assert.Equal("0", Blackboard.Shared.Get("dirX"));

        // reverse guard: pressing Down while moving up is ignored
        input.BeginFrame();
        input.KeyPressed(Key.Down);
        interp.Tick(1f / 60f);
        Assert.Equal("1", Blackboard.Shared.Get("dirY"));
        Assert.Equal("0", Blackboard.Shared.Get("dirX"));

        input.BeginFrame();
        input.KeyPressed(Key.Left);
        interp.Tick(1f / 60f);
        Assert.Equal("-1", Blackboard.Shared.Get("dirX"));
        Assert.Equal("0", Blackboard.Shared.Get("dirY"));
        Assert.Empty(interp.Errors);
    }
}
