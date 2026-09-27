using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Lumo.Runtime;

public sealed class GameApp : Application
{
    public override void Initialize()
    {
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Window window = GameHost.CreateWindow(Program.Args);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                if (window.Content is GameView gv)
                    gv.Shutdown();
                else
                    GameHost.Shutdown();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
