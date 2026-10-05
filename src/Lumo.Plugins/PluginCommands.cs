namespace Lumo.Plugins;

/// <summary>
/// Host services exposed to plugins (editor implements it; runtime may skip it).
/// Lets plugin commands create scene entities, push undo, save and open files.
/// </summary>
public interface IHostBridge
{
    /// <summary>Project root of the open project, or null when none.</summary>
    string? ProjectPath { get; }

    void Log(string message);

    /// <summary>Pushes a scene undo snapshot before a mutation.</summary>
    void PushUndo();

    void SaveProject();

    /// <summary>Refreshes hierarchy/inspector/file tree (must tolerate a not-yet-built UI).</summary>
    void RefreshUi();

    /// <summary>Creates an entity with a MeshRenderer for <paramref name="meshName"/>,
    /// or re-points the existing entity named <paramref name="entityName"/>.</summary>
    void CreateOrUpdateMeshEntity(string entityName, string meshName);

    /// <summary>True when an entity with this exact name already exists.</summary>
    bool EntityExists(string entityName);

    void DeleteEntitiesByPrefix(string prefix);

    /// <summary>Native open-file dialog; resolves to null when cancelled. UI-thread only.</summary>
    Task<string?> PickOpenFileAsync(string title, params (string Label, string[] Patterns)[] filters);
}

/// <summary>Registry of plugin-provided editor buttons. The host renders one
/// button per command (see the editor's plugin command bar) and can invoke
/// commands by id after project load.</summary>
public static class PluginCommands
{
    private static readonly List<PluginCommand> _commands = [];

    /// <summary>Raised whenever commands are registered or removed.</summary>
    public static event Action? Changed;

    public static IReadOnlyList<PluginCommand> Commands => _commands;

    /// <summary>Registers (or replaces, by id) a command and notifies the host.</summary>
    public static void Register(PluginCommand command)
    {
        _commands.RemoveAll(c => c.Id == command.Id);
        _commands.Add(command);
        Changed?.Invoke();
    }

    public static void UnregisterPlugin(string pluginId)
    {
        int removed = _commands.RemoveAll(c => c.PluginId == pluginId);
        if (removed > 0) Changed?.Invoke();
    }

    /// <summary>Runs the command with this id if it exists (used by hosts right
    /// after project load for restore-style commands).</summary>
    public static bool TryInvoke(string id)
    {
        foreach (var c in _commands)
        {
            if (c.Id != id) continue;
            c.Callback();
            return true;
        }
        return false;
    }
}

/// <summary>A single plugin button shown in the host UI.</summary>
public sealed class PluginCommand
{
    /// <summary>Owner plugin id (used to drop all commands of an unloaded plugin).</summary>
    public required string PluginId { get; init; }

    /// <summary>Stable unique id, e.g. "lumo.terrain.generate".</summary>
    public required string Id { get; init; }

    /// <summary>Button text.</summary>
    public required string Label { get; init; }

    /// <summary>Optional live label (e.g. toggled brush shows "● Raise"). Null = Label.</summary>
    public Func<string>? DynamicLabel { get; init; }

    public required Action Callback { get; init; }

    public string Display => DynamicLabel?.Invoke() ?? Label;
}
