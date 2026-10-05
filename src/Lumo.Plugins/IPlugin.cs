using System.Reflection;
using Lumo.Engine.VisualScripting;

namespace Lumo.Plugins;

/// <summary>
/// Entry point of a plugin assembly. A plugin DLL may contain several
/// implementations; each one gets its own <see cref="PluginContext"/>.
/// Assemblies that only ship <see cref="GraphNodeAttribute"/> node types
/// load fine without an <see cref="IPlugin"/> implementation.
/// </summary>
public interface IPlugin
{
    /// <summary>Stable unique id, e.g. "acme.physics".</summary>
    string Id { get; }

    string Name { get; }

    string Version { get; }

    void Load(PluginContext context);

    void Unload() { }
}

/// <summary>Services handed to a plugin while it loads.</summary>
public sealed class PluginContext
{
    private readonly Action<string> _log;

    internal PluginContext(Assembly assembly, string pluginDirectory, string? projectDirectory, Action<string> log)
    {
        PluginAssembly = assembly;
        PluginDirectory = pluginDirectory;
        ProjectDirectory = projectDirectory;
        _log = log;
    }

    /// <summary>The plugin's own assembly (loaded in its private context).</summary>
    public Assembly PluginAssembly { get; }

    /// <summary>Directory the plugin DLL was loaded from.</summary>
    public string PluginDirectory { get; }

    /// <summary>Host project directory (project plugins) or null for engine-level plugins.</summary>
    public string? ProjectDirectory { get; }

    /// <summary>Writes a line to the host's plugin log (editor Output panel, game console).</summary>
    public void Log(string message) => _log(message);

    /// <summary>Host services (scene, undo, file dialogs); may be null in bare hosts.</summary>
    public IHostBridge? Bridge => PluginHost.Bridge;

    /// <summary>Registers every [GraphNode] type in the plugin assembly.</summary>
    public void RegisterNodes() => NodeRegistry.ScanAssembly(PluginAssembly);

    /// <summary>Registers a single node type at runtime.</summary>
    public void RegisterNode<T>() where T : VSNode => NodeRegistry.Register<T>();
}
