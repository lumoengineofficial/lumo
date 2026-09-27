using Avalonia.Controls;

namespace Lumo.Runtime;

/// <summary>
/// Resolves what to run (project folder / scene file / project next to the
/// executable) and creates the game window.
/// </summary>
public static class GameHost
{
    private static GameRuntime? _runtime;

    public static Window CreateWindow(string[] args)
    {
        _runtime = new GameRuntime();
        string? projectDir = null;
        string? sceneFile = null;

        if (args.Length > 0)
        {
            string a = args[0];
            if (Directory.Exists(a) && File.Exists(Path.Combine(a, "Project.json")))
                projectDir = a;
            else if (File.Exists(a) && a.EndsWith("Project.json", StringComparison.OrdinalIgnoreCase))
                projectDir = Path.GetDirectoryName(a);
            else if (File.Exists(a) && a.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                sceneFile = a;
        }

        if (projectDir == null && sceneFile == null)
        {
            string local = Path.Combine(AppContext.BaseDirectory, "Project.json");
            if (File.Exists(local))
                projectDir = AppContext.BaseDirectory;
        }

        if (projectDir != null)
            _runtime.LoadProject(projectDir);
        else if (sceneFile != null)
            _runtime.LoadSceneFile(sceneFile);
        else
        {
            Console.WriteLine("No project found next to the executable and no arguments given.");
            _runtime.LoadError = "No project found (Project.json). Supply a project folder or scene file.";
        }

        var view = new GameView(_runtime);
        var window = new Window
        {
            Title = _runtime.Title,
            Width = 1280,
            Height = 720,
            Content = view
        };

        window.Opened += (_, _) =>
        {
            _runtime.Start();
            view.BeginLoop();
        };
        return window;
    }

    public static void Shutdown()
    {
        _runtime?.Dispose();
        _runtime = null;
    }
}
