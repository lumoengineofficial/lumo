using System.Numerics;
using Lumo.Engine.Input;
using Lumo.Engine.Scene;
using Lumo.Engine.Scripting;

namespace Lumo.Engine.VisualScripting;

/// <summary>
/// Event entry node: dispatch is triggered by name ("start", "tick", "key").
/// </summary>
public abstract class EventNode : VSNode
{
    public abstract string EventName { get; }

    public override void Execute(GraphContext ctx) => ctx.Emit("exec");
}

// ---------------------------------------------------------------- Events

[GraphNode("event.start", "On Start", "Events", "Fires once when play begins.")]
public sealed class EventStartNode : EventNode
{
    public EventStartNode() => ExecOut("exec");
    public override string EventName => "start";
}

[GraphNode("event.tick", "On Tick", "Events", "Fires every frame.")]
public sealed class EventTickNode : EventNode
{
    public EventTickNode() => ExecOut("exec");
    public override string EventName => "tick";
}

[GraphNode("event.key", "On Key Press", "Events", "Fires once each time the key is pressed.")]
public sealed class EventKeyNode : EventNode
{
    public EventKeyNode()
    {
        ExecOut("exec");
        DataIn("key", PinDataType.String, "Space");
    }

    public override string EventName => "key";

    public override void Execute(GraphContext ctx)
    {
        string raw = ctx.Get<string>(this, "key");
        if (ctx.Input is not null && Enum.TryParse<Key>(raw, true, out Key key) && ctx.Input.IsKeyJustPressed(key))
            ctx.Emit("exec");
    }
}

// ---------------------------------------------------------------- Flow

[GraphNode("flow.branch", "Branch", "Flow", "Splits control flow on a boolean condition.")]
public sealed class BranchNode : VSNode
{
    public BranchNode()
    {
        ExecIn();
        ExecOut("true");
        ExecOut("false");
        DataIn("condition", PinDataType.Bool, "false");
    }

    public override void Execute(GraphContext ctx) =>
        ctx.Emit(ctx.Get<bool>(this, "condition") ? "true" : "false");
}

[GraphNode("flow.sequence", "Sequence", "Flow", "Runs up to three branches in order.")]
public sealed class SequenceNode : VSNode
{
    public SequenceNode()
    {
        ExecIn();
        ExecOut("1");
        ExecOut("2");
        ExecOut("3");
    }

    public override void Execute(GraphContext ctx)
    {
        ctx.Emit("1");
        ctx.Emit("2");
        ctx.Emit("3");
    }
}

[GraphNode("flow.doOnce", "Do Once", "Flow", "Passes the first trigger only; a 'reset' input re-arms it.")]
public sealed class DoOnceNode : VSNode
{
    private bool _done;

    public DoOnceNode()
    {
        ExecIn();
        ExecIn("reset");
        ExecOut("exec");
    }

    public override void Execute(GraphContext ctx)
    {
        if (ctx.CurrentExecPin == "reset")
        {
            _done = false;
            return;
        }
        if (_done)
            return;
        _done = true;
        ctx.Emit("exec");
    }

    public override void Reset() => _done = false;
}

// ---------------------------------------------------------------- Actions

[GraphNode("action.log", "Log", "Actions", "Writes a value to the editor console.")]
public sealed class LogNode : VSNode
{
    public LogNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("message", PinDataType.Any, "Hello from Lumo");
    }

    public override void Execute(GraphContext ctx)
    {
        ctx.Log(ctx.Get<string>(this, "message"));
        ctx.Emit("exec");
    }
}

[GraphNode("action.translate", "Translate", "Actions", "Moves the target entity by a delta.")]
public sealed class TranslateNode : VSNode
{
    public TranslateNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("target", PinDataType.Entity);
        DataIn("dx", PinDataType.Float, "0");
        DataIn("dy", PinDataType.Float, "0");
        DataIn("dz", PinDataType.Float, "0");
    }

    public override void Execute(GraphContext ctx)
    {
        if (ctx.Get<Entity>(this, "target") is Entity target)
        {
            var delta = new Vector3(
                ctx.Get<float>(this, "dx"),
                ctx.Get<float>(this, "dy"),
                ctx.Get<float>(this, "dz"));
            target.Transform.Position += delta;
        }
        ctx.Emit("exec");
    }
}

[GraphNode("action.rotate", "Rotate", "Actions", "Adds euler degrees (pitch/yaw/roll) to the target entity.")]
public sealed class RotateNode : VSNode
{
    public RotateNode()
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
            Vector3 euler = target.Transform.GetEulerAngles();
            target.Transform.SetRotationFromEuler(
                euler.X + ctx.Get<float>(this, "pitch"),
                euler.Y + ctx.Get<float>(this, "yaw"),
                euler.Z + ctx.Get<float>(this, "roll"));
        }
        ctx.Emit("exec");
    }
}

[GraphNode("action.setPosition", "Set Position", "Actions", "Teleports the target entity.")]
public sealed class SetPositionNode : VSNode
{
    public SetPositionNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("target", PinDataType.Entity);
        DataIn("x", PinDataType.Float, "0");
        DataIn("y", PinDataType.Float, "0");
        DataIn("z", PinDataType.Float, "0");
    }

    public override void Execute(GraphContext ctx)
    {
        if (ctx.Get<Entity>(this, "target") is Entity target)
        {
            target.Transform.Position = new Vector3(
                ctx.Get<float>(this, "x"),
                ctx.Get<float>(this, "y"),
                ctx.Get<float>(this, "z"));
        }
        ctx.Emit("exec");
    }
}

// ---------------------------------------------------------------- Math

[GraphNode("math.add", "Add", "Math", "a + b")]
public sealed class MathAddNode : VSNode
{
    public MathAddNode()
    {
        DataIn("a", PinDataType.Float, "0");
        DataIn("b", PinDataType.Float, "0");
        DataOut("result", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        ctx.Get<float>(this, "a") + ctx.Get<float>(this, "b");
}

[GraphNode("math.subtract", "Subtract", "Math", "a - b")]
public sealed class MathSubtractNode : VSNode
{
    public MathSubtractNode()
    {
        DataIn("a", PinDataType.Float, "0");
        DataIn("b", PinDataType.Float, "0");
        DataOut("result", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        ctx.Get<float>(this, "a") - ctx.Get<float>(this, "b");
}

[GraphNode("math.multiply", "Multiply", "Math", "a * b")]
public sealed class MathMultiplyNode : VSNode
{
    public MathMultiplyNode()
    {
        DataIn("a", PinDataType.Float, "0");
        DataIn("b", PinDataType.Float, "1");
        DataOut("result", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        ctx.Get<float>(this, "a") * ctx.Get<float>(this, "b");
}

[GraphNode("math.divide", "Divide", "Math", "a / b (zero guard returns 0)")]
public sealed class MathDivideNode : VSNode
{
    public MathDivideNode()
    {
        DataIn("a", PinDataType.Float, "0");
        DataIn("b", PinDataType.Float, "1");
        DataOut("result", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        float b = ctx.Get<float>(this, "b");
        return b == 0f ? 0f : ctx.Get<float>(this, "a") / b;
    }
}

[GraphNode("math.distance", "Distance", "Math", "Distance between two points.")]
public sealed class MathDistanceNode : VSNode
{
    public MathDistanceNode()
    {
        DataIn("a", PinDataType.Vector3);
        DataIn("b", PinDataType.Vector3);
        DataOut("result", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        Vector3.Distance(ctx.Get<Vector3>(this, "a"), ctx.Get<Vector3>(this, "b"));
}

// ---------------------------------------------------------------- Logic

[GraphNode("logic.compare", "Compare", "Logic", "Compares two floats (== != < <= > >=).")]
public sealed class CompareNode : VSNode
{
    public CompareNode()
    {
        DataIn("a", PinDataType.Float, "0");
        DataIn("b", PinDataType.Float, "0");
        DataIn("op", PinDataType.String, "<");
        DataOut("result", PinDataType.Bool);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        float a = ctx.Get<float>(this, "a");
        float b = ctx.Get<float>(this, "b");
        return ctx.Get<string>(this, "op").Trim() switch
        {
            "==" => a == b,
            "!=" => a != b,
            "<=" => a <= b,
            ">=" => a >= b,
            ">" => a > b,
            _ => a < b
        };
    }
}

[GraphNode("logic.and", "And", "Logic", "a AND b")]
public sealed class AndNode : VSNode
{
    public AndNode()
    {
        DataIn("a", PinDataType.Bool, "true");
        DataIn("b", PinDataType.Bool, "true");
        DataOut("result", PinDataType.Bool);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        ctx.Get<bool>(this, "a") && ctx.Get<bool>(this, "b");
}

[GraphNode("logic.or", "Or", "Logic", "a OR b")]
public sealed class OrNode : VSNode
{
    public OrNode()
    {
        DataIn("a", PinDataType.Bool, "true");
        DataIn("b", PinDataType.Bool, "true");
        DataOut("result", PinDataType.Bool);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        ctx.Get<bool>(this, "a") || ctx.Get<bool>(this, "b");
}

[GraphNode("logic.not", "Not", "Logic", "NOT a")]
public sealed class NotNode : VSNode
{
    public NotNode()
    {
        DataIn("a", PinDataType.Bool, "true");
        DataOut("result", PinDataType.Bool);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        !ctx.Get<bool>(this, "a");
}

// ---------------------------------------------------------------- Values

[GraphNode("value.makeVector3", "Make Vector3", "Values", "Builds a vector from x/y/z.")]
public sealed class MakeVector3Node : VSNode
{
    public MakeVector3Node()
    {
        DataIn("x", PinDataType.Float, "0");
        DataIn("y", PinDataType.Float, "0");
        DataIn("z", PinDataType.Float, "0");
        DataOut("result", PinDataType.Vector3);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        new Vector3(ctx.Get<float>(this, "x"), ctx.Get<float>(this, "y"), ctx.Get<float>(this, "z"));
}

[GraphNode("value.breakVector3", "Break Vector3", "Values", "Splits a vector into x/y/z.")]
public sealed class BreakVector3Node : VSNode
{
    public BreakVector3Node()
    {
        DataIn("value", PinDataType.Vector3);
        DataOut("x", PinDataType.Float);
        DataOut("y", PinDataType.Float);
        DataOut("z", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        Vector3 v = ctx.Get<Vector3>(this, "value");
        return pin switch
        {
            "x" => v.X,
            "y" => v.Y,
            "z" => v.Z,
            _ => null
        };
    }
}

[GraphNode("value.time", "Get Time", "Values", "Elapsed play time and frame delta.")]
public sealed class GetTimeNode : VSNode
{
    public GetTimeNode()
    {
        DataOut("time", PinDataType.Float);
        DataOut("delta", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        pin == "delta" ? ctx.DeltaTime : ctx.Time;
}

[GraphNode("value.self", "Get Self", "Values", "Entity the graph is attached to for this run.")]
public sealed class GetSelfNode : VSNode
{
    public GetSelfNode() => DataOut("entity", PinDataType.Entity);

    public override object? EvaluateOutput(GraphContext ctx, string pin) => ctx.Self;
}

[GraphNode("value.random", "Random Range", "Values", "Random float between min and max.")]
public sealed class RandomRangeNode : VSNode
{
    public RandomRangeNode()
    {
        DataIn("min", PinDataType.Float, "0");
        DataIn("max", PinDataType.Float, "1");
        DataOut("value", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        float min = ctx.Get<float>(this, "min");
        float max = ctx.Get<float>(this, "max");
        return min + (float)Random.Shared.NextDouble() * (max - min);
    }
}

// ---------------------------------------------------------------- Entity

[GraphNode("entity.find", "Find Entity", "Entity", "Looks up an entity by name in the scene.")]
public sealed class FindEntityNode : VSNode
{
    public FindEntityNode()
    {
        DataIn("name", PinDataType.String, "");
        DataOut("entity", PinDataType.Entity);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        string name = ctx.Get<string>(this, "name");
        return string.IsNullOrWhiteSpace(name) ? null : ctx.Scene?.FindByName(name);
    }
}

[GraphNode("entity.getPosition", "Get Position", "Entity", "World position of an entity.")]
public sealed class GetPositionNode : VSNode
{
    public GetPositionNode()
    {
        DataIn("entity", PinDataType.Entity);
        DataOut("position", PinDataType.Vector3);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        ctx.Get<Entity>(this, "entity") is Entity e ? e.Transform.Position : Vector3.Zero;
}

// ---------------------------------------------------------------- Variables

[GraphNode("var.get", "Get Variable", "Variables", "Reads a local or blackboard variable.")]
public sealed class GetVariableNode : VSNode
{
    public GetVariableNode()
    {
        DataIn("name", PinDataType.String, "");
        DataIn("scope", PinDataType.String, "Local");
        DataOut("value", PinDataType.Any);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
    {
        string name = ctx.Get<string>(this, "name");
        if (string.IsNullOrWhiteSpace(name))
            return "";
        if (ctx.IsBlackboardScope(this))
            return ctx.Blackboard.Get(name);
        return ctx.Locals.TryGetValue(name, out string? value) ? value : "";
    }
}

[GraphNode("var.set", "Set Variable", "Variables", "Writes a local or blackboard variable.")]
public sealed class SetVariableNode : VSNode
{
    public SetVariableNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("value", PinDataType.Any, "");
        DataIn("name", PinDataType.String, "");
        DataIn("scope", PinDataType.String, "Local");
    }

    public override void Execute(GraphContext ctx)
    {
        string name = ctx.Get<string>(this, "name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            string raw = PinConvert.ToInvariant(ctx.GetValue(this, "value"));
            if (ctx.IsBlackboardScope(this))
                ctx.Blackboard.Set(name, raw);
            else
                ctx.Locals[name] = raw;
        }
        ctx.Emit("exec");
    }
}

/// <summary>
/// Writes every blackboard variable to a JSON file (default save.json,
/// resolved against the interpreter base directory when relative).
/// </summary>
[GraphNode("system.save", "Save Game", "Actions", "Writes all blackboard variables to a JSON file.")]
public sealed class SaveGameStateNode : VSNode
{
    public SaveGameStateNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("path", PinDataType.String, "save.json");
    }

    public override void Execute(GraphContext ctx)
    {
        string path = ctx.Get<string>(this, "path");
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                string full = Resolve(ctx, path);
                GameStateStore.Save(full, ctx.Blackboard);
                ctx.Log($"Game saved: {full}");
            }
            catch (Exception ex)
            {
                ctx.Log($"Save failed: {ex.Message}");
            }
        }
        ctx.Emit("exec");
    }

    internal static string Resolve(GraphContext ctx, string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.Combine(ctx.Interpreter.BaseDirectory ?? Environment.CurrentDirectory, path);
}

/// <summary>
/// Loads blackboard variables from a JSON file produced by system.save.
/// </summary>
[GraphNode("system.load", "Load Game", "Actions", "Restores blackboard variables from a JSON file.")]
public sealed class LoadGameStateNode : VSNode
{
    public LoadGameStateNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("path", PinDataType.String, "save.json");
    }

    public override void Execute(GraphContext ctx)
    {
        string path = ctx.Get<string>(this, "path");
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                string full = SaveGameStateNode.Resolve(ctx, path);
                Dictionary<string, string> vars = GameStateStore.Load(full);
                foreach ((string key, string value) in vars)
                    ctx.Blackboard.Set(key, value);
                ctx.Log($"Game loaded: {vars.Count} variable(s) from {full}");
            }
            catch (Exception ex)
            {
                ctx.Log($"Load failed: {ex.Message}");
            }
        }
        ctx.Emit("exec");
    }
}
