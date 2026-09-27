using Lumo.Editor.Editing;

namespace Lumo.Tests;

public class UndoHistoryTests
{
    [Fact]
    public void PushUndoRedo_RestoresInOrder()
    {
        var h = new HistoryStack();
        Assert.False(h.CanUndo);
        Assert.False(h.CanRedo);

        h.Push("A");           // about to change A -> B
        Assert.True(h.CanUndo);

        string? undone = h.Undo("B");   // back from B to A
        Assert.Equal("A", undone);
        Assert.False(h.CanUndo);
        Assert.True(h.CanRedo);

        string? redone = h.Redo("A");   // forward A -> B again
        Assert.Equal("B", redone);
        Assert.True(h.CanUndo);
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void NewMutation_ClearsRedoStack()
    {
        var h = new HistoryStack();
        h.Push("A");
        h.Undo("B");
        Assert.True(h.CanRedo);

        h.Push("C"); // new branch: state C was saved before mutating to D
        Assert.False(h.CanRedo);
        Assert.Equal("C", h.Undo("D"));
    }

    [Fact]
    public void Capacity_TrimsOldestSnapshots()
    {
        var h = new HistoryStack(capacity: 3);
        for (int i = 0; i < 10; i++)
            h.Push($"s{i}");

        // oldest were trimmed; the newest snapshot (s9) comes back first
        Assert.Equal("s9", h.Undo("now"));
        Assert.Equal("s8", h.Undo("now"));
        Assert.Equal("s7", h.Undo("now"));
        Assert.Null(h.Undo("now"));
    }
}
