using Lumo.Plugins;

namespace Lumo.SamplePlugin;

/// <summary>
/// Minimal reference plugin: ships a graph node and logs its lifecycle.
/// Deployed to the editor/runtime Plugins folder by the build.
/// </summary>
public sealed class SamplePlugin : IPlugin
{
    public string Id => "lumo.sample";

    public string Name => "Lumo Sample Plugin";

    public string Version => "1.0.0";

    public void Load(PluginContext context)
    {
        context.RegisterNodes();
        context.Log($"Sample nodes available from {context.PluginAssembly.GetName().Name}.");
    }
}
