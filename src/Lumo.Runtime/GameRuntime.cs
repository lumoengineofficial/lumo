using Lumo.Engine.Core;
using Lumo.Engine.Input;
using Lumo.Engine.Scene;
using Lumo.Engine.Scripting;
using Lumo.Engine.VisualScripting;
using System.Text.Json;

namespace Lumo.Runtime;

/// <summary>
/// Standalone game runtime: loads a Lumo project (scene + visual graphs +
/// C# scripts) and drives the engine loop. Rendering and input live in GameView.
/// </summary>
public sealed class GameRuntime : IDisposable
{
    private LumoEngine? _engine;
    private readonly InputState _input = new();
    private readonly ScriptHost _scripts = new();
    private readonly List<string> _graphFiles = [];
    private readonly List<string> _scriptSources = [];
    private readonly HashSet<string> _loggedErrors = new(StringComparer.Ordinal);
    private GraphInterpreter? _graphs;
    private bool _started;

    public Scene Scene { get; private set; } = new() { Name = "Empty" };
    public InputState Input => _input;
    public string Title { get; private set; } = "Lumo Game";
    public string? ProjectDir { get; private set; }
    public string? LoadError { get; set; }
    public int GraphCount { get; private set; }
    public int ScriptInstanceCount { get; private set; }
    public IReadOnlyList<string> GraphErrors => _graphs?.Errors ?? [];
    public bool IsRunning => _started;

    /// <summary>Graph interpreter for live debug attach (null before Start).</summary>
    public GraphInterpreter? Graphs => _graphs;

    private static readonly HudLayer _emptyHud = new();

    /// <summary>Screen-space HUD entries drawn by the game view.</summary>
    public HudLayer Hud => _graphs?.Hud ?? _emptyHud;

    /// <summary>Log lines from graphs/scripts (also written to the console).</summary>
    public event Action<string>? MessageLogged;

    private void Log(string message)
    {
        Console.WriteLine(message);
        MessageLogged?.Invoke(message);
    }

    // ------------------------------------------------------------ loading

    /// <summary>Load a project directory (Project.json, Scenes/, Graphs/, Scripts/).</summary>
    public void LoadProject(string projectDir)
    {
        try
        {
            ProjectDir = projectDir;

            string projectFile = Path.Combine(projectDir, "Project.json");
            if (File.Exists(projectFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(projectFile));
                if (doc.RootElement.TryGetProperty("Name", out var name) &&
                    name.GetString() is { Length: > 0 } projectName)
                    Title = projectName;
            }

            string scenesDir = Path.Combine(projectDir, "Scenes");
            if (Directory.Exists(scenesDir))
            {
                string[] scenes = Directory.GetFiles(scenesDir, "*.json")
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (scenes.Length > 0)
                    Scene = Scene.Load(scenes[0]);
            }

            string graphsDir = Path.Combine(projectDir, "Graphs");
            if (Directory.Exists(graphsDir))
                _graphFiles.AddRange(Directory.GetFiles(graphsDir, "*.graph.json"));

            string scriptsDir = Path.Combine(projectDir, "Scripts");
            if (Directory.Exists(scriptsDir))
            {
                foreach (string file in Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
                    _scriptSources.Add(File.ReadAllText(file));
            }

            Log($"Loaded project '{Title}': {Scene.AllEntities.Count} entities, " +
                $"{_graphFiles.Count} graph(s), {_scriptSources.Count} script(s).");
        }
        catch (Exception ex)
        {
            LoadError = $"Project load failed: {ex.Message}";
            Log(LoadError);
        }
    }

    /// <summary>Load a single scene file (no project context).</summary>
    public void LoadSceneFile(string scenePath)
    {
        try
        {
            Scene = Scene.Load(scenePath);
            Title = Path.GetFileNameWithoutExtension(scenePath);
            Log($"Loaded scene: {Scene.Name} ({Scene.AllEntities.Count} entities).");
        }
        catch (Exception ex)
        {
            LoadError = $"Scene load failed: {ex.Message}";
            Log(LoadError);
        }
    }

    // ------------------------------------------------------------ lifecycle

    public void Start()
    {
        if (_started) return;

        _engine = new LumoEngine(new EngineConfiguration { Name = Title });
        _engine.Initialize();
        _engine.Start();

        _graphs = new GraphInterpreter
        {
            Scene = Scene,
            Input = _input,
            Self = null,
            BaseDirectory = ProjectDir
        };
        _graphs.MessageLogged += Log;

        foreach (string file in _graphFiles)
        {
            try
            {
                _graphs.AddGraph(VisualGraph.Load(file));
                GraphCount++;
            }
            catch (Exception ex)
            {
                Log($"Graph load failed ({Path.GetFileName(file)}): {ex.Message}");
            }
        }
        _graphs.Start();

        if (_scriptSources.Count > 0)
        {
            if (_scripts.Compile(_scriptSources))
            {
                _scripts.Input = _input;
                _scripts.Bind(Scene);
                _scripts.Start(0f);
                ScriptInstanceCount = _scripts.InstanceCount;
                Log($"Scripts started: {ScriptInstanceCount} instance(s).");
            }
            else
            {
                foreach (string err in _scripts.Errors)
                    Log($"CS: {err}");
                Log($"Script compile failed ({_scripts.Errors.Count} error(s)).");
            }
        }

        _started = true;
        Log("Game started.");
    }

    /// <summary>Advance one frame: graphs, then input edge reset, then scripts.</summary>
    public void Tick()
    {
        if (!_started || _engine == null) return;

        float dt = _engine.Tick();

        if (_graphs != null)
        {
            _graphs.Tick(dt);
            foreach (string err in _graphs.Errors)
            {
                if (_loggedErrors.Add(err))
                    Log($"VS: {err}");
            }
        }

        _input.BeginFrame();

        if (_scripts.IsRunning)
            _scripts.Update(dt, (float)_engine.Time.ElapsedTime);
    }

    public void Dispose()
    {
        if (_started && _scripts.IsRunning)
            _scripts.Stop();
        _engine?.Dispose();
        _engine = null;
        _started = false;
    }
}
