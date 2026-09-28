using Lumo.Engine.VisualScripting;
using Lumo.Plugins;

namespace Lumo.TestPlugin;

/// <summary>Happy-path fixture plugin.</summary>
public sealed class TestOkPlugin : IPlugin
{
    public string Id => "lumo.test.ok";

    public string Name => "Test OK Plugin";

    public string Version => "0.1.0";

    public void Load(PluginContext context) => context.RegisterNodes();
}

/// <summary>Always throws — the loader must record it and keep going.</summary>
public sealed class TestThrowingPlugin : IPlugin
{
    public string Id => "lumo.test.throw";

    public string Name => "Test Throwing Plugin";

    public string Version => "0.1.0";

    public void Load(PluginContext context) =>
        throw new InvalidOperationException("boom");
}

/// <summary>Fixture node used to verify plugin nodes run inside the interpreter.</summary>
[GraphNode("test.echo", "Echo", "Testing", "Returns its input unchanged (test fixture).")]
public sealed class EchoNode : VSNode
{
    public EchoNode()
    {
        DataIn("a", PinDataType.String, "");
        DataOut("result", PinDataType.String);
    }

    public override object? EvaluateOutput(GraphContext ctx, string pin) =>
        pin == "result" ? ctx.Get<string>(this, "a") : null;
}
