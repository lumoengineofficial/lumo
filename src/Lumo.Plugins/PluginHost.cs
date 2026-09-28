namespace Lumo.Plugins;

/// <summary>
/// Process-wide plugin entry: one shared <see cref="PluginLoader"/> with
/// append-only sinks (editor Output panel, game console, ...). Hosts call
/// <see cref="LoadDefault"/> once per project; repeated calls skip files
/// that are already loaded.
/// </summary>
public static class PluginHost
{
    public static PluginLoader Loader { get; } = new();

    public static IReadOnlyList<LoadedPlugin> Plugins => Loader.Plugins;

    public static IReadOnlyList<string> Messages => Loader.Messages;

    public static void AttachSink(Action<string> sink) => Loader.AttachSink(sink);

    public static void DetachSink(Action<string> sink) => Loader.DetachSink(sink);

    /// <summary>
    /// Loads the engine-level Plugins folder next to the executable and, when a
    /// project is given, its Plugins folder too.
    /// </summary>
    public static void LoadDefault(string? projectDirectory = null)
    {
        Loader.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "Plugins"), projectDirectory);
        if (!string.IsNullOrEmpty(projectDirectory))
            Loader.LoadDirectory(Path.Combine(projectDirectory, "Plugins"), projectDirectory);
    }
}
