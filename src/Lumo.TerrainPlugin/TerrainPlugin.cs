using Lumo.Plugins;

namespace Lumo.TerrainPlugin;

/// <summary>Editable terrain system: procedural generation, heightmap import,
/// chunked meshes and viewport sculpt brushes, exposed as editor buttons.</summary>
public sealed class TerrainPlugin : IPlugin
{
    public string Id => "lumo.terrain";

    public string Name => "Lumo Terrain Plugin";

    public string Version => "1.0.0";

    private Func<ViewportPointerArgs, bool>? _pointerHandler;

    public void Load(PluginContext context)
    {
        context.RegisterNodes();
        RegisterCommands(context);

        _pointerHandler = args => TerrainSystem.HandlePointer(args, PluginHost.Bridge);
        ViewportHooks.Pointer += _pointerHandler;

        if (!string.IsNullOrEmpty(context.ProjectDirectory))
            TerrainSystem.TryRestore(context.ProjectDirectory, context.Log);

        context.Log("Terrain ready: generate, import heightmap, sculpt with brushes.");
    }

    public void Unload()
    {
        if (_pointerHandler != null) ViewportHooks.Pointer -= _pointerHandler;
        _pointerHandler = null;
        PluginCommands.UnregisterPlugin(Id);
        TerrainSystem.SetBrush(null);
    }

    private void RegisterCommands(PluginContext context)
    {
        string pid = Id;
        Action Guard(Action run) => () =>
        {
            try { run(); }
            catch (Exception ex) { context.Log($"Terrain command failed: {ex.Message}"); }
        };

        void Add(string id, string label, Action run, Func<string>? dynamicLabel = null)
            => PluginCommands.Register(new PluginCommand
            {
                PluginId = pid,
                Id = id,
                Label = label,
                DynamicLabel = dynamicLabel,
                Callback = Guard(run),
            });

        Add("lumo.terrain.generate", "Generate Terrain", () =>
        {
            var b = RequireBridge(context);
            if (b != null) TerrainSystem.Generate(b);
        });

        Add("lumo.terrain.import", "Import Heightmap…", async () =>
        {
            var b = RequireBridge(context);
            if (b != null) await TerrainSystem.ImportAsync(b);
        });

        Add("lumo.terrain.save", "Save Heightmap", () =>
        {
            var b = RequireBridge(context);
            if (b != null) TerrainSystem.Save(b);
        });

        Add("lumo.terrain.sync", "Sync Terrain", () =>
        {
            var b = RequireBridge(context);
            if (b != null) TerrainSystem.SyncEntities(b, replace: false);
        });

        foreach (string brush in new[] { "raise", "lower", "smooth", "flatten" })
        {
            string label = char.ToUpperInvariant(brush[0]) + brush[1..];
            Add($"lumo.terrain.brush.{brush}", label,
                () => TerrainSystem.SetBrush(TerrainSystem.ActiveBrush == brush ? null : brush),
                () => (TerrainSystem.ActiveBrush == brush ? "● " : "") + label + $" r{TerrainSystem.BrushRadius:0.#}");
        }

        Add("lumo.terrain.radius.dec", "Radius −", () => TerrainSystem.AdjustRadius(-1.5f));
        Add("lumo.terrain.radius.inc", "Radius +", () => TerrainSystem.AdjustRadius(1.5f));
    }

    private static IHostBridge? RequireBridge(PluginContext context)
    {
        var bridge = context.Bridge;
        if (bridge == null) context.Log("Terrain needs an editor host (no bridge available).");
        return bridge;
    }
}
