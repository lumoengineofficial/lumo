using System.Numerics;
using Lumo.Engine.Assets;
using Lumo.Engine.Input;
using Lumo.Engine.Scene;

namespace Lumo.Engine.VisualScripting;

// ---------------------------------------------------------------- Input

[GraphNode("input.isDown", "Key Is Down", "Input", "True while the key is held down.")]
public sealed class KeyIsDownNode : VSNode
{
    public KeyIsDownNode()
    {
        DataIn("key", PinDataType.String, "W");
        DataOut("down", PinDataType.Bool);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        string raw = ctx.Get<string>(this, "key");
        return ctx.Input is not null
            && Enum.TryParse<Key>(raw, true, out Key key)
            && ctx.Input.IsKeyDown(key);
    }
}

[GraphNode("input.axis", "Input Axis", "Input", "+1 while the positive key is held, -1 for the negative key, otherwise 0.")]
public sealed class InputAxisNode : VSNode
{
    public InputAxisNode()
    {
        DataIn("positive", PinDataType.String, "W");
        DataIn("negative", PinDataType.String, "S");
        DataOut("axis", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        if (ctx.Input is null)
            return 0f;
        float axis = 0f;
        if (Enum.TryParse<Key>(ctx.Get<string>(this, "positive"), true, out Key pos) && ctx.Input.IsKeyDown(pos))
            axis += 1f;
        if (Enum.TryParse<Key>(ctx.Get<string>(this, "negative"), true, out Key neg) && ctx.Input.IsKeyDown(neg))
            axis -= 1f;
        return axis;
    }
}

[GraphNode("input.mouseButton", "Mouse Button", "Input", "'down' while held, 'pressed' on the click frame.")]
public sealed class MouseButtonNode : VSNode
{
    public MouseButtonNode()
    {
        DataIn("button", PinDataType.String, "Left");
        DataOut("down", PinDataType.Bool);
        DataOut("pressed", PinDataType.Bool);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        if (!Enum.TryParse<MouseButton>(ctx.Get<string>(this, "button"), true, out MouseButton btn))
            btn = MouseButton.Left;
        if (ctx.Input is null)
            return false;
        return pin == "pressed" ? ctx.Input.IsMouseJustPressed(btn) : ctx.Input.IsMouseDown(btn);
    }
}

[GraphNode("input.mouseDelta", "Mouse Delta", "Input", "Cursor movement this frame in pixels; right and down are positive.")]
public sealed class MouseDeltaNode : VSNode
{
    public MouseDeltaNode()
    {
        DataOut("dx", PinDataType.Float);
        DataOut("dy", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        Vector2 d = ctx.Input?.MouseDelta ?? Vector2.Zero;
        return pin == "dy" ? d.Y : d.X;
    }
}

// ---------------------------------------------------------------- Math

[GraphNode("math.vsubtract", "Vector Subtract", "Math", "a - b for two vectors.")]
public sealed class VectorSubtractNode : VSNode
{
    public VectorSubtractNode()
    {
        DataIn("a", PinDataType.Vector3);
        DataIn("b", PinDataType.Vector3);
        DataOut("result", PinDataType.Vector3);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        ctx.Get<Vector3>(this, "a") - ctx.Get<Vector3>(this, "b");
}

[GraphNode("math.vnormalize", "Vector Normalize", "Math", "Unit-length direction; a zero-length input returns zero.")]
public sealed class VectorNormalizeNode : VSNode
{
    public VectorNormalizeNode()
    {
        DataIn("a", PinDataType.Vector3);
        DataOut("result", PinDataType.Vector3);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        Vector3 v = ctx.Get<Vector3>(this, "a");
        return v.LengthSquared() < 1e-10f ? Vector3.Zero : Vector3.Normalize(v);
    }
}

// ---------------------------------------------------------------- Logic

[GraphNode("logic.equals", "Text Equals", "Logic", "True when both texts match (case-insensitive).")]
public sealed class TextEqualsNode : VSNode
{
    public TextEqualsNode()
    {
        DataIn("a", PinDataType.String, "");
        DataIn("b", PinDataType.String, "");
        DataOut("result", PinDataType.Bool);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        string.Equals(ctx.Get<string>(this, "a"), ctx.Get<string>(this, "b"), StringComparison.OrdinalIgnoreCase);
}

// ---------------------------------------------------------------- Entity

[GraphNode("entity.direction", "Entity Direction", "Entity", "Forward, right and up unit vectors of an entity.")]
public sealed class EntityDirectionNode : VSNode
{
    public EntityDirectionNode()
    {
        DataIn("entity", PinDataType.Entity);
        DataOut("forward", PinDataType.Vector3);
        DataOut("right", PinDataType.Vector3);
        DataOut("up", PinDataType.Vector3);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        if (ctx.Get<Entity>(this, "entity") is not Entity e)
            return Vector3.Zero;
        return pin switch
        {
            "right" => e.Transform.Right,
            "up" => e.Transform.Up,
            _ => e.Transform.Forward
        };
    }
}

[GraphNode("entity.getRotation", "Get Rotation", "Entity", "Euler angles (degrees) of an entity.")]
public sealed class EntityRotationNode : VSNode
{
    public EntityRotationNode()
    {
        DataIn("entity", PinDataType.Entity);
        DataOut("pitch", PinDataType.Float);
        DataOut("yaw", PinDataType.Float);
        DataOut("roll", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        if (ctx.Get<Entity>(this, "entity") is not Entity e)
            return 0f;
        Vector3 euler = e.Transform.GetEulerAngles();
        return pin switch
        {
            "yaw" => euler.Y,
            "roll" => euler.Z,
            _ => euler.X
        };
    }
}

// ---------------------------------------------------------------- Actions

[GraphNode("action.setVisible", "Set Visible", "Actions", "Shows or hides the target entity's renderer.")]
public sealed class SetVisibleNode : VSNode
{
    public SetVisibleNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("target", PinDataType.Entity);
        DataIn("visible", PinDataType.Bool, "true");
    }

    public override void Execute(GraphContext ctx)
    {
        if (ctx.Get<Entity>(this, "target") is Entity target)
        {
            bool visible = ctx.Get<bool>(this, "visible");
            if (target.MeshRenderer != null) target.MeshRenderer.IsVisible = visible;
            if (target.SpriteRenderer != null) target.SpriteRenderer.IsVisible = visible;
        }
        ctx.Emit("exec");
    }
}

[GraphNode("action.setRotation", "Set Rotation", "Actions", "Sets the target's euler rotation (degrees) absolutely.")]
public sealed class SetRotationNode : VSNode
{
    public SetRotationNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("target", PinDataType.Entity);
        DataIn("pitch", PinDataType.Float, "0");
        DataIn("yaw", PinDataType.Float, "0");
        DataIn("roll", PinDataType.Float, "0");
    }

    public override void Execute(GraphContext ctx)
    {
        if (ctx.Get<Entity>(this, "target") is Entity target)
        {
            target.Transform.SetRotationFromEuler(
                ctx.Get<float>(this, "pitch"),
                ctx.Get<float>(this, "yaw"),
                ctx.Get<float>(this, "roll"));
        }
        ctx.Emit("exec");
    }
}

[GraphNode("action.look", "Look (Mouse)", "Actions", "Turns the target with a mouse delta; pitch is clamped to +/-85 degrees.")]
public sealed class LookNode : VSNode
{
    public LookNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("target", PinDataType.Entity);
        DataIn("dx", PinDataType.Float, "0");
        DataIn("dy", PinDataType.Float, "0");
        DataIn("sensitivity", PinDataType.Float, "0.12");
    }

    public override void Execute(GraphContext ctx)
    {
        if (ctx.Get<Entity>(this, "target") is Entity target)
        {
            float dx = ctx.Get<float>(this, "dx");
            float dy = ctx.Get<float>(this, "dy");
            float sens = ctx.Get<float>(this, "sensitivity");
            Vector3 euler = target.Transform.GetEulerAngles();
            // Screen down (dy > 0) pitches the view down; screen right turns right.
            float pitch = Math.Clamp(euler.X + dy * sens, -85f, 85f);
            float yaw = euler.Y + dx * sens;
            target.Transform.SetRotationFromEuler(pitch, yaw, euler.Z);
        }
        ctx.Emit("exec");
    }
}

// ---------------------------------------------------------------- Physics

/// <summary>World-space AABB of a collidable mesh entity.</summary>
internal readonly record struct Obstacle(Entity Entity, Vector3 Min, Vector3 Max);

internal static class PhysicsHelper
{
    public static List<Obstacle> Collect(
        Scene.Scene? scene, Entity? self, Entity? ignore,
        string includePrefix = "", string excludePrefix = "", Entity? ignore2 = null)
    {
        var list = new List<Obstacle>();
        if (scene is null)
            return list;

        foreach (var e in scene.AllEntities)
        {
            if (!e.IsActive || e.Transform is null || e == self || e == ignore || e == ignore2)
                continue;
            if (e.MeshRenderer is not { IsVisible: true } mr)
                continue;
            if (includePrefix.Length > 0 && !e.Name.StartsWith(includePrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            if (excludePrefix.Length > 0 && e.Name.StartsWith(excludePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var mesh = MeshLibrary.Get(mr.MeshName);
            if (mesh is null || mesh.Vertices.Length < 3)
                continue;

            var (mn, mx) = MeshLibrary.GetBounds(mesh);
            Vector3 half = (mx - mn) * 0.5f;
            Vector3 mid = (mn + mx) * 0.5f;
            Vector3 s = e.Transform.Scale;
            Vector3 halfScaled = new(half.X * MathF.Abs(s.X), half.Y * MathF.Abs(s.Y), half.Z * MathF.Abs(s.Z));
            Vector3 midScaled = new(mid.X * s.X, mid.Y * s.Y, mid.Z * s.Z);
            Vector3 center = e.Transform.Position + midScaled;
            list.Add(new Obstacle(e, center - halfScaled, center + halfScaled));
        }
        return list;
    }

    public static bool Overlaps(Vector3 point, float radius, List<Obstacle> obstacles)
    {
        foreach (var o in obstacles)
        {
            if (point.X > o.Min.X - radius && point.X < o.Max.X + radius &&
                point.Y > o.Min.Y - radius && point.Y < o.Max.Y + radius &&
                point.Z > o.Min.Z - radius && point.Z < o.Max.Z + radius)
                return true;
        }
        return false;
    }

    /// <summary>Ray vs AABB slab test; returns entry distance in [0, maxT] or null.</summary>
    public static float? RaySlab(Vector3 o, Vector3 d, Vector3 min, Vector3 max, float maxT)
    {
        float tmin = 0f;
        float tmax = maxT;

        for (int axis = 0; axis < 3; axis++)
        {
            float oA = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
            float dA = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float minA = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float maxA = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;

            if (MathF.Abs(dA) < 1e-8f)
            {
                if (oA < minA || oA > maxA)
                    return null;
                continue;
            }

            float t1 = (minA - oA) / dA;
            float t2 = (maxA - oA) / dA;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            if (t1 > tmin) tmin = t1;
            if (t2 < tmax) tmax = t2;
            if (tmin > tmax)
                return null;
        }
        return tmin;
    }
}

[GraphNode("physics.moveSlide", "Move With Collision", "Physics",
    "Moves an entity by delta, sliding along mesh obstacles (axis-aligned bounds). Outputs the resulting position and whether anything blocked it.")]
public sealed class MoveSlideNode : VSNode
{
    public MoveSlideNode()
    {
        DataIn("entity", PinDataType.Entity);
        DataIn("delta", PinDataType.Vector3);
        DataIn("radius", PinDataType.Float, "0.35");
        DataIn("ignore", PinDataType.Entity);
        DataOut("position", PinDataType.Vector3);
        DataOut("blocked", PinDataType.Bool);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        Entity? target = ctx.Get<Entity>(this, "entity");
        if (target is null || target.Transform is null)
            return pin == "blocked" ? false : Vector3.Zero;

        Vector3 delta = ctx.Get<Vector3>(this, "delta");
        float radius = ctx.Get<float>(this, "radius");
        Entity? ignore = ctx.Get<Entity>(this, "ignore");
        List<Obstacle> obstacles = PhysicsHelper.Collect(ctx.Scene, target, ignore);

        Vector3 pos = target.Transform.Position;
        bool blocked = false;

        if (delta.X != 0f)
        {
            Vector3 cand = pos with { X = pos.X + delta.X };
            if (PhysicsHelper.Overlaps(cand, radius, obstacles)) blocked = true;
            else pos = cand;
        }
        if (delta.Y != 0f)
        {
            Vector3 cand = pos with { Y = pos.Y + delta.Y };
            if (PhysicsHelper.Overlaps(cand, radius, obstacles)) blocked = true;
            else pos = cand;
        }
        if (delta.Z != 0f)
        {
            Vector3 cand = pos with { Z = pos.Z + delta.Z };
            if (PhysicsHelper.Overlaps(cand, radius, obstacles)) blocked = true;
            else pos = cand;
        }

        return pin == "blocked" ? blocked : pos;
    }
}

[GraphNode("physics.raycast", "Raycast", "Physics",
    "Casts a ray against mesh obstacles and returns the closest hit. Name prefix filters targets (e.g. 'Enemy'); exclude prefix skips them (e.g. enemies when testing walls).")]
public sealed class RaycastNode : VSNode
{
    public RaycastNode()
    {
        DataIn("origin", PinDataType.Vector3);
        DataIn("direction", PinDataType.Vector3);
        DataIn("maxDistance", PinDataType.Float, "50");
        DataIn("ignore", PinDataType.Entity);
        DataIn("ignore2", PinDataType.Entity);
        DataIn("namePrefix", PinDataType.String, "");
        DataIn("excludePrefix", PinDataType.String, "");
        DataOut("hit", PinDataType.Bool);
        DataOut("entity", PinDataType.Entity);
        DataOut("name", PinDataType.String);
        DataOut("distance", PinDataType.Float);
        DataOut("position", PinDataType.Vector3);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        Vector3 origin = ctx.Get<Vector3>(this, "origin");
        Vector3 dir = ctx.Get<Vector3>(this, "direction");
        float maxDist = ctx.Get<float>(this, "maxDistance");
        if (maxDist <= 0f)
            maxDist = 50f;

        Entity? ignore = ctx.Get<Entity>(this, "ignore");
        Entity? ignore2 = ctx.Get<Entity>(this, "ignore2");
        string include = ctx.Get<string>(this, "namePrefix");
        string exclude = ctx.Get<string>(this, "excludePrefix");

        Vector3 unit = dir.LengthSquared() < 1e-10f ? Vector3.Zero : Vector3.Normalize(dir);
        float best = maxDist;
        Entity? bestEntity = null;

        if (unit != Vector3.Zero)
        {
            foreach (var o in PhysicsHelper.Collect(ctx.Scene, null, ignore, include, exclude, ignore2))
            {
                float? t = PhysicsHelper.RaySlab(origin, unit, o.Min, o.Max, best);
                if (t.HasValue && t.Value < best)
                {
                    best = t.Value;
                    bestEntity = o.Entity;
                }
            }
        }

        Vector3 hitPos = origin + unit * best;
        return pin switch
        {
            "hit" => bestEntity is not null,
            "entity" => bestEntity,
            "name" => bestEntity?.Name ?? "",
            "position" => hitPos,
            "distance" => best,
            _ => null
        };
    }
}
