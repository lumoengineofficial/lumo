using System.Numerics;
using Lumo.Engine.Rendering;
using Lumo.Plugins;

namespace Lumo.GraphicsPlugin;

/// <summary>
/// Graphics enhancement plugin: turns on lambert mesh shading and registers
/// frame overlays (vignette + cool color grade) that the renderers draw each
/// frame. Ships graph nodes so games can retune the effects while running.
/// </summary>
public sealed class GraphicsPlugin : IPlugin
{
    public string Id => "lumo.graphics";

    public string Name => "Lumo Graphics Plugin";

    public string Version => "1.0.0";

    public void Load(PluginContext context)
    {
        context.RegisterNodes();
        FxRegistry.MeshShading = true;
        FxRegistry.Register(new FxEffect("vignette", FxKind.Vignette, 0.65f, Vector3.Zero));
        FxRegistry.Register(new FxEffect("grade", FxKind.ColorGrade, 0.5f, new Vector3(0.06f, 0.08f, 0.16f)));
        context.Log($"Graphics fx enabled: mesh shading + vignette + color grade.");
    }

    public void Unload()
    {
        FxRegistry.Clear();
        FxRegistry.MeshShading = false;
    }
}
