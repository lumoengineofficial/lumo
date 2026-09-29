using System.Globalization;
using System.Numerics;
using Lumo.Engine.Core;
using Lumo.Engine.Input;
using Lumo.Engine.Scene;
using Lumo.Engine.VisualScripting;

namespace Lumo.Tests;

/// <summary>
/// Generates the "FPS Sample" starter project: a 3D arena scene plus a visual
/// graph implementing mouse-look movement, shooting with raycasts, chasing
/// enemies, a kill/HP HUD, win/lose states and a gun viewmodel. The scene and
/// graph are written straight into Documents\LumoProjects\FPS Sample so the
/// project can be opened in the editor immediately.
/// </summary>
[Collection("Blackboard")]
public class FpsSampleGeneratorTests
{
    private static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LumoProjects", "FPS Sample");

    private const float SpawnZ = 9f;
    private const float EyeY = 1.6f;

    // ---------------------------------------------------------------- scene

    private static Scene BuildFpsScene()
    {
        var scene = new Scene { Name = "FPS" };

        Entity Cube(string name, Vector3 pos, Vector3 scale, Vector3 color)
        {
            Entity e = scene.CreateEntity(name);
            e.Transform.Position = pos;
            e.Transform.Scale = scale;
            e.MeshRenderer = new MeshRendererComponent
            {
                MeshName = "Cube",
                IsVisible = true,
                Color = color
            };
            return e;
        }

        var floorGrey = new Vector3(0.20f, 0.23f, 0.30f);
        var wallGrey = new Vector3(0.33f, 0.37f, 0.46f);
        var pillarGrey = new Vector3(0.42f, 0.46f, 0.56f);
        var blockLight = new Vector3(0.55f, 0.58f, 0.66f);
        var enemyRed = new Vector3(0.85f, 0.22f, 0.18f);
        var gunDark = new Vector3(0.13f, 0.13f, 0.16f);

        // 20x20 arena: floor + four 3m walls.
        Cube("Floor", new Vector3(0, -0.25f, 0), new Vector3(20, 0.5f, 20), floorGrey);
        Cube("WallN", new Vector3(0, 1.5f, -10), new Vector3(20, 3, 0.5f), wallGrey);
        Cube("WallS", new Vector3(0, 1.5f, 10), new Vector3(20, 3, 0.5f), wallGrey);
        Cube("WallW", new Vector3(-10, 1.5f, 0), new Vector3(0.5f, 3, 20), wallGrey);
        Cube("WallE", new Vector3(10, 1.5f, 0), new Vector3(0.5f, 3, 20), wallGrey);

        // pillars (cover) and low blocks.
        Cube("Pillar1", new Vector3(-4, 1.5f, -3), new Vector3(1.5f, 3, 1.5f), pillarGrey);
        Cube("Pillar2", new Vector3(4, 1.5f, -3), new Vector3(1.5f, 3, 1.5f), pillarGrey);
        Cube("Pillar3", new Vector3(-4, 1.5f, 5), new Vector3(1.5f, 3, 1.5f), pillarGrey);
        Cube("Pillar4", new Vector3(4, 1.5f, 5), new Vector3(1.5f, 3, 1.5f), pillarGrey);
        Cube("Block1", new Vector3(0, 0.75f, -4), new Vector3(2, 1.5f, 2), blockLight);
        Cube("Block2", new Vector3(7, 0.75f, 3), new Vector3(2, 1.5f, 2), blockLight);
        Cube("Block3", new Vector3(-7, 0.75f, -6), new Vector3(2, 1.5f, 2), blockLight);

        // enemies (AI targets).
        Cube("Enemy1", new Vector3(-3, 0.9f, -7), new Vector3(1, 1.8f, 1), enemyRed);
        Cube("Enemy2", new Vector3(3, 0.9f, -7), new Vector3(1, 1.8f, 1), enemyRed);
        Cube("Enemy3", new Vector3(-7, 0.9f, -1), new Vector3(1, 1.8f, 1), enemyRed);
        Cube("Enemy4", new Vector3(0, 0.9f, -6), new Vector3(1, 1.8f, 1), enemyRed);

        // gun viewmodel (positioned by the graph each tick).
        Cube("Gun", new Vector3(0, EyeY - 0.12f, SpawnZ - 0.55f), new Vector3(0.12f, 0.12f, 0.5f), gunDark);

        // player + primary camera.
        Entity player = scene.CreateEntity("Player");
        player.Transform.Position = new Vector3(0, EyeY, SpawnZ);
        player.Camera = new CameraComponent { IsPrimary = true, FieldOfView = 75 };

        return scene;
    }

    // ---------------------------------------------------------------- graph

    private sealed class G
    {
        public VisualGraph Graph = new() { Name = "FPSGame" };

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

    private static VisualGraph BuildFpsGraph()
    {
        var g = new G();

        // ---- On Start: run state -------------------------------------------
        VSNode evStart = g.N("event.start", 40, 40);
        VSNode setHp = g.N("var.set", 300, 40, ("name", "hp"), ("scope", "Blackboard"), ("value", "100"));
        VSNode setScore = g.N("var.set", 300, 160, ("name", "score"), ("scope", "Blackboard"), ("value", "0"));
        VSNode setFire = g.N("var.set", 300, 280, ("name", "fireT"), ("scope", "Blackboard"), ("value", "0"));
        VSNode setHurt = g.N("var.set", 300, 400, ("name", "hurtT"), ("scope", "Blackboard"), ("value", "0"));
        VSNode setE1 = g.N("var.set", 300, 520, ("name", "e1alive"), ("scope", "Blackboard"), ("value", "true"));
        VSNode setE2 = g.N("var.set", 300, 640, ("name", "e2alive"), ("scope", "Blackboard"), ("value", "true"));
        VSNode setE3 = g.N("var.set", 300, 760, ("name", "e3alive"), ("scope", "Blackboard"), ("value", "true"));
        VSNode setE4 = g.N("var.set", 300, 880, ("name", "e4alive"), ("scope", "Blackboard"), ("value", "true"));
        VSNode logStart = g.N("action.log", 560, 40, ("message", "FPS Sample: eliminate 4 targets!"));
        g.C(evStart, "exec", setHp, "in");
        g.C(setHp, "exec", setScore, "in");
        g.C(setScore, "exec", setFire, "in");
        g.C(setFire, "exec", setHurt, "in");
        g.C(setHurt, "exec", setE1, "in");
        g.C(setE1, "exec", setE2, "in");
        g.C(setE2, "exec", setE3, "in");
        g.C(setE3, "exec", setE4, "in");
        g.C(setE4, "exec", logStart, "in");

        // ---- shared lookups -------------------------------------------------
        VSNode findPlayer = g.N("entity.find", 40, 1100, ("name", "Player"));
        VSNode findGun = g.N("entity.find", 40, 1240, ("name", "Gun"));
        VSNode findEnemy1 = g.N("entity.find", 40, 1380, ("name", "Enemy1"));
        VSNode findEnemy2 = g.N("entity.find", 40, 1520, ("name", "Enemy2"));
        VSNode findEnemy3 = g.N("entity.find", 40, 1660, ("name", "Enemy3"));
        VSNode findEnemy4 = g.N("entity.find", 40, 1800, ("name", "Enemy4"));
        VSNode getPosPlayer = g.N("entity.getPosition", 300, 1100);
        g.C(findPlayer, "entity", getPosPlayer, "entity");
        VSNode getTime = g.N("value.time", 300, 1240);

        // ---- tick 1: look + collision slide movement -------------------------
        VSNode evMain = g.N("event.tick", 40, 2100);
        VSNode mDelta = g.N("input.mouseDelta", 60, 2260);
        VSNode look = g.N("action.look", 300, 2100, ("sensitivity", "0.12"));
        g.C(evMain, "exec", look, "in");
        g.C(findPlayer, "entity", look, "target");
        g.C(mDelta, "dx", look, "dx");
        g.C(mDelta, "dy", look, "dy");

        VSNode dir = g.N("entity.direction", 560, 2100);
        g.C(findPlayer, "entity", dir, "entity");
        VSNode brkF = g.N("value.breakVector3", 800, 2100);
        VSNode brkR = g.N("value.breakVector3", 800, 2300);
        g.C(dir, "forward", brkF, "value");
        g.C(dir, "right", brkR, "value");
        VSNode mkF = g.N("value.makeVector3", 1040, 2100, ("y", "0"));
        VSNode mkR = g.N("value.makeVector3", 1040, 2300, ("y", "0"));
        g.C(brkF, "x", mkF, "x");
        g.C(brkF, "z", mkF, "z");
        g.C(brkR, "x", mkR, "x");
        g.C(brkR, "z", mkR, "z");
        VSNode normF = g.N("math.vnormalize", 1280, 2100);
        VSNode normR = g.N("math.vnormalize", 1280, 2300);
        g.C(mkF, "result", normF, "a");
        g.C(mkR, "result", normR, "a");
        VSNode brkNF = g.N("value.breakVector3", 1520, 2100);
        VSNode brkNR = g.N("value.breakVector3", 1520, 2300);
        g.C(normF, "result", brkNF, "value");
        g.C(normR, "result", brkNR, "value");

        VSNode axisZ = g.N("input.axis", 560, 2500, ("positive", "W"), ("negative", "S"));
        VSNode axisX = g.N("input.axis", 560, 2640, ("positive", "D"), ("negative", "A"));
        VSNode speedDt = g.N("math.multiply", 800, 2500, ("b", "4"));
        g.C(getTime, "delta", speedDt, "a");
        VSNode stepZ = g.N("math.multiply", 1040, 2500);
        VSNode stepX = g.N("math.multiply", 1040, 2640);
        g.C(axisZ, "axis", stepZ, "a");
        g.C(speedDt, "result", stepZ, "b");
        g.C(axisX, "axis", stepX, "a");
        g.C(speedDt, "result", stepX, "b");
        VSNode dzF = g.N("math.multiply", 1280, 2500);
        VSNode dzF2 = g.N("math.multiply", 1280, 2600);
        VSNode dxR = g.N("math.multiply", 1280, 2740);
        VSNode dxR2 = g.N("math.multiply", 1280, 2840);
        g.C(brkNF, "x", dzF, "a");
        g.C(stepZ, "result", dzF, "b");
        g.C(brkNF, "z", dzF2, "a");
        g.C(stepZ, "result", dzF2, "b");
        g.C(brkNR, "x", dxR, "a");
        g.C(stepX, "result", dxR, "b");
        g.C(brkNR, "z", dxR2, "a");
        g.C(stepX, "result", dxR2, "b");
        VSNode addX = g.N("math.add", 1520, 2640);
        VSNode addZ = g.N("math.add", 1520, 2740);
        g.C(dzF, "result", addX, "a");
        g.C(dxR, "result", addX, "b");
        g.C(dzF2, "result", addZ, "a");
        g.C(dxR2, "result", addZ, "b");
        VSNode mkDelta = g.N("value.makeVector3", 1760, 2640, ("y", "0"));
        g.C(addX, "result", mkDelta, "x");
        g.C(addZ, "result", mkDelta, "z");
        VSNode move = g.N("physics.moveSlide", 2000, 2640, ("radius", "0.35"));
        g.C(findPlayer, "entity", move, "entity");
        g.C(findGun, "entity", move, "ignore");
        g.C(mkDelta, "result", move, "delta");
        VSNode brkMove = g.N("value.breakVector3", 2240, 2640);
        VSNode setPosPlayer = g.N("action.setPosition", 2480, 2640);
        g.C(move, "position", brkMove, "value");
        g.C(brkMove, "x", setPosPlayer, "x");
        g.C(brkMove, "y", setPosPlayer, "y");
        g.C(brkMove, "z", setPosPlayer, "z");
        g.C(findPlayer, "entity", setPosPlayer, "target");
        g.C(look, "exec", setPosPlayer, "in");

        // ---- tick 2: weapon / hurt cooldown timers ---------------------------
        VSNode evTimer = g.N("event.tick", 40, 3050);
        VSNode getFire = g.N("var.get", 300, 3000, ("name", "fireT"), ("scope", "Blackboard"));
        VSNode cmpFireGt = g.N("logic.compare", 540, 3000, ("b", "0"), ("op", ">"));
        VSNode brFire = g.N("flow.branch", 780, 3050);
        VSNode decFire = g.N("math.subtract", 1020, 3000);
        VSNode setFireT = g.N("var.set", 1020, 3140, ("name", "fireT"), ("scope", "Blackboard"));
        g.C(evTimer, "exec", brFire, "in");
        g.C(getFire, "value", cmpFireGt, "a");
        g.C(cmpFireGt, "result", brFire, "condition");
        g.C(brFire, "true", setFireT, "in");
        g.C(getFire, "value", decFire, "a");
        g.C(getTime, "delta", decFire, "b");
        g.C(decFire, "result", setFireT, "value");

        VSNode getHurtT = g.N("var.get", 300, 3300, ("name", "hurtT"), ("scope", "Blackboard"));
        VSNode cmpHurtGt = g.N("logic.compare", 540, 3300, ("b", "0"), ("op", ">"));
        VSNode brHurtT = g.N("flow.branch", 780, 3350);
        VSNode decHurt = g.N("math.subtract", 1020, 3300);
        VSNode setHurtT = g.N("var.set", 1020, 3440, ("name", "hurtT"), ("scope", "Blackboard"));
        g.C(evTimer, "exec", brHurtT, "in");
        g.C(getHurtT, "value", cmpHurtGt, "a");
        g.C(cmpHurtGt, "result", brHurtT, "condition");
        g.C(brHurtT, "true", setHurtT, "in");
        g.C(getHurtT, "value", decHurt, "a");
        g.C(getTime, "delta", decHurt, "b");
        g.C(decHurt, "result", setHurtT, "value");

        // ---- ticks 3..6: one chain per enemy (chase when far, bite when close)
        void AiChain(VSNode findEnemy, int n, double y)
        {
            string aliveVar = $"e{n}alive";

            VSNode evAi = g.N("event.tick", 40, y);
            VSNode getAlive = g.N("var.get", 300, y - 60, ("name", aliveVar), ("scope", "Blackboard"));
            VSNode brAlive = g.N("flow.branch", 540, y);
            g.C(evAi, "exec", brAlive, "in");
            g.C(getAlive, "value", brAlive, "condition");

            VSNode getPosEnemy = g.N("entity.getPosition", 300, y + 120);
            g.C(findEnemy, "entity", getPosEnemy, "entity");
            VSNode dist = g.N("math.distance", 780, y + 120);
            g.C(getPosEnemy, "position", dist, "a");
            g.C(getPosPlayer, "position", dist, "b");
            VSNode cmpClose = g.N("logic.compare", 1020, y + 120, ("b", "1.6"), ("op", "<"));
            VSNode brClose = g.N("flow.branch", 1260, y + 120);
            g.C(brAlive, "true", brClose, "in");
            g.C(dist, "result", cmpClose, "a");
            g.C(cmpClose, "result", brClose, "condition");

            // far (false): chase the player with slide movement.
            VSNode toPlayer = g.N("math.vsubtract", 1500, y + 60);
            g.C(getPosPlayer, "position", toPlayer, "a");
            g.C(getPosEnemy, "position", toPlayer, "b");
            VSNode chaseDir = g.N("math.vnormalize", 1740, y + 60);
            g.C(toPlayer, "result", chaseDir, "a");
            VSNode brkChase = g.N("value.breakVector3", 1980, y + 60);
            g.C(chaseDir, "result", brkChase, "value");
            VSNode mkChase = g.N("value.makeVector3", 2220, y + 60, ("y", "0"));
            g.C(brkChase, "x", mkChase, "x");
            g.C(brkChase, "z", mkChase, "z");
            VSNode normChase = g.N("math.vnormalize", 2460, y + 60);
            g.C(mkChase, "result", normChase, "a");
            VSNode brkMoveE = g.N("value.breakVector3", 2700, y + 60);
            g.C(normChase, "result", brkMoveE, "value");
            VSNode chaseDt = g.N("math.multiply", 2940, y + 60, ("b", "3"));
            g.C(getTime, "delta", chaseDt, "a");
            VSNode ecx = g.N("math.multiply", 3180, y + 60);
            VSNode ecz = g.N("math.multiply", 3180, y + 160);
            g.C(brkMoveE, "x", ecx, "a");
            g.C(chaseDt, "result", ecx, "b");
            g.C(brkMoveE, "z", ecz, "a");
            g.C(chaseDt, "result", ecz, "b");
            VSNode mkEDelta = g.N("value.makeVector3", 3420, y + 60, ("y", "0"));
            g.C(ecx, "result", mkEDelta, "x");
            g.C(ecz, "result", mkEDelta, "z");
            VSNode moveE = g.N("physics.moveSlide", 3660, y + 60, ("radius", "0.3"));
            g.C(findEnemy, "entity", moveE, "entity");
            g.C(mkEDelta, "result", moveE, "delta");
            VSNode brkEP = g.N("value.breakVector3", 3900, y + 60);
            VSNode setPosE = g.N("action.setPosition", 4140, y + 60);
            g.C(moveE, "position", brkEP, "value");
            g.C(brkEP, "x", setPosE, "x");
            g.C(brkEP, "y", setPosE, "y");
            g.C(brkEP, "z", setPosE, "z");
            g.C(findEnemy, "entity", setPosE, "target");
            g.C(brClose, "false", setPosE, "in");

            // close (true): bite when the global hurt cooldown allows it.
            VSNode getHurtA = g.N("var.get", 1500, y + 340, ("name", "hurtT"), ("scope", "Blackboard"));
            VSNode cmpHurtOk = g.N("logic.compare", 1740, y + 340, ("b", "0"), ("op", "<="));
            VSNode brHurtOk = g.N("flow.branch", 1980, y + 340);
            g.C(brClose, "true", brHurtOk, "in");
            g.C(getHurtA, "value", cmpHurtOk, "a");
            g.C(cmpHurtOk, "result", brHurtOk, "condition");

            VSNode getHp = g.N("var.get", 2220, y + 340, ("name", "hp"), ("scope", "Blackboard"));
            VSNode subHp = g.N("math.subtract", 2460, y + 340, ("b", "10"));
            VSNode setHp2 = g.N("var.set", 2460, y + 480, ("name", "hp"), ("scope", "Blackboard"));
            VSNode setHurt2 = g.N("var.set", 2700, y + 480, ("name", "hurtT"), ("scope", "Blackboard"), ("value", "0.8"));
            VSNode logBite = g.N("action.log", 2940, y + 480, ("message", "Ouch!"));
            g.C(brHurtOk, "true", setHp2, "in");
            g.C(getHp, "value", subHp, "a");
            g.C(subHp, "result", setHp2, "value");
            g.C(setHp2, "exec", setHurt2, "in");
            g.C(setHurt2, "exec", logBite, "in");
        }

        AiChain(findEnemy1, 1, 3700);
        AiChain(findEnemy2, 2, 4050);
        AiChain(findEnemy3, 3, 4400);
        AiChain(findEnemy4, 4, 4750);

        // ---- tick 7: shoot (click + cooldown gate + wall/enemy ray race) ----
        VSNode evShoot = g.N("event.tick", 40, 5300);
        VSNode mouseShoot = g.N("input.mouseButton", 300, 5240, ("button", "Left"));
        VSNode getFireS = g.N("var.get", 300, 5400, ("name", "fireT"), ("scope", "Blackboard"));
        VSNode cmpFireOk = g.N("logic.compare", 540, 5400, ("b", "0"), ("op", "<="));
        VSNode andShoot = g.N("logic.and", 780, 5300);
        VSNode brShoot = g.N("flow.branch", 1020, 5300);
        g.C(evShoot, "exec", brShoot, "in");
        g.C(mouseShoot, "pressed", andShoot, "a");
        g.C(getFireS, "value", cmpFireOk, "a");
        g.C(cmpFireOk, "result", andShoot, "b");
        g.C(andShoot, "result", brShoot, "condition");

        VSNode setFire3 = g.N("var.set", 1260, 5240, ("name", "fireT"), ("scope", "Blackboard"), ("value", "0.5"));
        g.C(brShoot, "true", setFire3, "in");

        VSNode rayE = g.N("physics.raycast", 1260, 5480, ("maxDistance", "60"), ("namePrefix", "Enemy"));
        g.C(getPosPlayer, "position", rayE, "origin");
        g.C(dir, "forward", rayE, "direction");
        VSNode rayW = g.N("physics.raycast", 1260, 5700, ("maxDistance", "60"), ("excludePrefix", "Enemy"));
        g.C(getPosPlayer, "position", rayW, "origin");
        g.C(dir, "forward", rayW, "direction");
        g.C(findPlayer, "entity", rayW, "ignore");
        g.C(findGun, "entity", rayW, "ignore2");

        VSNode cmpWall = g.N("logic.compare", 1560, 5580, ("op", ">"));
        g.C(rayW, "distance", cmpWall, "a");
        g.C(rayE, "distance", cmpWall, "b");
        VSNode brWall = g.N("flow.branch", 1800, 5480);
        g.C(setFire3, "exec", brWall, "in");
        g.C(cmpWall, "result", brWall, "condition");

        // victim identification: nearest enemy from the ray, else next check.
        VSNode prevTrue = brWall;
        string prevPin = "true";
        for (int n = 1; n <= 4; n++)
        {
            VSNode findEnemy = n switch { 1 => findEnemy1, 2 => findEnemy2, 3 => findEnemy3, _ => findEnemy4 };
            VSNode eq = g.N("logic.equals", 2040, 5400 + (n - 1) * 260, ("b", $"Enemy{n}"));
            g.C(rayE, "name", eq, "a");
            VSNode brKill = g.N("flow.branch", 2280, 5400 + (n - 1) * 260);
            g.C(prevTrue, prevPin, brKill, "in");
            g.C(eq, "result", brKill, "condition");

            double ky = 5400 + (n - 1) * 260;
            VSNode setVisible = g.N("action.setVisible", 2520, ky, ("visible", "false"));
            VSNode setAlive = g.N("var.set", 2760, ky, ("name", $"e{n}alive"), ("scope", "Blackboard"), ("value", "false"));
            VSNode getScoreK = g.N("var.get", 2760, ky + 140, ("name", "score"), ("scope", "Blackboard"));
            VSNode addScore = g.N("math.add", 3000, ky + 140, ("b", "1"));
            VSNode setScoreK = g.N("var.set", 3000, ky, ("name", "score"), ("scope", "Blackboard"));
            VSNode logKill = g.N("action.log", 3240, ky, ("message", $"Target {n} down!"));
            g.C(brKill, "true", setVisible, "in");
            g.C(findEnemy, "entity", setVisible, "target");
            g.C(setVisible, "exec", setAlive, "in");
            g.C(setAlive, "exec", setScoreK, "in");
            g.C(getScoreK, "value", addScore, "a");
            g.C(addScore, "result", setScoreK, "value");
            g.C(setScoreK, "exec", logKill, "in");

            prevTrue = brKill;
            prevPin = "false";
        }

        // ---- tick 8: gun viewmodel follows eyes -----------------------------
        VSNode evGun = g.N("event.tick", 40, 6600);
        VSNode brkP2 = g.N("value.breakVector3", 300, 6600);
        g.C(getPosPlayer, "position", brkP2, "value");
        VSNode mulFx = g.N("math.multiply", 540, 6540, ("b", "0.55"));
        VSNode mulFz = g.N("math.multiply", 540, 6660, ("b", "0.55"));
        VSNode mulRx = g.N("math.multiply", 540, 6780, ("b", "0.18"));
        VSNode mulRz = g.N("math.multiply", 540, 6900, ("b", "0.18"));
        g.C(brkF, "x", mulFx, "a");
        g.C(brkF, "z", mulFz, "a");
        g.C(brkR, "x", mulRx, "a");
        g.C(brkR, "z", mulRz, "a");
        VSNode addGX = g.N("math.add", 780, 6540);
        VSNode addGX2 = g.N("math.add", 1020, 6540);
        VSNode addGZ = g.N("math.add", 780, 6660);
        VSNode addGZ2 = g.N("math.add", 1020, 6660);
        VSNode subGY = g.N("math.subtract", 780, 6780, ("b", "0.12"));
        g.C(brkP2, "x", addGX, "a");
        g.C(mulFx, "result", addGX, "b");
        g.C(addGX, "result", addGX2, "a");
        g.C(mulRx, "result", addGX2, "b");
        g.C(brkP2, "z", addGZ, "a");
        g.C(mulFz, "result", addGZ, "b");
        g.C(addGZ, "result", addGZ2, "a");
        g.C(mulRz, "result", addGZ2, "b");
        g.C(brkP2, "y", subGY, "a");
        VSNode mkGun = g.N("value.makeVector3", 1260, 6600);
        g.C(addGX2, "result", mkGun, "x");
        g.C(subGY, "result", mkGun, "y");
        g.C(addGZ2, "result", mkGun, "z");
        VSNode brkGun = g.N("value.breakVector3", 1380, 6600);
        g.C(mkGun, "result", brkGun, "value");
        VSNode setGunPos = g.N("action.setPosition", 1620, 6600);
        g.C(brkGun, "x", setGunPos, "x");
        g.C(brkGun, "y", setGunPos, "y");
        g.C(brkGun, "z", setGunPos, "z");
        g.C(findGun, "entity", setGunPos, "target");
        VSNode rot = g.N("entity.getRotation", 1620, 6840);
        g.C(findPlayer, "entity", rot, "entity");
        VSNode setGunRot = g.N("action.setRotation", 1860, 6720, ("roll", "0"));
        g.C(evGun, "exec", setGunPos, "in");
        g.C(setGunPos, "exec", setGunRot, "in");
        g.C(rot, "pitch", setGunRot, "pitch");
        g.C(rot, "yaw", setGunRot, "yaw");

        // ---- tick 9+: HUD ---------------------------------------------------
        // kills
        VSNode evHudK = g.N("event.tick", 40, 7200);
        VSNode getScoreH = g.N("var.get", 300, 7140, ("name", "score"), ("scope", "Blackboard"));
        VSNode concatK = g.N("value.concat", 540, 7140, ("a", "Kills: "));
        VSNode hudK = g.N("hud.text", 780, 7200,
            ("id", "kills"), ("x", "0.03"), ("y", "0.03"), ("size", "26"),
            ("r", "1"), ("g", "0.9"), ("b", "0.3"));
        g.C(evHudK, "exec", hudK, "in");
        g.C(getScoreH, "value", concatK, "b");
        g.C(concatK, "result", hudK, "text");

        // hp
        VSNode evHudH = g.N("event.tick", 40, 7400);
        VSNode getHpH = g.N("var.get", 300, 7340, ("name", "hp"), ("scope", "Blackboard"));
        VSNode concatH = g.N("value.concat", 540, 7340, ("a", "HP: "));
        VSNode hudH = g.N("hud.text", 780, 7400,
            ("id", "hp"), ("x", "0.03"), ("y", "0.10"), ("size", "26"),
            ("r", "1"), ("g", "0.4"), ("b", "0.4"));
        g.C(evHudH, "exec", hudH, "in");
        g.C(getHpH, "value", concatH, "b");
        g.C(concatH, "result", hudH, "text");

        // crosshair
        VSNode evHudC = g.N("event.tick", 40, 7600);
        VSNode hudCross = g.N("hud.text", 300, 7600,
            ("id", "cross"), ("x", "0.485"), ("y", "0.46"), ("size", "30"),
            ("text", "+"), ("r", "1"), ("g", "1"), ("b", "1"));
        g.C(evHudC, "exec", hudCross, "in");

        // controls hint
        VSNode evHudT = g.N("event.tick", 40, 7760);
        VSNode hudTip = g.N("hud.text", 300, 7760,
            ("id", "tip"), ("x", "0.28"), ("y", "0.94"), ("size", "16"),
            ("text", "WASD move | Mouse look | Click shoot | Esc release"),
            ("r", "0.8"), ("g", "0.85"), ("b", "0.95"));
        g.C(evHudT, "exec", hudTip, "in");

        // death: hp <= 0 shows the banner, logs once via Do Once.
        VSNode evHudD = g.N("event.tick", 40, 7920);
        VSNode getHpD = g.N("var.get", 300, 7860, ("name", "hp"), ("scope", "Blackboard"));
        VSNode cmpAlive = g.N("logic.compare", 540, 7860, ("b", "0"), ("op", ">"));
        VSNode brDead = g.N("flow.branch", 780, 7920);
        g.C(evHudD, "exec", brDead, "in");
        g.C(getHpD, "value", cmpAlive, "a");
        g.C(cmpAlive, "result", brDead, "condition");
        VSNode hudAlive = g.N("hud.remove", 1020, 7860, ("id", "dead"));
        VSNode hudDead = g.N("hud.text", 1020, 8000,
            ("id", "dead"), ("x", "0.33"), ("y", "0.42"), ("size", "48"),
            ("text", "YOU DIED"), ("r", "1"), ("g", "0.2"), ("b", "0.2"));
        VSNode doOnceDead = g.N("flow.doOnce", 1260, 8000);
        VSNode logDead = g.N("action.log", 1500, 8000, ("message", "You died!"));
        g.C(brDead, "true", hudAlive, "in");
        g.C(brDead, "false", hudDead, "in");
        g.C(hudDead, "exec", doOnceDead, "in");
        g.C(doOnceDead, "exec", logDead, "in");

        // win: score >= 4 clears the arena, logs once via Do Once.
        VSNode evHudW = g.N("event.tick", 40, 8260);
        VSNode getScoreW = g.N("var.get", 300, 8200, ("name", "score"), ("scope", "Blackboard"));
        VSNode cmpWin = g.N("logic.compare", 540, 8200, ("b", "4"), ("op", ">="));
        VSNode brWin = g.N("flow.branch", 780, 8260);
        g.C(evHudW, "exec", brWin, "in");
        g.C(getScoreW, "value", cmpWin, "a");
        g.C(cmpWin, "result", brWin, "condition");
        VSNode hudWin = g.N("hud.text", 1020, 8320,
            ("id", "win"), ("x", "0.36"), ("y", "0.36"), ("size", "44"),
            ("text", "AREA CLEAR!"), ("r", "0.4"), ("g", "1"), ("b", "0.5"));
        VSNode hudNoWin = g.N("hud.remove", 1020, 8200, ("id", "win"));
        VSNode doOnceWin = g.N("flow.doOnce", 1260, 8320);
        VSNode logWin = g.N("action.log", 1500, 8320, ("message", "Area clear!"));
        g.C(brWin, "true", hudWin, "in");
        g.C(brWin, "false", hudNoWin, "in");
        g.C(hudWin, "exec", doOnceWin, "in");
        g.C(doOnceWin, "exec", logWin, "in");

        return g.Graph;
    }

    // ---------------------------------------------------------------- runner

    private static (GraphInterpreter Interp, List<string> Logs, Scene Scene) Run(InputState? input = null)
    {
        var scene = BuildFpsScene();
        VisualGraph graph = VisualGraph.FromJson(BuildFpsGraph().ToJson());
        var interp = new GraphInterpreter { Scene = scene, Input = input };
        var logs = new List<string>();
        interp.MessageLogged += logs.Add;
        interp.AddGraph(graph);
        interp.Start();
        return (interp, logs, scene);
    }

    private static void Frame(GraphInterpreter interp, InputState input)
    {
        input.BeginFrame();
        interp.Tick(1f / 60f);
    }

    // ---------------------------------------------------------------- tests

    [Fact]
    public void GenerateFpsSampleProject()
    {
        // Documents may be synced/read-only: create in place and overwrite.
        string root = Root;
        Directory.CreateDirectory(Path.Combine(root, "Scenes"));
        Directory.CreateDirectory(Path.Combine(root, "Graphs"));
        Directory.CreateDirectory(Path.Combine(root, "Assets"));

        ProjectManager.SaveProjectInfo(new ProjectInfo
        {
            Name = "FPS Sample",
            Path = root,
            Description = "First-person arena - visual graph shooting, enemy AI and HUD.",
            CreatedAt = DateTime.Now,
            LastModified = DateTime.Now,
            Version = EngineConstants.Version
        });

        BuildFpsScene().Save(Path.Combine(root, "Scenes", "scene.json"));

        VisualGraph graph = BuildFpsGraph();
        Assert.DoesNotContain(GraphValidator.Validate(graph), i => i.IsError);
        Assert.True(graph.Nodes.Count >= 200, $"expected a full game graph, got {graph.Nodes.Count}");
        graph.Save(Path.Combine(root, "Graphs", "FPSGame.graph.json"));

        Scene loaded = Scene.Load(Path.Combine(root, "Scenes", "scene.json"));
        Assert.NotNull(loaded.FindByName("Player")?.Camera);
        Assert.NotNull(loaded.FindByName("Enemy1")?.MeshRenderer);
        Assert.Equal(4, loaded.AllEntities.Count(e => e.Name.StartsWith("Enemy")));

        VisualGraph reloaded = VisualGraph.Load(Path.Combine(root, "Graphs", "FPSGame.graph.json"));
        Assert.Equal(graph.Nodes.Count, reloaded.Nodes.Count);
        Assert.Equal(graph.Connections.Count, reloaded.Connections.Count);
        Assert.DoesNotContain(GraphValidator.Validate(reloaded), i => i.IsError);
    }

    [Fact]
    public void Fps_Start_InitializesRunState()
    {
        var (interp, logs, scene) = Run();
        Assert.Equal("100", Blackboard.Shared.Get("hp"));
        Assert.Equal("0", Blackboard.Shared.Get("score"));
        Assert.Equal("true", Blackboard.Shared.Get("e1alive"));
        Assert.Equal("true", Blackboard.Shared.Get("e4alive"));
        Assert.Contains("FPS Sample: eliminate 4 targets!", logs);
        Assert.NotNull(scene.FindByName("Player")?.Camera);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Fps_Tick_WasdMovesPlayer()
    {
        var input = new InputState();
        var (interp, _, scene) = Run(input);
        Entity player = scene.FindByName("Player")!;

        // idle frame: no movement
        Frame(interp, input);
        Assert.Equal(new Vector3(0, EyeY, SpawnZ), player.Transform.Position);

        // hold W: forward is -Z at yaw 0
        input.KeyPressed(Key.W);
        Frame(interp, input);
        float z1 = player.Transform.Position.Z;
        Assert.True(z1 < SpawnZ, $"W should move -Z, got {z1}");
        Assert.Equal(0, player.Transform.Position.X, 2);

        // hold A too: strafe adds -X at yaw 0 (right is +X)
        input.KeyPressed(Key.A);
        Frame(interp, input);
        float x2 = player.Transform.Position.X;
        Assert.True(x2 < 0, $"A should strafe -X, got {x2}");
        Assert.True(player.Transform.Position.Z < z1, "keeps moving forward");
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Fps_Shoot_KillsEnemyInLineOfSight()
    {
        var input = new InputState();
        var (interp, logs, scene) = Run(input);
        Entity enemy4 = scene.FindByName("Enemy4")!;
        Entity enemy1 = scene.FindByName("Enemy1")!;

        // straight ahead sits Enemy4 (over Block1 at eye height).
        // edges must be pressed AFTER BeginFrame and BEFORE Tick.
        input.BeginFrame();
        input.MousePressed(MouseButton.Left);
        interp.Tick(1f / 60f);

        Assert.False(enemy4.MeshRenderer!.IsVisible, "Enemy4 should be dead");
        Assert.True(enemy1.MeshRenderer!.IsVisible, "Enemy1 is not in line of sight");
        Assert.Equal("false", Blackboard.Shared.Get("e4alive"));
        Assert.Equal("1", Blackboard.Shared.Get("score"));
        Assert.Equal("0.5", Blackboard.Shared.Get("fireT"));
        Assert.Contains("Target 4 down!", logs);
        Assert.Empty(interp.Errors);

        // cooldown: clicking again immediately must not fire.
        input.BeginFrame();
        input.MouseReleased(MouseButton.Left);
        interp.Tick(1f / 60f);
        input.BeginFrame();
        input.MousePressed(MouseButton.Left);
        interp.Tick(1f / 60f);
        Assert.True(Blackboard.Shared.Get("score") == "1", "fire cooldown must block a second shot");
        Assert.True(enemy1.MeshRenderer!.IsVisible);
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Fps_Enemy_ChasesAndBites()
    {
        var input = new InputState();
        var (interp, logs, scene) = Run(input);
        Entity chaser = scene.FindByName("Enemy2")!;
        Entity biter = scene.FindByName("Enemy1")!;

        // Enemy1 already in bite range, Enemy2 far away.
        biter.Transform.Position = new Vector3(0, 0.9f, 7.6f);
        float farZ = chaser.Transform.Position.Z;

        Frame(interp, input);
        Assert.Equal("90", Blackboard.Shared.Get("hp"));
        Assert.Equal("0.8", Blackboard.Shared.Get("hurtT"));
        Assert.Contains("Ouch!", logs);
        Assert.True(chaser.Transform.Position.Z > farZ, "enemy should chase toward the player (+Z)");

        // hurt cooldown: no second bite on the next frame.
        Frame(interp, input);
        Assert.Equal("90", Blackboard.Shared.Get("hp"));
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Fps_Hud_TracksKillsHpAndDeath()
    {
        var input = new InputState();
        var (interp, logs, _) = Run(input);

        Frame(interp, input);
        Assert.Equal("Kills: 0", interp.Hud.Get("kills")!.Text);
        Assert.Equal("HP: 100", interp.Hud.Get("hp")!.Text);
        Assert.Equal("+", interp.Hud.Get("cross")!.Text);
        Assert.NotNull(interp.Hud.Get("tip"));
        Assert.Null(interp.Hud.Get("dead"));

        // lethal damage: banner appears, logs exactly once.
        Blackboard.Shared.Set("hp", "0");
        Frame(interp, input);
        Assert.Equal("YOU DIED", interp.Hud.Get("dead")!.Text);
        Assert.Contains("You died!", logs);
        Frame(interp, input);
        Assert.Single(logs, m => m == "You died!");
        Assert.Empty(interp.Errors);
    }

    [Fact]
    public void Fps_Win_WhenAllTargetsDown()
    {
        var input = new InputState();
        var (interp, logs, _) = Run(input);

        Frame(interp, input);
        Assert.Null(interp.Hud.Get("win"));

        Blackboard.Shared.Set("score", "4");
        Frame(interp, input);
        Assert.Equal("AREA CLEAR!", interp.Hud.Get("win")!.Text);
        Assert.Contains("Area clear!", logs);
        Assert.Empty(interp.Errors);
    }
}
