namespace Lumo.Editor.Editing;

/// <summary>
/// Snapshot-based undo/redo: callers push the state *before* each mutation
/// and receive the state to restore on undo/redo. States are JSON strings.
/// </summary>
public sealed class HistoryStack
{
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private readonly int _capacity;

    public HistoryStack(int capacity = 50)
    {
        _capacity = Math.Max(1, capacity);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void Push(string snapshotBeforeMutation)
    {
        _undo.Add(snapshotBeforeMutation);
        if (_undo.Count > _capacity)
            _undo.RemoveAt(0);
        _redo.Clear();
    }

    public string? Undo(string currentSnapshot)
    {
        if (_undo.Count == 0) return null;
        string previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(currentSnapshot);
        return previous;
    }

    public string? Redo(string currentSnapshot)
    {
        if (_redo.Count == 0) return null;
        string next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(currentSnapshot);
        return next;
    }
}
