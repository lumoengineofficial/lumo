using System.Reflection;
using System.Runtime.Loader;
using Lumo.Engine.VisualScripting;

namespace Lumo.Plugins;

/// <summary>Outcome of one plugin entry-point run inside a loaded assembly.</summary>
public sealed record LoadedPlugin(
    string FilePath,
    string? Id,
    string? Name,
    string? Version,
    bool Success,
    string? Error);

/// <summary>
/// Discovers plugin DLLs in a directory, loads each into a private collectible
/// <see cref="AssemblyLoadContext"/> and runs every <see cref="IPlugin"/> it
/// declares. Load contract types (IPlugin, VSNode, ...) come from the host
/// context so type identity never splits. A plugin that throws is recorded and
/// logged but never aborts the remaining plugins or the host.
/// </summary>
public sealed class PluginLoader
{
    private readonly object _gate = new();
    private readonly HashSet<string> _loadedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LoadedPlugin> _plugins = [];
    private readonly List<string> _messages = [];
    private readonly List<Action<string>> _sinks = [];
    private readonly List<PluginLoadContext> _contexts = [];
    private readonly List<IPlugin> _instances = [];

    public IReadOnlyList<LoadedPlugin> Plugins
    {
        get { lock (_gate) return [.. _plugins]; }
    }

    public IReadOnlyList<string> Messages
    {
        get { lock (_gate) return [.. _messages]; }
    }

    /// <summary>Subscribes to log lines; replays everything recorded so far.</summary>
    public void AttachSink(Action<string> sink)
    {
        List<string> replay;
        lock (_gate)
        {
            if (_sinks.Contains(sink)) return;
            _sinks.Add(sink);
            replay = [.. _messages];
        }
        foreach (string message in replay)
            sink(message);
    }

    public void DetachSink(Action<string> sink)
    {
        lock (_gate) _sinks.Remove(sink);
    }

    /// <summary>Loads every top-level *.dll in the directory (missing dir = no-op).</summary>
    public void LoadDirectory(string directory, string? projectDirectory = null)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string file in Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
            LoadFile(file, projectDirectory);
    }

    public void LoadFile(string file, string? projectDirectory = null)
    {
        string full = Path.GetFullPath(file);
        lock (_gate)
        {
            if (!_loadedFiles.Add(full)) return;
        }

        var context = new PluginLoadContext(full);
        try
        {
            // Stream-load: the DLL is copied into memory and never locked,
            // so hosts keep running while a plugin is rebuilt on disk.
            Assembly assembly;
            using (FileStream stream = File.OpenRead(full))
                assembly = context.LoadFromStream(stream);

            // Our own contract assembly sitting in the folder is not a plugin.
            if (full.Equals(typeof(PluginLoader).Assembly.Location, StringComparison.OrdinalIgnoreCase))
            {
                Discard(full, context);
                return;
            }

            // Only assemblies that reference the Lumo contract are candidates;
            // neighbouring dependency DLLs are skipped without noise.
            if (!ReferencesPluginContract(assembly))
            {
                Discard(full, context);
                return;
            }

            // Node-library DLLs work with zero code: scan first so plugin types
            // are registered even if a later Load() fails.
            NodeRegistry.ScanAssembly(assembly);
            lock (_gate) _contexts.Add(context);

            Type[] types = SafeGetTypes(assembly);
            var pluginTypes = types
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(t))
                .ToList();

            if (pluginTypes.Count == 0)
            {
                Emit($"Node library loaded: {Path.GetFileName(full)}");
                return;
            }

            var pluginContext = new PluginContext(assembly, Path.GetDirectoryName(full)!, projectDirectory, Emit);
            foreach (Type type in pluginTypes)
            {
                IPlugin? plugin = null;
                try
                {
                    plugin = (IPlugin)Activator.CreateInstance(type)!;
                    plugin.Load(pluginContext);
                    lock (_gate)
                    {
                        _plugins.Add(new LoadedPlugin(full, plugin.Id, plugin.Name, plugin.Version, true, null));
                        _instances.Add(plugin);
                    }
                    Emit($"Plugin loaded: {plugin.Name} {plugin.Version} ({plugin.Id})");
                }
                catch (Exception ex)
                {
                    // Load() failed: keep the entry so callers can inspect it.
                    string id = plugin?.Id ?? type.FullName ?? type.Name;
                    string name = plugin?.Name ?? type.Name;
                    lock (_gate)
                        _plugins.Add(new LoadedPlugin(full, id, name, plugin?.Version, false, ex.Message));
                    Emit($"Plugin failed: {name} — {ex.Message}");
                }
            }
        }
        catch (BadImageFormatException)
        {
            // Native/unmanaged DLL beside a plugin — not ours, ignore quietly.
            Discard(full, context);
        }
        catch (Exception ex)
        {
            // Remember the failure so a rebuilt DLL can be retried.
            Discard(full, context);
            lock (_gate)
                _plugins.Add(new LoadedPlugin(full, null, Path.GetFileName(full), null, false, ex.Message));
            Emit($"Plugin load failed: {Path.GetFileName(full)} — {ex.Message}");
        }
    }

    /// <summary>Runs Unload() on every plugin and releases the private contexts.</summary>
    public void UnloadAll()
    {
        List<IPlugin> instances;
        List<PluginLoadContext> contexts;
        lock (_gate)
        {
            instances = [.. _instances];
            contexts = [.. _contexts];
            _plugins.Clear();
            _instances.Clear();
            _contexts.Clear();
            _loadedFiles.Clear();
        }
        foreach (IPlugin plugin in instances)
        {
            try { plugin.Unload(); }
            catch (Exception ex) { Emit($"Plugin unload failed: {plugin.Id} — {ex.Message}"); }
        }
        foreach (PluginLoadContext context in contexts)
            context.Unload();
    }

    private void Discard(string file, PluginLoadContext context)
    {
        lock (_gate) _loadedFiles.Remove(file);
        context.Unload();
    }

    private void Emit(string message)
    {
        Action<string>[] sinks;
        lock (_gate)
        {
            _messages.Add(message);
            sinks = [.. _sinks];
        }
        foreach (Action<string> sink in sinks)
        {
            try { sink(message); }
            catch { /* a broken log sink must not break loading */ }
        }
    }

    private static bool ReferencesPluginContract(Assembly assembly)
    {
        try
        {
            return assembly.GetReferencedAssemblies()
                .Any(name => name.Name is "Lumo.Plugins" or "Lumo.Engine");
        }
        catch
        {
            return false;
        }
    }

    private static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return [.. ex.Types.OfType<Type>()];
        }
    }

    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;

        public PluginLoadContext(string mainAssemblyPath)
            : base(Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: true)
            => _directory = Path.GetDirectoryName(mainAssemblyPath)!;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Contract and engine types must resolve to the HOST copies —
            // otherwise IPlugin would be a different type per context.
            if (assemblyName.Name is null or "Lumo.Plugins" or "Lumo.Engine")
                return null;

            string candidate = Path.Combine(_directory, assemblyName.Name + ".dll");
            if (!File.Exists(candidate)) return null;
            using FileStream stream = File.OpenRead(candidate);
            return LoadFromStream(stream);
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            foreach (string candidate in new[]
                     {
                         Path.Combine(_directory, unmanagedDllName),
                         Path.Combine(_directory, unmanagedDllName + ".dll")
                     })
            {
                if (File.Exists(candidate))
                    return LoadUnmanagedDllFromPath(candidate);
            }
            return 0;
        }
    }
}
