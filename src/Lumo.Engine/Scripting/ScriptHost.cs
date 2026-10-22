using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Entity = Lumo.Engine.Scene.Entity;
using GameScene = Lumo.Engine.Scene.Scene;
using InputState = Lumo.Engine.Input.InputState;

namespace Lumo.Engine.Scripting;

/// <summary>
/// Compiles user C# scripts at runtime with Roslyn, binds them to
/// entities, and drives their lifecycle (OnStart / OnUpdate / OnDestroy).
/// </summary>
public sealed class ScriptHost
{
    private readonly List<LumoScript> _instances = [];
    private readonly List<(LumoScript script, Entity entity)> _bindings = [];
    private Assembly? _assembly;
    private bool _started;

    public static event Action<string>? MessageLogged;

    /// <summary>
    /// HUD layer scripts write to. The game runtime wires this to the
    /// active HudLayer before scripts start; null outside play mode.
    /// </summary>
    public static VisualScripting.HudLayer? Hud { get; set; }

    public IReadOnlyList<string> Errors => _errors;
    private readonly List<string> _errors = [];
    public bool IsCompiled => _assembly != null;
    public bool IsRunning => _started;
    public int InstanceCount => _instances.Count;
    public InputState? Input { get; set; }

    public static void Log(string message) => MessageLogged?.Invoke(message);

    /// <summary>
    /// Compile all .cs sources into an in-memory assembly.
    /// Returns true on success; diagnostics are available in Errors.
    /// </summary>
    public bool Compile(IEnumerable<string> sources)
    {
        _errors.Clear();
        _assembly = null;

        var trees = sources
            .Select((src, i) => CSharpSyntaxTree.ParseText(src, path: $"Script{i}.cs"))
            .ToList();

        if (trees.Count == 0)
        {
            _errors.Add("No script sources to compile.");
            return false;
        }

        var references = BuildReferences();

        var compilation = CSharpCompilation.Create(
            $"LumoScripts_{Guid.NewGuid():N}",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);

        foreach (var diag in emitResult.Diagnostics)
        {
            if (diag.Severity == DiagnosticSeverity.Error)
                _errors.Add(diag.ToString());
        }

        if (!emitResult.Success)
            return false;

        _assembly = Assembly.Load(stream.ToArray());
        return true;
    }

    /// <summary>
    /// Instantiate scripts for every entity that has a ScriptComponent.
    /// Call after Compile; safe to call again after recompiles.
    /// </summary>
    public void Bind(GameScene scene)
    {
        DisposeInstances();

        if (_assembly == null)
            return;

        var scriptTypes = _assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } &&
                        typeof(LumoScript).IsAssignableFrom(t))
            .ToList();

        var byName = scriptTypes.ToDictionary(t => t.FullName ?? t.Name, StringComparer.Ordinal);

        foreach (var entity in scene.AllEntities)
        {
            var comp = entity.Scripts;
            if (comp == null || !comp.Enabled) continue;

            foreach (var name in comp.ScriptNames)
            {
                if (!byName.TryGetValue(name, out var type))
                {
                    _errors.Add($"Script '{name}' not found (entity '{entity.Name}').");
                    continue;
                }

                if (Activator.CreateInstance(type) is not LumoScript instance)
                {
                    _errors.Add($"Failed to create instance of '{name}'.");
                    continue;
                }

                instance.Entity = entity;
                instance.Input = Input;
                _instances.Add(instance);
                _bindings.Add((instance, entity));
            }
        }
    }

    /// <summary>Start lifecycle: invokes OnStart on all bound scripts.</summary>
    public void Start(float time = 0f)
    {
        if (_started) return;
        _started = true;

        foreach (var script in _instances)
        {
            script.Time = time;
            try { script.OnStart(); }
            catch (Exception ex) { ReportScriptError(script, ex); }
        }
    }

    /// <summary>Advance all bound scripts by one frame.</summary>
    public void Update(float deltaTime, float time)
    {
        if (!_started) return;

        foreach (var (script, entity) in _bindings)
        {
            if (!entity.IsActive) continue;
            script.DeltaTime = deltaTime;
            script.Time = time;
            script.Input = Input;
            try { script.OnUpdate(deltaTime); }
            catch (Exception ex) { ReportScriptError(script, ex); }
        }
    }

    /// <summary>Stop lifecycle: invokes OnDestroy on all bound scripts.</summary>
    public void Stop()
    {
        if (!_started) return;
        _started = false;

        foreach (var script in _instances)
        {
            try { script.OnDestroy(); }
            catch (Exception ex) { ReportScriptError(script, ex); }
        }
    }

    /// <summary>Query a running script instance by class name.</summary>
    public T? GetInstance<T>() where T : LumoScript
        => _instances.OfType<T>().FirstOrDefault();

    private void ReportScriptError(LumoScript script, Exception ex)
    {
        var name = script.GetType().Name;
        if (!_errors.Contains($"[{name}] {ex.Message}"))
            _errors.Add($"[{name}] {ex.Message}");
        Log($"[Script Error] {name}: {ex.Message}");
    }

    private void DisposeInstances()
    {
        if (_started)
        {
            foreach (var s in _instances)
            {
                try { s.OnDestroy(); } catch { }
            }
            _started = false;
        }
        _instances.Clear();
        _bindings.Clear();
    }

    private static IReadOnlyList<MetadataReference> BuildReferences()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refs = new List<MetadataReference>();

        void Add(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (!seen.Add(path)) return;
            refs.Add(MetadataReference.CreateFromFile(path));
        }

        // Runtime assemblies (all loaded + trusted platform assemblies).
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (tpa != null)
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                Add(path);
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try { Add(asm.Location); } catch { }
        }

        // Engine itself (LumoScript, components, math).
        Add(typeof(LumoScript).Assembly.Location);

        return refs;
    }
}
