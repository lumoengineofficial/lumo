using Avalonia.Controls;
using Lumo.Runtime;

namespace Lumo.Editor.Views;

/// <summary>
/// Standalone game window: loads the project through GameRuntime and runs the
/// full game loop (graphs, scripts, input) in its own window, while the editor
/// stays in edit mode.
/// </summary>
public sealed class GamePlayWindow : Window
{
    private readonly GameRuntime _runtime;
    private readonly GameView _view;
    private bool _stoppedRaised;

    public event Action<string>? LogLine;
    public event Action? Stopped;

    public GameRuntime Runtime => _runtime;

    public GamePlayWindow(string projectPath, string projectName)
    {
        Title = $"Play - {projectName}";
        Width = 1280;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _runtime = new GameRuntime();
        _runtime.MessageLogged += msg => LogLine?.Invoke(msg);
        _runtime.LoadProject(projectPath);

        _view = new GameView(_runtime);
        Content = _view;

        Opened += (_, _) =>
        {
            if (_runtime.LoadError != null)
            {
                LogLine?.Invoke($"Play: {_runtime.LoadError}");
                return;
            }
            try
            {
                // GameView shows the loading screen, then starts the runtime itself.
                _view.BeginLoop();
            }
            catch (Exception ex)
            {
                LogLine?.Invoke($"Play start failed: {ex.Message}");
            }
        };

        Closed += (_, _) =>
        {
            try { _view.Shutdown(); } catch { /* already disposed */ }
            if (!_stoppedRaised)
            {
                _stoppedRaised = true;
                Stopped?.Invoke();
            }
        };
    }
}
