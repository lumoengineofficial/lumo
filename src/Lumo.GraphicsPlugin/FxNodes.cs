using System.Numerics;
using Lumo.Engine.Rendering;
using Lumo.Engine.VisualScripting;

namespace Lumo.GraphicsPlugin;

[GraphNode("gfx.vignette", "Vignette Effect", "Graphics", "Sets the screen vignette intensity (0..1); 0 disables it.")]
public sealed class VignetteNode : VSNode
{
    public VignetteNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("intensity", PinDataType.Float, "0.65");
    }

    public override void Execute(GraphContext ctx)
    {
        FxRegistry.Register(new FxEffect(
            "vignette", FxKind.Vignette,
            Math.Clamp(ctx.Get<float>(this, "intensity"), 0f, 1f),
            Vector3.Zero));
        ctx.Emit("exec");
    }
}

[GraphNode("gfx.grade", "Color Grade", "Graphics", "Sets the full-screen color-grade tint and strength (0..1).")]
public sealed class ColorGradeNode : VSNode
{
    public ColorGradeNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("strength", PinDataType.Float, "0.5");
        DataIn("r", PinDataType.Float, "0.06");
        DataIn("g", PinDataType.Float, "0.08");
        DataIn("b", PinDataType.Float, "0.16");
    }

    public override void Execute(GraphContext ctx)
    {
        FxRegistry.Register(new FxEffect(
            "grade", FxKind.ColorGrade,
            Math.Clamp(ctx.Get<float>(this, "strength"), 0f, 1f),
            new Vector3(
                Math.Clamp(ctx.Get<float>(this, "r"), 0f, 1f),
                Math.Clamp(ctx.Get<float>(this, "g"), 0f, 1f),
                Math.Clamp(ctx.Get<float>(this, "b"), 0f, 1f))));
        ctx.Emit("exec");
    }
}

[GraphNode("gfx.shading", "Mesh Shading", "Graphics", "Turns lambert mesh shading on or off.")]
public sealed class MeshShadingNode : VSNode
{
    public MeshShadingNode()
    {
        ExecIn();
        ExecOut("exec");
        DataIn("enabled", PinDataType.Bool, "true");
    }

    public override void Execute(GraphContext ctx)
    {
        FxRegistry.MeshShading = ctx.Get<bool>(this, "enabled");
        ctx.Emit("exec");
    }
}
