using Lumo.Engine.VisualScripting;

namespace Lumo.SamplePlugin;

/// <summary>Demo node proving plugin assemblies can extend the palette.</summary>
[GraphNode("sample.double", "Double", "Sample", "Multiplies the input by two (sample plugin).")]
public sealed class DoubleNode : VSNode
{
    public DoubleNode()
    {
        DataIn("a", PinDataType.Float, "1");
        DataOut("result", PinDataType.Float);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        pin == "result" ? ctx.Get<float>(this, "a") * 2f : null;
}
