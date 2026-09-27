using Lumo.Engine.Input;
using Lumo.Engine.Scene;
using GameScene = Lumo.Engine.Scene.Scene;

namespace Lumo.Engine.VisualScripting;

/// <summary>
/// Shared key-value store available to every graph (global scope).
/// </summary>
public sealed class Blackboard
{
    public static Blackboard Shared { get; } = new();

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Values => _values;

    public void Set(string name, string value) => _values[name] = value;

    public string Get(string name) => _values.TryGetValue(name, out string? value) ? value : "";

    public bool Has(string name) => _values.ContainsKey(name);

    public void Clear() => _values.Clear();
}

/// <summary>
/// Execution context passed to nodes during a dispatch: scene bindings,
/// timing, per-graph locals, exec-flow emission and data resolution
/// (connected pins pull-evaluate, otherwise the literal value converts).
/// </summary>
public sealed class GraphContext
{
    internal GraphInterpreter Interpreter = null!;

    public VisualGraph Graph { get; internal set; } = null!;
    public Entity? Self { get; internal set; }
    public GameScene? Scene { get; internal set; }
    public InputState? Input { get; internal set; }
    public Blackboard Blackboard { get; internal set; } = Blackboard.Shared;
    public Dictionary<string, string> Locals { get; internal set; } = new();
    public float DeltaTime { get; internal set; }
    public float Time { get; internal set; }

    /// <summary>Exec input pin that triggered the current node (Do Once reset detection).</summary>
    public string? CurrentExecPin { get; internal set; }

    private readonly List<string> _pendingExec = [];

    /// <summary>Queues an exec output pin to run after the current node returns.</summary>
    public void Emit(string execPin) => _pendingExec.Add(execPin);

    internal void ClearPending() => _pendingExec.Clear();

    internal List<string> TakePending()
    {
        var taken = new List<string>(_pendingExec);
        _pendingExec.Clear();
        return taken;
    }

    internal bool IsBlackboardScope(VSNode node) =>
        string.Equals(Get<string>(node, "scope"), "Blackboard", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a pin: pull-evaluates the connected output, else converts the literal.</summary>
    public object? GetValue(VSNode node, string pin)
    {
        Connection? conn = Graph.FindInputConnection(node.Id, pin);
        if (conn is not null)
        {
            VSNode? source = Graph.FindNode(conn.FromNode);
            return source is null ? null : Interpreter.Evaluate(source, conn.FromPin, this);
        }
        if (node.GetPin(pin) is Pin p && p.Kind == PinKind.Data && node.Values.TryGetValue(pin, out string? raw))
            return PinConvert.ToType(raw, p.DataType);
        return null;
    }

    /// <summary>Typed pin read used by node implementations.</summary>
    public T Get<T>(VSNode node, string pin) => PinConvert.Coerce<T>(GetValue(node, pin));

    /// <summary>Writes a message through the interpreter's console channel.</summary>
    public void Log(string message) => Interpreter.Log(message);
}

/// <summary>
/// Tree-walk graph interpreter: events dispatch exec pins depth-first
/// (push-traverse), data pins pull-evaluate on demand with per-frame
/// memoization and cycle detection.
/// </summary>
public sealed class GraphInterpreter
{
    /// <summary>Guard against exec-flow infinite loops within one dispatch.</summary>
    public const int MaxStepsPerDispatch = 2000;

    private sealed class RunState
    {
        public Dictionary<string, string> Locals { get; } = new(StringComparer.Ordinal);
    }

    private readonly List<VisualGraph> _graphs = [];
    private readonly Dictionary<VisualGraph, RunState> _states = new();
    private readonly HashSet<string> _activeNodes = new(StringComparer.Ordinal);
    private readonly Dictionary<(VisualGraph Graph, string Node, string Pin), object?> _memo = new();
    private readonly HashSet<(VisualGraph Graph, string Node, string Pin)> _visiting = new();
    private readonly List<string> _errors = [];
    private int _steps;
    private float _lastDelta;

    public Entity? Self { get; set; }
    public GameScene? Scene { get; set; }
    public InputState? Input { get; set; }

    /// <summary>Base folder for relative paths (project dir); hosts set this.</summary>
    public string? BaseDirectory { get; set; }

    public Blackboard Blackboard { get; } = Blackboard.Shared;
    public float Time { get; private set; }

    /// <summary>Node ids executed during the last dispatch (debug overlay).</summary>
    public IReadOnlyCollection<string> ActiveNodes => _activeNodes;

    public IReadOnlyList<string> Errors => _errors;
    public IReadOnlyList<VisualGraph> Graphs => _graphs;

    public event Action<string>? MessageLogged;
    public event Action? FrameDispatched;

    public void AddGraph(VisualGraph graph)
    {
        _graphs.Add(graph);
        _states[graph] = new RunState();
    }

    public void Clear()
    {
        _graphs.Clear();
        _states.Clear();
        _activeNodes.Clear();
        _errors.Clear();
    }

    /// <summary>Seeds locals from declared variables, resets node state, fires "start".</summary>
    public void Start()
    {
        Time = 0f;
        _lastDelta = 0f;
        Blackboard.Clear();
        foreach ((VisualGraph graph, RunState state) in _states)
        {
            state.Locals.Clear();
            foreach (GraphVariable variable in graph.Variables)
            {
                if (variable.Scope == VariableScope.Local)
                    state.Locals[variable.Name] = variable.DefaultValue;
            }
        }
        foreach (VisualGraph graph in _graphs)
        {
            foreach (VSNode node in graph.Nodes)
                node.Reset();
        }
        BeginDispatch();
        DispatchEvent("start");
        DispatchEvent("key");
        FrameDispatched?.Invoke();
    }

    public void Tick(float deltaTime)
    {
        Time += deltaTime;
        _lastDelta = deltaTime;
        BeginDispatch();
        DispatchEvent("key");
        DispatchEvent("tick");
        FrameDispatched?.Invoke();
    }

    private void BeginDispatch()
    {
        _memo.Clear();
        _visiting.Clear();
        _activeNodes.Clear();
        _errors.Clear();
        _steps = 0;
    }

    private void DispatchEvent(string eventName)
    {
        foreach (VisualGraph graph in _graphs)
        {
            var ctx = new GraphContext
            {
                Interpreter = this,
                Graph = graph,
                Self = Self,
                Scene = Scene,
                Input = Input,
                Blackboard = Blackboard,
                Locals = _states[graph].Locals,
                DeltaTime = _lastDelta,
                Time = Time
            };
            foreach (VSNode node in graph.Nodes)
            {
                if (node is EventNode ev && string.Equals(ev.EventName, eventName, StringComparison.Ordinal))
                    ExecuteNode(graph, node, ctx, null);
            }
        }
    }

    private void ExecuteNode(VisualGraph graph, VSNode node, GraphContext ctx, string? viaPin)
    {
        if (++_steps > MaxStepsPerDispatch)
        {
            AddError($"Step limit ({MaxStepsPerDispatch}) exceeded - possible infinite exec loop.");
            return;
        }

        _activeNodes.Add(node.Id);
        ctx.CurrentExecPin = viaPin;
        ctx.ClearPending();
        try
        {
            node.Execute(ctx);
        }
        catch (Exception ex)
        {
            AddError($"Node '{node.TypeId}' threw: {ex.Message}");
            return;
        }

        foreach (string pin in ctx.TakePending())
        {
            foreach (Connection conn in graph.FindOutputConnections(node.Id, pin))
            {
                if (graph.FindNode(conn.ToNode) is VSNode target)
                    ExecuteNode(graph, target, ctx, conn.ToPin);
            }
        }
    }

    /// <summary>Pull-evaluates a pure output pin with memoization and cycle detection.</summary>
    public object? Evaluate(VSNode node, string pin, GraphContext ctx)
    {
        var key = (ctx.Graph, node.Id, pin);
        if (_memo.TryGetValue(key, out object? cached))
            return cached;
        if (!_visiting.Add(key))
        {
            AddError($"Data cycle detected at '{node.TypeId}.{pin}'.");
            return null;
        }

        object? value;
        try
        {
            value = node.EvaluateOutput(ctx, pin);
        }
        catch (Exception ex)
        {
            AddError($"Node '{node.TypeId}' threw: {ex.Message}");
            value = null;
        }
        _visiting.Remove(key);
        _memo[key] = value;
        return value;
    }

    /// <summary>Routes a console message to subscribers (editor console, tests).</summary>
    public void Log(string message) => MessageLogged?.Invoke(message);

    private void AddError(string message)
    {
        if (!_errors.Contains(message))
            _errors.Add(message);
    }
}
