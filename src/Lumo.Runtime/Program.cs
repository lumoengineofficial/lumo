using Avalonia;
using Lumo.Runtime;

namespace Lumo.Runtime;

public static class Program
{
    public static string[] Args = [];

    [STAThread]
    public static void Main(string[] args)
    {
        Args = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<GameApp>().UsePlatformDetect();
}
