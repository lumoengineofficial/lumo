using System.Numerics;

namespace Lumo.Engine.VisualScripting;

/// <summary>A screen-space text entry drawn by the game view.</summary>
public sealed record HudEntry(string Id, string Text, float X, float Y, float Size, Vector3 Color);

/// <summary>
/// Text overlay state: nodes write entries (keyed by id), the game view
/// renders them after the scene. X/Y are 0..1 fractions of the screen,
/// Size is font size in pixels.
/// </summary>
public sealed class HudLayer
{
    private readonly Dictionary<string, HudEntry> _entries = new(StringComparer.Ordinal);

    public void Set(string id, string text, float x, float y, float size, Vector3 color)
    {
        if (string.IsNullOrEmpty(id)) id = "hud";
        _entries[id] = new HudEntry(id, text ?? "", x, y, MathF.Max(4f, size), color);
    }

    public void Remove(string id) => _entries.Remove(id);

    public void ClearAll() => _entries.Clear();

    public HudEntry? Get(string id) => _entries.GetValueOrDefault(id);

    public IReadOnlyCollection<HudEntry> Entries => _entries.Values;

    public int Count => _entries.Count;
}
