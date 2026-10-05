using Lumo.Engine.VisualScripting;

namespace Lumo.TerrainPlugin;

[GraphNode("terrain.height", "Terrain Height", "Terrain", "Samples terrain height at world x/z (0 outside the map).")]
public sealed class TerrainHeightNode : VSNode
{
    public TerrainHeightNode()
    {
        DataIn("x", PinDataType.Float, "0");
        DataIn("z", PinDataType.Float, "0");
        DataOut("height", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin)
        => pin == "height" ? TerrainSystem.SampleHeight(ctx.Get<float>(this, "x"), ctx.Get<float>(this, "z")) : null;
}

[GraphNode("terrain.brush", "Terrain Brush", "Terrain", "Sculpts the terrain at world x/z: op = raise | lower | smooth | flatten.")]
public sealed class TerrainBrushNode : VSNode
{
    public TerrainBrushNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("op", PinDataType.String, "raise");
        DataIn("x", PinDataType.Float, "0");
        DataIn("z", PinDataType.Float, "0");
        DataIn("radius", PinDataType.Float, "6");
        DataIn("strength", PinDataType.Float, "0.5");
    }

    public override void Execute(GraphContext ctx)
    {
        string op = ctx.Get<string>(this, "op").Trim().ToLowerInvariant() switch
        {
            "lower" => "lower",
            "smooth" => "smooth",
            "flatten" => "flatten",
            _ => "raise",
        };
        var brushOp = op switch
        {
            "lower" => BrushOp.Lower,
            "smooth" => BrushOp.Smooth,
            "flatten" => BrushOp.Flatten,
            _ => BrushOp.Raise,
        };
        TerrainSystem.ApplyBrushAt(
            brushOp,
            ctx.Get<float>(this, "x"),
            ctx.Get<float>(this, "z"),
            Math.Clamp(ctx.Get<float>(this, "radius"), 0.5f, 64f),
            Math.Clamp(ctx.Get<float>(this, "strength"), 0.01f, 8f));
        ctx.Emit("exec");
    }
}
